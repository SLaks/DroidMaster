using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroidMaster.Core {
	///<summary>Listens for devices from a collection of <see cref="DeviceScanner"/>s, and maintains a <see cref="PersistentDevice"/> for each physical devices.</summary>
	class PersistentDeviceManager {
		readonly List<DeviceScanner> scanners = new List<DeviceScanner>();

		readonly ConcurrentDictionary<string, PersistentDevice> knownDevices = new ConcurrentDictionary<string, PersistentDevice>();

		public PersistentDeviceManager(IEnumerable<DeviceScanner> scanners) {
			Scanners = new ReadOnlyCollection<DeviceScanner>(this.scanners);
			foreach (var scanner in scanners)
				AddScanner(scanner);
		}
		public ReadOnlyCollection<DeviceScanner> Scanners { get; }

		public void AddScanner(DeviceScanner scanner) {
			scanners.Add(scanner);
			scanner.DeviceDiscovered += Scanner_DeviceDiscovered;
			scanner.DiscoveryError += (s, e) => OnDiscoveryError(e);
		}

		///<summary>Rescans all sources for new devices.</summary>
		public Task Refresh() {
			return Task.WhenAll(Scanners.Select(s => s.Scan()));
		}

		private async void Scanner_DeviceDiscovered(object sender, DataEventArgs<IDeviceConnection> e) {
			try {
				var id = await GetPersistentId(e.Data).ConfigureAwait(false);

				// If we already have a PersistentDevice with this ID, replace
				// it, and ignore (and do not dispose) the new instance. If we 
				// don't, fully create a new one.  
				var newDevice = new PersistentDevice(id, e.Data);
				var existingDevice = knownDevices.GetOrAdd(id, newDevice);
				if (existingDevice != newDevice)
					await existingDevice.SetDevice(e.Data).ConfigureAwait(false);
				else {
					newDevice.ConnectionError += (s, ee) => {
						OnDiscoveryError(new DataEventArgs<string>($"A connection error occurred on {ee.DisposedConnection.ConnectionId}:\r\n{ee.Error.Message}"));
						ee.DisposedConnection.Owner.ScanFor(ee.DisposedConnection.ConnectionId);
					};
					OnDeviceDiscovered(new DataEventArgs<PersistentDevice>(newDevice));
				}
			} catch (Exception ex) {
				OnDiscoveryError(new DataEventArgs<string>($"An error occurred while identifying {e.Data.ConnectionId}:\r\n{ex.Message}"));
			}
		}

		///<summary>The path to the file on each device that stores the persistent ID.</summary>
		const string DeviceIdPath = "/mnt/sdcard/droidmaster-id";
		///<summary>Finds or creates a persistent unique identifier for a device.</summary>
		private static async Task<string> GetPersistentId(IDeviceConnection device) {
			try {
				var existingId = await device.ExecuteShellCommand("cat " + DeviceIdPath).Complete.ConfigureAwait(false);
				if (!string.IsNullOrWhiteSpace(existingId))
					return existingId;
			} catch (FileNotFoundException) { }	// If the command fails (because the file does not exist), proceed

			var newId = $"SLaks/DroidMaster: First found at {DateTime.Now} as {device.ConnectionId}. {Guid.NewGuid()}";
			await device.ExecuteShellCommand($"echo > {DeviceIdPath} {newId}").Complete.ConfigureAwait(false);
			return newId;
		}

		///<summary>Occurs when a device is discovered.</summary>
		public event EventHandler<DataEventArgs<PersistentDevice>> DeviceDiscovered;
		///<summary>Raises the DeviceDiscovered event.</summary>
		///<param name="e">A DataEventArgs object that provides the event data.</param>
		internal protected virtual void OnDeviceDiscovered(DataEventArgs<PersistentDevice> e) => DeviceDiscovered?.Invoke(this, e);
		///<summary>Occurs when an error or warning is encountered during device discovery.</summary>
		public event EventHandler<DataEventArgs<string>> DiscoveryError;
		///<summary>Raises the DiscoveryError event.</summary>
		///<param name="e">A DataEventArgs object that provides the event data.</param>
		internal protected virtual void OnDiscoveryError(DataEventArgs<string> e) => DiscoveryError?.Invoke(this, e);
	}
}
