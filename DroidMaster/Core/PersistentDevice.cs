using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Managed.Adb.Exceptions;
using Nito.AsyncEx;
using Renci.SshNet.Common;

namespace DroidMaster.Core {
	///<summary>
	/// Wraps an ephemeral connection to a device, automatically retrying 
	/// commands against a new connection from <see cref="PersistentDeviceManager"/> 
	/// if the connection fails.
	///</summary>
	///<remarks>
	/// A <see cref="PersistentDevice"/> is initially created with a connection.  It will hold
	/// this connection and run commands against it until a connection error is thrown.  After
	/// that happens, the connection is disposed and cleared, and subsequent commands will not
	/// run until a new connection arrives via <see cref="SetDevice(IDeviceConnection)"/>.  If
	/// <see cref="SetDevice(IDeviceConnection)"/> is called while connection is active, it'll
	/// wait for all commands against the previous connection to complete, and replace the old
	/// connection (and dispose it) with the new one.
	/// 
	/// This class maintains the following invariants:
	///  1. A new connection will not be installed until all operations running against the current connection finish.
	///  2. A connection will not be disposed until all operations running against the current connection finish.
	///  3. Any instance ever set to <see cref="volatileDeviceSource"/> will not dangle in the presence of a connection.
	///     (if an unresolved task is set, it will not be replaced.
	///  4. Any connection that is passed to <see cref="SetDevice(IDeviceConnection)"/> will be eventually be disposed,
	///     when it errors, is replaced with a new connection or when the entire class is disposed.
	///</remarks>
	class PersistentDevice : IDeviceConnection {
		///<summary>Controls reads and writes of <see cref="volatileDeviceSource"/>.</summary>
		readonly AsyncReaderWriterLock sourceLock = new AsyncReaderWriterLock();
		///<summary>The current device, if any, or a promise that will resolve to the current device once it arrives.</summary>
		///<remarks>
		/// It is always safe to wait for this promise, but you must enter
		/// a read lock and double-check the promise again once it arrives
		///</remarks>
		volatile TaskCompletionSource<IDeviceConnection> volatileDeviceSource;

		///<summary>Gets the persistent identifier for this device.</summary>
		public string ConnectionId { get; }


		public PersistentDevice(string id, IDeviceConnection initialConnection) {
			ConnectionId = id;
			volatileDeviceSource = CompletedSource(initialConnection);
		}


		public ICommandResult ExecuteShellCommand(string command) {
			var result = new ForwardingCommandResult();
			PropertyChangedEventHandler onPropertyChanged = (s, e) => result.OnPropertyChanged(e);
			result.Complete = Execute(c => {
				// Each time we re-execute the command, change our outer
				// result to reflect the result of the current execution
				if (result.Inner != null)
					result.Inner.PropertyChanged -= onPropertyChanged;
				result.Inner = c.ExecuteShellCommand(command);
				result.Inner.PropertyChanged += onPropertyChanged;
				return result.Inner.Complete;
			}).ContinueWith(_ => result.Output);
			return result;
		}

		class ForwardingCommandResult : ICommandResult {
			public ICommandResult Inner { get; set; }
			public Task<string> Complete { get; set; }

			public string Output => Inner.Output;

			public event PropertyChangedEventHandler PropertyChanged;
			public void OnPropertyChanged(PropertyChangedEventArgs e) { PropertyChanged?.Invoke(this, e); }
		}

		public Task PullFileAsync(string devicePath, string localPath, CancellationToken token = default(CancellationToken), IProgress<double> progress = null) {
			return Execute(d => d.PullFileAsync(devicePath, localPath, token, progress));
		}

		public Task PushFileAsync(string localPath, string devicePath, CancellationToken token = default(CancellationToken), IProgress<double> progress = null) {
			return Execute(d => d.PushFileAsync(localPath, devicePath, token, progress));
		}

		public Task RebootAsync() {
			return Execute(d => d.RebootAsync());
		}

