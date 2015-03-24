using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DroidMaster.Models;

namespace DroidMaster.UI {
	partial class DeviceListViewModel : NotifyPropertyChanged, IDisposable {
		readonly SynchronizationContext syncContext = SynchronizationContext.Current;

		public DeviceListViewModel(IEnumerable<Core.DeviceScanner> sources, CancellationToken stopToken) {
			deviceManager = new Core.PersistentDeviceManager(sources);
			deviceManager.DeviceDiscovered += (s, e) => syncContext.RunAsync(() => OnDeviceAdded(e.Data));
			deviceManager.DiscoveryError += (s, e) => syncContext.RunAsync(() => DiscoveryErrors.Insert(0, e.Data));

			Stop = stopToken;
			RunRefreshLoop();
		}

		private readonly Core.PersistentDeviceManager deviceManager;

		public ObservableCollection<DeviceModel> Devices { get; } = new ObservableCollection<DeviceModel>();
		public ObservableCollection<string> DiscoveryErrors { get; } = new ObservableCollection<string>();

		public CancellationToken Stop { get; }

		// These methods are async voids because the caller doesn't care about the result.
		// Therefore, they must handle all possible exceptions.
		async void RunRefreshLoop() {
			while (!Stop.IsCancellationRequested) {
				await Task.Delay(TimeSpan.FromSeconds(5), Stop);
				Stop.ThrowIfCancellationRequested();
				try {
					await deviceManager.Refresh();
				} catch (Exception ex) {
					DiscoveryErrors.Insert(0, $"An error occurred while scanning for devices:\r\n{ex.Message}");
				}
			}
		}

		async void OnDeviceAdded(Core.PersistentDevice device) {
			var model = new DeviceModel(device);
			Devices.Add(model);

			while (!Stop.IsCancellationRequested) {
				await Task.Delay(TimeSpan.FromSeconds(5), Stop);
				Stop.ThrowIfCancellationRequested();
				try {
					await model.Refresh();
				} catch (Exception ex) {
					model.Log($"An error occurred while refreshing device status:\r\n{ex.Message}");
				}
			}
		}

		///<summary>Releases all resources used by the DeviceListViewModel.</summary>
		public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
		///<summary>Releases the unmanaged resources used by the DeviceListViewModel and optionally releases the managed resources.</summary>
		///<param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
		protected virtual void Dispose(bool disposing) {
			if (disposing) {
				foreach (var device in Devices)
					device.Device.Dispose();
			}
		}
	}
}
