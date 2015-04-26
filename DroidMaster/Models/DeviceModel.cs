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
	public partial class DeviceModel : NotifyPropertyChanged {
		readonly ObservableCollection<object> writableLog = new ObservableCollection<object>();

		internal DeviceModel(PersistentDevice device) {
			LogItems = new ReadOnlyObservableCollection<object>(writableLog);
			Device = device;
			Device.PropertyChanged += (s, e) => {
				if (e.PropertyName == nameof(Device.CurrentConnectionMethod))
					OnPropertyChanged(nameof(IsOnline));
			};
		}

		internal PersistentDevice Device { get; }

		object status;
		///<summary>Gets or sets a WPF-bindable short status model object (eg, a string or a <see cref="ProgressModel"/>) to display in the grid.</summary>
		public object Status {
			get { return status; }
			set {
				if ((Status as AggregateProgressModel)?.IsActive == true)
					return;

				status = value;
				OnPropertyChanged();
			}
		}

		///<summary>Gets a collection of WPF-bindable objects containing output from script commands.</summary>
		public ReadOnlyObservableCollection<object> LogItems { get; }

		///<summary>Logs something of interest from this device.</summary>
		public void Log(object item) => writableLog.Insert(0, item);

		///<summary>Indicates whether the device has an active connection.  Offline devices are managed automatically; this property should only be used by UI.</summary>
		public bool IsOnline => Device.CurrentConnectionMethod != "Offline";

		#region Device Method Wrappers
		///<summary>Executes a shell command on the device, returning the full output.</summary>
		public Task<string> ExecuteShellCommand(string command) {
			var result = Device.ExecuteShellCommand(command);
			Log(result);
			return result.Complete;
		}

		///<summary>Copies a file onto the device.  If root access is required, call <see cref="PushFileAsRoot"/> instead.</summary>
		public Task PushFile(string localPath, string devicePath) {
			var model = new ProgressModel("Pushing " + Path.GetFileName(localPath));
			Status = model;
			Log(model);
			return Device.PushFileAsync(localPath, devicePath, progress: model);
		}

		///<summary>Copies a file from the device to the local computer.  If root access is required, call <see cref="PullFileAsRoot"/> instead.</summary>
		public Task PullFile(string devicePath, string localPath) {
			var model = new ProgressModel("Pulling " + Path.GetFileName(localPath));
			Status = model;
			Log(model);
			return Device.PullFileAsync(devicePath, localPath, progress: model);
		}

		///<summary>Reboots the device.</summary>
		public Task Reboot() {
			Status = "Rebooting...";
			Log("Rebooting device");
			return Device.RebootAsync();
		}
		#endregion

		#region Script Helper Methods
		///<summary>A directory on the device to push temporary files to.  Use this directory if the file is not needed beyond the script lifetime, and be sure to delete the file when you're done.</summary>
		public const string TempPath = "/data/local/tmp/";

		///<summary>Copies a file onto the device as root.</summary>
		public async Task PushFileAsRoot(string localPath, string devicePath) {
			var tempPath = TempPath + Guid.NewGuid();
			await PushFile(localPath, tempPath);
			await Device.ExecuteShellCommand($"su -c mv {tempPath} \"{devicePath}\"").Complete;
		}

		///<summary>Copies a file from the device to the local computer, as root.</summary>
		public async Task PullFileAsRoot(string devicePath, string localPath) {
			var tempPath = TempPath + Guid.NewGuid();
			await Device.ExecuteShellCommand($"su -c cp \"{devicePath}\" {tempPath} && su -c chmod 666 {tempPath}").Complete;
			await PullFile(tempPath, localPath);
			await Device.ExecuteShellCommand($"rm {tempPath}").Complete;
		}

		///<summary>Installs an APK file (from the computer) to the device.</summary>
		public async Task InstallPackage(string apkPath) {
			var tempPath = TempPath + Guid.NewGuid();
			await PushFile(apkPath, tempPath);
			var output = await ExecuteShellCommand($"pm install {tempPath} && rm {tempPath}");
			if (output.Contains("Failure"))
				throw new InvalidOperationException($"Couldn't install {Path.GetFileName(apkPath)}: " +
													output.Substring(output.IndexOf("Failure")));
        }
		#endregion
	}

	///<summary>A WPF-bindable view model that reports the progress of an operation.</summary>
	public class ProgressModel : NotifyPropertyChanged, IProgress<double> {
		///<summary>Creates a ProgressModel with the specified description.</summary>
		public ProgressModel(string description) { Description = description; }
		///<summary>Updates the progress (called via the <see cref="IProgress{T}"/> interface).</summary>
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
