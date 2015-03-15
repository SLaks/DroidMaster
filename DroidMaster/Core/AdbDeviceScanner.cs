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
					OnDeviceDiscovered(new DataEventArgs<IDeviceConnection>((new AdbDeviceConnection(device))));
				}
			});
		}

		class AdbDeviceConnection : IDeviceConnection {
			public AdbDeviceConnection(Device device) { Device = device; }
			Device Device { get; }

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
				var result = new OutputReporter();
				result.Complete = Task.Run(() => {
					Device.ExecuteShellCommand(command, result);
					return result.Output;
				});
				return result;
			}

			class ProgressAdapter : ISyncProgressMonitor {
				readonly CancellationToken token;
				readonly IProgress<double> reporter;
				public ProgressAdapter(CancellationToken token, IProgress<double> reporter) {
					this.token = token;
					this.reporter = reporter;
				}

				public bool IsCanceled { get { return token.IsCancellationRequested; } }

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
				protected virtual void OnPropertyChanged([CallerMemberName] string name = null) {
					OnPropertyChanged(new PropertyChangedEventArgs(name));
				}
				///<summary>Raises the PropertyChanged event.</summary>
				///<param name="e">An EventArgs object that provides the event data.</param>
				protected virtual void OnPropertyChanged(PropertyChangedEventArgs e) {
					if (PropertyChanged != null)
						PropertyChanged(this, e);
				}

				protected override void ProcessNewLines(string[] lines) {
					output += string.Join(Environment.NewLine, lines);
				}
			}
		}
	}
}