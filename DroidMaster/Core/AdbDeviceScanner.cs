using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Managed.Adb;

namespace DroidMaster.Core {
	class AdbDeviceScanner : DeviceScanner {
		public override string DisplayName => "USB";

		public override Task Scan() {
			return Task.Run(() => {
				var discoveredDevices = AndroidDebugBridge.Instance.Devices;
				var offline = discoveredDevices.Where(d => d.IsOffline);
				if (offline.Any()) {
					LogError($"Skipping {offline.Count()} offline device{(offline.Skip(1).Any() ? "s" : "")} (which cannot be controlled)\r\n"
						   + string.Join(", ", offline.Select(d => d.DeviceProperty)));
				}
				var duplicates = discoveredDevices.GroupBy(d => d.DeviceProperty)
												  .Where(g => g.Skip(1).Any());
				if (duplicates.Any()) {
					LogError("Skipping the following duplicate device IDs, which ADB cannot control:"
							  + string.Join("\r\n", duplicates.Select(g =>
									$"{g.Key}: {g.Count()} device" + (g.Skip(1).Any() ? "s" : ""))));
				}

				foreach (var device in discoveredDevices
											.Where(d => d.IsOnline)
											.Except(duplicates.SelectMany(g => g))) {
					OnDeviceDiscovered(new DataEventArgs<IDeviceConnection>(new AdbDeviceConnection(this, device)));
				}
			});
		}

		// ADB has no concept of searching for a single device ID.
		public override Task ScanFor(string connectionId) => Scan();

		sealed class AdbDeviceConnection : IDeviceConnection {
			Device Device { get; }
			public DeviceScanner Owner { get; }
			public string ConnectionId => Device.SerialNumber;
			public AdbDeviceConnection(DeviceScanner owner, Device device) {
				Device = device;
				Owner = owner;
			}

			public Task RebootAsync() {
				return Task.Run(new Action(Device.Reboot));
			}

			public Task PullFileAsync(string localPath, string devicePath, CancellationToken token = default(CancellationToken), IProgress<double> progress = null) {
				return Task.Run(() => Device.SyncService.PullFile(
					Device.FileListingService.FindFileEntry(devicePath),
					localPath,
					CreateMonitor(token, progress)
				));
			}
			public Task PushFileAsync(string localPath, string devicePath, CancellationToken token = default(CancellationToken), IProgress<double> progress = null) {
				return Task.Run(() => Device.SyncService.PushFile(localPath, devicePath, CreateMonitor(token, progress)));
			}
			static ISyncProgressMonitor CreateMonitor(CancellationToken token, IProgress<double> progress) {
				return progress != null ? new ProgressAdapter(token, progress) : (ISyncProgressMonitor)new NullSyncProgressMonitor();
			}

			public ICommandResult ExecuteShellCommand(string command) {
				var result = new OutputReporter { CommandText = command };
				result.Complete = Task.Run(() => {
					AdbHelper.Instance.ExecuteRemoteCommand(AndroidDebugBridge.SocketAddress, command, Device, result);
					return result.Output;
				});
				return result;
			}

			void IDisposable.Dispose() { }	// Nothing to dispose

			class ProgressAdapter : ISyncProgressMonitor {
				readonly CancellationToken token;
				readonly IProgress<double> reporter;
				public ProgressAdapter(CancellationToken token, IProgress<double> reporter) {
					this.token = token;
					this.reporter = reporter;
				}

				public bool IsCanceled => token.IsCancellationRequested;

				long current, total;

				public void Advance(long work) {
					current += work;
					reporter.Report((double)current / total);
				}

				public void Start(long totalWork) { total = totalWork; }

				public void StartSubTask(string source, string destination) { }

				public void Stop() { }
			}

			class OutputReporter : MultiLineReceiver, ICommandResult {
				public string CommandText { get; set; }
				public Task<string> Complete { get; set; }

				string output;
				public string Output {
					get { return output; }
					set { output = value; OnPropertyChanged(); }
				}

				///<summary>Occurs when a property value is changed.</summary>
				public event PropertyChangedEventHandler PropertyChanged;
				///<summary>Raises the PropertyChanged event.</summary>
				///<param name="name">The name of the property that changed.</param>
				protected virtual void OnPropertyChanged([CallerMemberName] string name = null) => OnPropertyChanged(new PropertyChangedEventArgs(name));
				///<summary>Raises the PropertyChanged event.</summary>
				///<param name="e">An EventArgs object that provides the event data.</param>
				protected virtual void OnPropertyChanged(PropertyChangedEventArgs e) => PropertyChanged?.Invoke(this, e);

				protected override void ProcessNewLines(string[] lines) {
					// This is only called synchronously, by the blocking
					// Device.ExecuteShellCommand() method, so we have no
					// threading issues.
					output += string.Join(Environment.NewLine, lines);
				}
			}
		}
	}
}