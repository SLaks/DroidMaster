using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
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
		///<summary>Holds every device ever discovered.</summary>
		internal List<DeviceViewModel> AllDevices { get; } = new List<DeviceViewModel>();

		///<summary>Gets the devices to display in the grid, excluding stale disconnected devices.</summary>
		public ObservableCollection<DeviceViewModel> ActiveDevices { get; } = new ObservableCollection<DeviceViewModel>();
		public ObservableCollection<string> DiscoveryErrors { get; } = new ObservableCollection<string>();

		public CancellationToken Stop { get; }

		private async Task RefreshManager() {
			try {
				await deviceManager.Refresh();
			} catch (Exception ex) {
				DiscoveryErrors.Insert(0, $"An error occurred while scanning for devices:\r\n{ex.Message}");
			}
		}

		// These methods are async voids because the caller doesn't care about the result.
		// Therefore, they must handle all possible exceptions.
		async void RunRefreshLoop() {
			while (!Stop.IsCancellationRequested) {
				await RefreshManager();
				try {
					await Task.Delay(TimeSpan.FromSeconds(5), Stop);
				} catch (TaskCanceledException) { return; }
			}
		}

		async void OnDeviceAdded(Core.PersistentDevice device) {
			var model = new DeviceViewModel(device);
			AllDevices.Add(model);
			ActiveDevices.Add(model);
			// When a hidden device is reconnected, re-add it to the grid.
			model.Device.ConnectionEstablished += delegate {
				if (!ActiveDevices.Contains(model))
					ActiveDevices.Add(model);
			};

			while (!Stop.IsCancellationRequested) {
				await model.Refresh();
				try {
					await Task.Delay(TimeSpan.FromSeconds(5), Stop);
				} catch (TaskCanceledException) { return; }
			}
		}

		///<summary>Releases all resources used by the DeviceListViewModel.</summary>
		public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
		///<summary>Releases the unmanaged resources used by the DeviceListViewModel and optionally releases the managed resources.</summary>
		///<param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
		protected virtual void Dispose(bool disposing) {
			if (disposing) {
				foreach (var device in AllDevices)
					device.Device.Dispose();
			}
		}
	}
	partial class DeviceViewModel : DeviceModel {
		public DeviceViewModel(Core.PersistentDevice device) : base(device) { }
		// Must be public for data-binding
		public new Core.PersistentDevice Device => base.Device;

		public override int BatteryLevel {
			get { return base.BatteryLevel; }
			protected set { base.BatteryLevel = value; OnPropertyChanged(nameof(BatteryColor)); }
		}
		public Brush BatteryColor => new SolidColorBrush(	// http://www.google.com/design/spec/style/color.html#color-color-palette
			BatteryLevel <= 10 ? Color.FromRgb(229, 57, 53) : BatteryLevel < 30 ? Color.FromRgb(255, 235, 59) : Color.FromRgb(76, 175, 80));

		public async Task HandleErrors(Func<DeviceViewModel, Task> func) {
			try {
				await func(this);
			} catch (Exception ex) {
				Log(ex.Message);
			}
		}
	}
}
