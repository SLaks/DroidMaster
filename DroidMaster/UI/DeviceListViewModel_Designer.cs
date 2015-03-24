﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DroidMaster.Core;
using DroidMaster.Models;

namespace DroidMaster.UI {
	partial class DeviceListViewModel {
		[Obsolete("This constructor should only be called by the designer.", error: true)]
		public DeviceListViewModel() {
			Devices.Add(new DeviceModel(new PersistentDevice("A", new DesignerDevice(new AdbDeviceScanner(), "AC123DEADB33F"))));
			Devices.Add(new DeviceModel(new PersistentDevice("B", new DesignerDevice(new SshDeviceScanner(), "192.168.1.217"))));
			Devices.Add(new DeviceModel(new PersistentDevice("C", new DesignerDevice(new AdbDeviceScanner(), "4829AD254FE76"))));
			Devices.Add(new DeviceModel(new PersistentDevice("D", new DesignerDevice(new SshDeviceScanner(), "192.168.1.187"))));

			SetDesignerProperty(0, nameof(DeviceModel.BatteryLevel), 78);
			SetDesignerProperty(1, nameof(DeviceModel.BatteryLevel), 32);
			SetDesignerProperty(2, nameof(DeviceModel.BatteryLevel), 12);
			SetDesignerProperty(3, nameof(DeviceModel.BatteryLevel), 90);

			SetDesignerProperty(0, nameof(DeviceModel.PowerSources), "AC");
			SetDesignerProperty(2, nameof(DeviceModel.PowerSources), "USB");

			SetDesignerProperty(1, nameof(DeviceModel.IsScreenOn), true);
			SetDesignerProperty(2, nameof(DeviceModel.IsScreenOn), true);
		}

		void SetDesignerProperty(int index, string property, object value)
			=> typeof(DeviceModel).GetProperty(property).SetValue(Devices[index], value);

		class DesignerDevice : IDeviceConnection {
			public DesignerDevice(DeviceScanner type, string id) {
				Owner = type;
				ConnectionId = id;
			}
			public string ConnectionId { get; }

			public DeviceScanner Owner { get; }

			public void Dispose() { }
			public ICommandResult ExecuteShellCommand(string command) { throw new NotImplementedException(); }
			public Task RebootAsync() { throw new NotImplementedException(); }
			public Task PullFileAsync(string devicePath, string localPath, CancellationToken token = default(CancellationToken), IProgress<double> progress = null) {
				throw new NotImplementedException();
			}
			public Task PushFileAsync(string localPath, string devicePath, CancellationToken token = default(CancellationToken), IProgress<double> progress = null) {
				throw new NotImplementedException();
			}
		}
	}
}
