using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace DroidMaster.Core {
	class SshDeviceScanner : DeviceScanner {
		public override string DisplayName => "WiFi";
		public IPAddress StartAddress { get; set; }
		public IPAddress EndAddress { get; set; }
		public short Port { get; set; }

		public string UserName { get; set; }
		public IEnumerable<PrivateKeyFile> PrivateKeys { get; set; }

		public override Task Scan() {
			return Task.WhenAll(GetAddresses().Select(ip => ip.ToString()).Select(TryConnect));
		}

		private async Task TryConnect(string a) {
			try {
				var client = new SshClient(a, Port, UserName, PrivateKeys.ToArray());
				await Task.Run(new Action(client.Connect)).ConfigureAwait(false);

				OnDeviceDiscovered(new DataEventArgs<IDeviceConnection>(new SshDeviceConnection(this, client)));
			} catch (Exception ex) {
				LogError(ex.Message);
			}
		}

		public override Task ScanFor(string connectionId) => TryConnect(connectionId);

		IEnumerable<IPAddress> GetAddresses() {
			var end = new BigInteger(EndAddress.GetAddressBytes());
			for (var address = new BigInteger(StartAddress.GetAddressBytes()); address <= end; address++) {
				yield return new IPAddress(address.ToByteArray());
			}
		}

		class SshDeviceConnection : IDeviceConnection {
			public SshClient Client { get; }
			public DeviceScanner Owner { get; }
			public string ConnectionId => Client.ConnectionInfo.Host;

			public SshDeviceConnection(SshDeviceScanner owner, SshClient client) {
				Client = client;
				Owner = owner;
			}

			public async Task RebootAsync() {
				// Source: http://android.stackexchange.com/a/43708/2569
				await ExecuteShellCommand("am broadcast android.intent.action.ACTION_SHUTDOWN").Complete.ConfigureAwait(false);
				await Task.Delay(TimeSpan.FromSeconds(5));
				try {
					await ExecuteShellCommand("reboot").Complete.ConfigureAwait(false);
				} catch {
					await ExecuteShellCommand("su -c reboot").Complete.ConfigureAwait(false);
				}
			}

			// TODO: SFTP / SCP
			public Task PushFileAsync(string localPath, string devicePath, CancellationToken token = default(CancellationToken), IProgress<double> progress = null) {
				throw new NotImplementedException();
			}

			public Task PullFileAsync(string devicePath, string localPath, CancellationToken token = default(CancellationToken), IProgress<double> progress = null) {
				throw new NotImplementedException();
			}

			public ICommandResult ExecuteShellCommand(string command) {
				var result = new ShellCommandResult();
				result.Complete = result.Execute(Client.CreateCommand(command));
				return result;
			}

			class ShellCommandResult : Stream, ICommandResult {
				static readonly IReadOnlyCollection<Action<SshCommand, Stream>> StreamPropertySetters = new[] {
					nameof(SshCommand.OutputStream),
					nameof(SshCommand.ExtendedOutputStream)
				}
					.Select(typeof(SshCommand).GetProperty)
					.Select(p => (Action<SshCommand, Stream>)Delegate.CreateDelegate(typeof(Action<SshCommand, Stream>), p.GetMethod))
					.ToList();

				public async Task<string> Execute(SshCommand command) {
					CommandText = command.CommandText;
					var task = Task.Factory.FromAsync(command.BeginExecute, command.EndExecute, null);
					foreach (var setter in StreamPropertySetters) {
						setter(command, this);
					}
					await task.ConfigureAwait(false);
					return Output;
				}

				public Task<string> Complete { get; set; }
				public string CommandText { get; private set; }

				string output;
				public string Output { get { return output; } }
				void AppendData(string data) {
					while (true) {
						var oldValue = output;
						var newValue = oldValue + data;
						if (Interlocked.CompareExchange(ref output, newValue, oldValue) == oldValue)
							break;
					}
					OnPropertyChanged(nameof(Output));
				}

				// SshNet does not expose any event that lets us listen as output comes arrives.
				// Instead, I use reflection to inject this class as the Stream instance that it
				// appends data to.
				#region Stream Implementation
				public override void Write(byte[] buffer, int offset, int count) {
					// TODO: Use StreamReader or UTF8Encoding to buffer incomplete codepoints
					AppendData(Encoding.UTF8.GetString(buffer, offset, count));
				}
				public override void Flush() { }	// Called immediately after each Write() call
				public override bool CanRead => false;

				public override bool CanSeek => false;

				public override bool CanWrite => true;

				public override long Length { get { throw new NotImplementedException(); } }

				public override long Position {
					get { throw new NotImplementedException(); }
					set { throw new NotImplementedException(); }
				}


				public override long Seek(long offset, SeekOrigin origin) { throw new NotImplementedException(); }

				public override void SetLength(long value) { throw new NotImplementedException(); }

				public override int Read(byte[] buffer, int offset, int count) { throw new NotImplementedException(); }

				#endregion

				///<summary>Occurs when a property value is changed.</summary>
				public event PropertyChangedEventHandler PropertyChanged;
				///<summary>Raises the PropertyChanged event.</summary>
				///<param name="name">The name of the property that changed.</param>
				protected virtual void OnPropertyChanged([CallerMemberName] string name = null) => OnPropertyChanged(new PropertyChangedEventArgs(name));
				///<summary>Raises the PropertyChanged event.</summary>
				///<param name="e">An EventArgs object that provides the event data.</param>
				protected virtual void OnPropertyChanged(PropertyChangedEventArgs e) => PropertyChanged?.Invoke(this, e);
			}

			#region IDisposable Support
			protected virtual void Dispose(bool disposing) {
				if (disposing) {
					Client.Dispose();
				}
			}
			// This code added to correctly implement the disposable pattern.
			public void Dispose() { Dispose(true); }
			#endregion
		}
	}
}
