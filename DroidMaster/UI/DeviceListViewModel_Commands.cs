using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace DroidMaster.UI {
	partial class DeviceListViewModel {
		public ActionCommand RefreshCommand => new ActionCommand(async () => {
			await Task.WhenAll(new[] { RefreshManager() }.Concat(
				// All offline devices should already have a pending refresh from
				// their refresh loop, so there is no need to queue a second one.
				Devices.Where(d => d.IsOnline).Select(d => d.Refresh())
			));
		});

		static Task EachDevice(IEnumerable<DeviceViewModel> selectedDevices, Func<DeviceViewModel, Task> action) {
			return Task.WhenAll(selectedDevices.Select(async d => {
				await d.HandleErrors(action);
				await d.Refresh();
			}));
		}
		static ICommand CreateSelectionCommand(Func<DeviceViewModel, Task> action) =>
			new ActionCommand<IEnumerable<DeviceViewModel>>(selectedDevices => EachDevice(selectedDevices, action));
		public ICommand ToggleScreensCommand { get; } = CreateSelectionCommand(d => d.ToggleScreen());
		public ICommand ScreensOffCommand => CreateSelectionCommand(async d => {
			await d.Refresh();
			if (d.IsScreenOn)
				await d.ToggleScreen();
		});
		public ICommand ScreensOnCommand => CreateSelectionCommand(async d => {
			await d.Refresh();
			if (!d.IsScreenOn)
				await d.ToggleScreen();
		});
	}

	partial class DeviceViewModel {
		public Task ToggleScreen() => Device.ExecuteShellCommand("input keyevent 26").Complete;

		public ActionCommand ToggleScreenCommand => new ActionCommand(async () => {
			await HandleErrors(d => d.ToggleScreen());
			await Refresh();
		});
		public ActionCommand ToggleWiFiCommand => new ActionCommand(async () => {
			await Refresh();	// TODO: Does this need root?
			await HandleErrors(d => d.Device.ExecuteShellCommand("svc wifi " + (IsWiFiEnabled ? "disable" : "enable")).Complete);
			await Refresh();
		});
	}
}
