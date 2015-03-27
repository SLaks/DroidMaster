using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroidMaster.UI {
	partial class DeviceListViewModel {
		public ActionCommand RefreshCommand => new ActionCommand(async () => {
			await Task.WhenAll(new[] { RefreshManager() }.Concat(Devices.Select(d => d.Refresh())));
		});
	}

	partial class DeviceViewModel {
		public ActionCommand ToggleScreenCommand => new ActionCommand(async () => {
			await HandleErrors(d => d.Device.ExecuteShellCommand("input keyevent 26").Complete);
			await Refresh();
		});
	}
}
