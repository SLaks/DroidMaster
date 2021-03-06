﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Managed.Adb;
using Managed.Adb.IO;

namespace DroidMaster.Core {
	class AdbDeviceScanner : DeviceScanner {
		public override string DisplayName => "USB";

		public override Task Scan() {
			Device.DisableAutomaticInfoRetrieval = true;
			return Task.Run(() => {
				IEnumerable<Device> discoveredDevices;
				try {
					discoveredDevices = AndroidDebugBridge.Instance.Devices;
				} catch (SocketException) {
					try {
						TryStartServer();
					} catch (Exception ex) {
						LogError("Could not start adb.exe: " + ex.Message);
						return;
					}
					discoveredDevices = AndroidDebugBridge.Instance.Devices;
				}
				var offline = discoveredDevices.Where(d => !d.IsOnline);
				if (offline.Any()) {
					LogError($"Skipping {offline.Count()} offline device{(offline.Skip(1).Any() ? "s" : "")} (which cannot be controlled)\r\n"
						   + string.Join(", ", offline.Select(d => $"{d.SerialNumber}: {d.State}")));
				}
				var duplicates = discoveredDevices.GroupBy(d => d.SerialNumber)
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

		static readonly object serverStartLock = new object();
		void TryStartServer(bool isRetry = false) {
			lock (serverStartLock) {
				var process = Process.Start(new ProcessStartInfo("adb.exe", "start-server") {
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true
				});
				if (!isRetry)
					LogError("Starting ADB server...");
				process.WaitForExit();
				var error = process.StandardError.ReadToEnd();
				if (string.IsNullOrWhiteSpace(error))
					return;

				// ADB can end up in a zombie state and require a hard restart
				var deadADBs = Process.GetProcessesByName("adb");
				if (isRetry || !deadADBs.Any())
					throw new Exception("\r\n" + error.Trim());
				else {
					LogError($"Could not start adb.exe:\r\n{error.Trim()}\r\nKilling existing ADB process and retrying...");
					foreach (var deadADB in deadADBs) {
						try { deadADB.Kill(); } catch (IOException) { }	// Process has already exited
					}
					TryStartServer(isRetry: true);
				}
			}
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

			// The SyncService constructor opens a socket used by
			// all calls on that instance. Therefore, SyncService
			// instances must not be constructed on the UI thread
			// and must not be shared across threads.
			public Task PullFileAsync(string devicePath, string localPath, CancellationToken token = default(CancellationToken), IProgress<double> progress = null) {
				return Task.Run(() => {
					using (var syncService = new SyncService(Device))
						AssertResult(syncService.PullFile(
						// FindFileEntry(string) will recursively ls the parent
						// directories, throwing errors within /data/local/tmp.
						// Instead, I explicitly create an entry for the parent
						// directory, and find the file in that.
						Device.FileListingService.FindFileEntry(
							FileEntry.CreateNoPermissions(Device, LinuxPath.GetDirectoryName(devicePath)), devicePath
						),
						localPath,
						CreateMonitor(token, progress)
					));
				});
			}
			public Task PushFileAsync(string localPath, string devicePath, CancellationToken token = default(CancellationToken), IProgress<double> progress = null) {
				return Task.Run(() => {
					using (var syncService = new SyncService(Device))
						AssertResult(syncService.PushFile(localPath, devicePath, CreateMonitor(token, progress)));
				});
			}
			static ISyncProgressMonitor CreateMonitor(CancellationToken token, IProgress<double> progress) {
				return progress != null ? new ProgressAdapter(token, progress) : (ISyncProgressMonitor)new NullSyncProgressMonitor();
			}
			static void AssertResult(SyncResult result) {
				switch (result.Code) {
					case ErrorCodeHelper.RESULT_OK:
						return;
					case ErrorCodeHelper.RESULT_CANCELED:
						throw new OperationCanceledException();
					case ErrorCodeHelper.RESULT_UNKNOWN_ERROR:
					case ErrorCodeHelper.RESULT_CONNECTION_ERROR:
						throw new IOException(result.Message);	// Force connection retry in PersistentDevice
					default:
						throw new Exception(result.Message);
				}
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
					Output += string.Join(Environment.NewLine, lines);
				}
			}
		}
	}
}