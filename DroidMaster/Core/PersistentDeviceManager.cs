using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroidMaster.Core {
	///<summary>Listens for devices from a collection of <see cref="DeviceScanner"/>s, and maintains a <see cref="PersistentDevice"/> for each physical devices.</summary>
	class PersistentDeviceManager {
		readonly List<DeviceScanner> scanners = new List<DeviceScanner>();

		readonly ConcurrentDictionary<string, PersistentDevice> knownDevices = new ConcurrentDictionary<string, PersistentDevice>();

		public PersistentDeviceManager() {
			Scanners = new ReadOnlyCollection<DeviceScanner>(scanners);
		}
		public ReadOnlyCollection<DeviceScanner> Scanners { get; }

		public void AddScanner(DeviceScanner scanner) {
			scanners.Add(scanner);
			scanner.DeviceDiscovered += Scanner_DeviceDiscovered;
			scanner.DiscoveryError += (s, e) => OnDiscoveryError(e);
		}

		private async void Scanner_DeviceDiscovered(object sender, DataEventArgs<IDeviceConnection> e) {
			try {
				var id = await GetPersistentId(e.Data);

				// If we already have a PersistentDevice with this ID, replace
				// it, and ignore (and do not dispose) the new instance. If we 
				// don't, fully create a new one.  
				var newDevice = new PersistentDevice(id, e.Data);
				var existingDevice = knownDevices.GetOrAdd(id, newDevice);
				if (existingDevice != newDevice)
					await existingDevice.SetDevice(e.Data);
				else {
					newDevice.ConnectionError += (s, ee) => {
						OnDiscoveryError(new DataEventArgs<string>($"A connection error occurred on ee.DisposedConnection.ConnectionId:\r\n{ee.Error.Message}"));
						ee.DisposedConnection.Owner.ScanFor(ee.DisposedConnection.ConnectionId);
					};
				}
			} catch (Exception ex) {
				OnDiscoveryError(new DataEventArgs<string>($"An error occurred while identifying {e.Data.ConnectionId}:\r\n${ex.Message}"));
			}
		}

		///<summary>The path to the file on each device that stores the persistent ID.</summary>
		const string DeviceIdPath = "/mnt/sdcard/droidmaster-id";
		///<summary>Finds or creates a persistent unique identifier for a device.</summary>
		private static async Task<string> GetPersistentId(IDeviceConnection device) {
			var existingId = await device.ExecuteShellCommand("cat " + DeviceIdPath).Complete;
			if (string.IsNullOrWhiteSpace(existingId))
				return existingId;
			var newId = $"SLaks/DroidMaster: First found at {DateTime.Now} as {device.ConnectionId}. {Guid.NewGuid()}";
			await device.ExecuteShellCommand($"echo > {DeviceIdPath} {newId}").Complete;
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