		///<summary>Keeps running an operation against the current connection until no errors occur.</summary>
		async Task Execute(Func<IDeviceConnection, Task> operation) {
			while (true) {
				var currentSource = volatileDeviceSource;
				var connection = await currentSource.Task;

				using (await sourceLock.ReaderLockAsync()) {
					// If a different device was installed between waiting for the source and
					// acquiring the lock, start over.
					if (currentSource != volatileDeviceSource)
						continue;
					try {
						await operation(connection);
						return;
						// If a connection-level error occurs, clear the device, then wait for the next connection.
					} catch (SocketException ex) {
						HandleConnectionError(currentSource, ex);
					} catch (IOException ex) {
						HandleConnectionError(currentSource, ex);
					} catch (ShellCommandUnresponsiveException ex) {
						HandleConnectionError(currentSource, ex);
					} catch (SshConnectionException ex) {
						HandleConnectionError(currentSource, ex);
					} catch (ProxyException ex) {
						HandleConnectionError(currentSource, ex);
					}
				}
			}
		}

		///<summary>Releases the current connection, causing all future commands to wait for a new connection.</summary>
		/// <param name="currentSource">The source instance holding the device that failed.  This must already be resolved.</param>
		private void HandleConnectionError(TaskCompletionSource<IDeviceConnection> currentSource, Exception ex) {
			var newSource = new TaskCompletionSource<IDeviceConnection>();
			// Immediately clear the stored source to opportunistically
			// make new tasks wait for the next device. If a task slips
			// through, it will get a connection error and nothing will
			// happen.
			if (Interlocked.CompareExchange(ref volatileDeviceSource, newSource, currentSource) != currentSource)
				return;
			// Escape the read lock, then dispose the old connection in
			// a write lock to make sure that all tasks have finished.
			Task.Run(async () => {
				// Don't raise events inside a lock.
				OnConnectionError(new ConnectionErrorEventArgs(currentSource.Task.Result, ex));
				using (await sourceLock.WriterLockAsync())
					currentSource.Task.Result.Dispose();
			});
		}

		///<summary>Provides a new connection, causing any commands that are waiting for a connection to run immediately.</summary>
		public async Task SetDevice(IDeviceConnection newDevice) {
			if (newDevice == null) throw new ArgumentNullException(nameof(newDevice));

			// Wait for all commands to finish before replacing the connection.
			using (await sourceLock.WriterLockAsync()) {
				var oldDevice = volatileDeviceSource;

				// If we're waiting for a connection, simply resolve the promise.
				if (!oldDevice.Task.IsCompleted)
					oldDevice.SetResult(newDevice);
				else {
					// If we already have a device, replace it and dispose the old one.
					volatileDeviceSource = CompletedSource(newDevice);
					oldDevice.Task.Result.Dispose();
				}
			}

			OnConnectionEstablished();
		}

		///<summary>Occurs when a connection-level error is thrown.</summary>
		public event EventHandler<ConnectionErrorEventArgs> ConnectionError;
		///<summary>Raises the ConnectionError event.</summary>
		///<param name="e">A ConnectionErrorEventArgs object that provides the event data.</param>
		protected internal virtual void OnConnectionError(ConnectionErrorEventArgs e) => ConnectionError?.Invoke(this, e);

		///<summary>Occurs when a new connection is provided.</summary>
		public event EventHandler ConnectionEstablished;
		///<summary>Raises the ConnectionEstablished event.</summary>
		internal protected virtual void OnConnectionEstablished() => OnConnectionEstablished(EventArgs.Empty);
		///<summary>Raises the ConnectionEstablished event.</summary>
		///<param name="e">An EventArgs object that provides the event data.</param>
		protected internal virtual void OnConnectionEstablished(EventArgs e) => ConnectionEstablished?.Invoke(this, e);



		static TaskCompletionSource<T> CompletedSource<T>(T value) {
			var source = new TaskCompletionSource<T>();
			source.SetResult(value);
			return source;
		}

		protected virtual void Dispose(bool disposing) {
			if (disposing) {
				volatileDeviceSource.Task.ContinueWith(
					td => td.Result.Dispose(),
					TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously
				);
			}
		}
		public void Dispose() { Dispose(true); }

		DeviceScanner IDeviceConnection.Owner { get { throw new NotSupportedException(); } }
	}
	class ConnectionErrorEventArgs : EventArgs {
		public ConnectionErrorEventArgs(IDeviceConnection connection, Exception error) {
			DisposedConnection = connection;
			Error = error;
		}
		public Exception Error { get; }
		///<summary>Gets the connection that caused the error.</summary>
		public IDeviceConnection DisposedConnection { get; }
	}
}
