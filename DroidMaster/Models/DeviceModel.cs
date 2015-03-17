using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using DroidMaster.Core;

namespace DroidMaster.Models {
	///<summary>A wrapper around <see cref="PersistentDevice"/>.</summary>
	///<remarks>The public members of this class form the API  for device scripts.</remarks>
	public class DeviceModel : NotifyPropertyChanged {
		internal DeviceModel(PersistentDevice device) {
			Device = device;
		}

		internal PersistentDevice Device { get; }

		object status;
		///<summary>Gets or sets a WPF-bindable short status model object (eg, a string or a <see cref="ProgressModel"/>) to display in the grid.</summary>
		public object Status {
			get { return status; }
			set { status = value; OnPropertyChanged(); }
		}

		///<summary>Gets a collection of WPF-bindable objects containing output from script commands.</summary>
		public ObservableCollection<object> Log { get; } = new ObservableCollection<object>();

		#region Device Method Wrappers
		///<summary>Executes a shell command on the device, returning the full output.</summary>
		public Task<string> ExecuteShellCommand(string command) {
			var result = Device.ExecuteShellCommand(command);
			Log.Add(result);
			return result.Complete;
		}

		///<summary>Copies a file onto the device.  If root access is required, call <see cref="PushFileAsRoot"/> instead.</summary>
		public Task PushFile(string localPath, string devicePath) {
			var model = new ProgressModel("Pushing " + Path.GetFileName(localPath));
			Status = model;
			Log.Add(model);
			return Device.PushFileAsync(localPath, devicePath, progress: model);
		}

		///<summary>Copies a file from the device to the local computer.  If root access is required, call <see cref="PullFileAsRoot"/> instead.</summary>
		public Task PullFile(string devicePath, string localPath) {
			var model = new ProgressModel("Pulling " + Path.GetFileName(localPath));
			Status = model;
			Log.Add(model);
			return Device.PushFileAsync(devicePath, localPath, progress: model);
		}

		///<summary>Reboots the device.</summary>
		public Task Reboot() {
			Status = "Rebooting...";
			Log.Add("Rebooting device");
			return Device.RebootAsync();
		}
		#endregion

		#region Script Helper Methods
		///<summary>Copies a file onto the device as root.</summary>
		public async Task PushFileAsRoot(string localPath, string devicePath) {
			var tempPath = "/data/local/tmp/" + Guid.NewGuid();
			await PushFile(localPath, tempPath);
			await Device.ExecuteShellCommand($"su -c mv {tempPath} \"{devicePath}\"").Complete;
		}

		///<summary>Copies a file from the device to the local computer, as root.</summary>
		public async Task PullFileAsRoot(string localPath, string devicePath) {
			var tempPath = "/data/local/tmp/" + Guid.NewGuid();
			await Device.ExecuteShellCommand($"su -c cp \"{devicePath}\" {tempPath}").Complete;
			await PushFile(localPath, tempPath);
			await Device.ExecuteShellCommand($"rm {tempPath}").Complete;
		}
		#endregion
	}

	///<summary>A WPF-bindable view model that reports the progress of an operation.</summary>
	public class ProgressModel : NotifyPropertyChanged, IProgress<double> {
		public ProgressModel(string description) { Description = description; }
		public void Report(double value) => Progress = value;

		double progress;
		///<summary>Gets or sets the progress of the operation, between 0 and 1.</summary>
		public double Progress {
			get { return progress; }
			set { progress = value; OnPropertyChanged(); }
		}

		string description;
		///<summary>Gets or sets a short description of the operation taking place.</summary>
		public string Description {
			get { return description; }
			set { description = value; OnPropertyChanged(); }
		}
	}
}
