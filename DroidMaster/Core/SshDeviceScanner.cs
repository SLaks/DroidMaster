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
		static SshDeviceScanner() {
			// SshNet uses this semaphore to prevent too many simultaneous connections.
			// That's the exact opposite of the behavior I want.
			typeof(Session).GetField("AuthenticationConnection", BindingFlags.NonPublic | BindingFlags.Static)
						   .SetValue(null, new SemaphoreLight(100));
		}

		public override string DisplayName => "Wi-Fi";
		public IPAddress StartAddress { get; set; }
		public IPAddress EndAddress { get; set; }
		public ushort Port { get; set; }

		public string UserName { get; set; }
		public string Password { get; set; }

		public override string GetConfigError() {
			var builder = new StringBuilder();
			if (StartAddress == null) builder.AppendLine("Please enter a start IP address to scan for devices.");
			if (EndAddress == null) builder.AppendLine("Please enter an end IP address to scan for devices.");
			if (StartAddress != null && EndAddress != null
			 && new BigInteger(StartAddress.GetAddressBytes().Reverse().ToArray()) > new BigInteger(EndAddress.GetAddressBytes().Reverse().ToArray()))
				builder.AppendLine("The start IP address must not be after the end IP address.");
			if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrWhiteSpace(Password))
				builder.AppendLine("Please enter a username and password to authenticate to the SSH servers");
			var result = builder.ToString().Trim();
			return string.IsNullOrEmpty(result) ? null : result;
		}

		public override Task Scan() {
			return Task.WhenAll(GetAddresses().Select(ip => ip.ToString()).Select(TryConnect));
		}

		private async Task TryConnect(string a) {
			try {
				var client = new SshClient(a, Port, UserName, Password);
				await Task.Run(new Action(client.Connect)).ConfigureAwait(false);

				OnDeviceDiscovered(new DataEventArgs<IDeviceConnection>(new SshDeviceConnection(this, client)));
			} catch (Exception ex) {
				LogError($"An error occurred while connecting to {a}:\r\n{ex.Message}");
			}
		}

		public override Task ScanFor(string connectionId) => TryConnect(connectionId);

		IEnumerable<IPAddress> GetAddresses() {
			var end = new BigInteger(EndAddress.GetAddressBytes().Reverse().ToArray());
			for (var address = new BigInteger(StartAddress.GetAddressBytes().Reverse().ToArray()); address <= end; address++) {
				yield return new IPAddress(address.ToByteArray().Reverse().ToArray());
			}
		}

		class SshDeviceConnection : IDeviceConnection {
			public SshClient Client { get; }
			public DeviceScanner Owner { get; }
			public string ConnectionId { get; }

			public SshDeviceConnection(SshDeviceScanner owner, SshClient client) {
				Client = client;
				Owner = owner;
				ConnectionId = Client.ConnectionInfo.Host;	// This will throw after the connection is closed.
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

			public Task PushFileAsync(string localPath, string devicePath, CancellationToken token = default(CancellationToken), IProgress<double> progress = null) {
				return Task.Run(() => {
					using (var client = new ScpClient(Client.ConnectionInfo)) {
						client.Connect();
						token.ThrowIfCancellationRequested();
						client.Uploading += (s, e) => progress.Report(e.Uploaded / (double)e.Size);
						client.Upload(new FileInfo(localPath), devicePath);
					}
				}, token);
			}

			public Task PullFileAsync(string devicePath, string localPath, CancellationToken token = default(CancellationToken), IProgress<double> progress = null) {
				return Task.Run(() => {
					using (var client = new ScpClient(Client.ConnectionInfo)) {
						client.Connect();
						token.ThrowIfCancellationRequested();
						client.Downloading += (s, e) => progress.Report(e.Downloaded / (double)e.Size);
						client.Download(devicePath, new FileInfo(localPath));
					}
				}, token);
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
					.Select(p => (Action<SshCommand, Stream>)Delegate.CreateDelegate(typeof(Action<SshCommand, Stream>), p.SetMethod))
					.ToList();

				public async Task<string> Execute(SshCommand command) {
					CommandText = command.CommandText;
					// BeginExecute opens the channel synchronously, so we need
					// to run it in a background thread.
					var task = Task.Run(() => {
						var innerTask = Task.Factory.FromAsync(command.BeginExecute, command.EndExecute, null);
						// We must wait for BeginExecute to finish to overwrite
						// the Stream properties which are set in CreateChannel
						foreach (var setter in StreamPropertySetters) {
							setter(command, this);
						}
						return innerTask;
					});
					await task.ConfigureAwait(false);
					// https://play.google.com/store/apps/details?id=berserker.android.apps.sshdroid appends a trailing \n to every response
					if (Output == null)
						output = "";
					else if (Output.EndsWith("\n"))
						output = Output.Remove(Output.Length - 1);
					OnPropertyChanged(nameof(Output));
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

				public override long Length { get { return 0; } }	// Called by Execute()

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
