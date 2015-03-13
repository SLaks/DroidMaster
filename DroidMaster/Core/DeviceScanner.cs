using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DroidMaster.Core {
	///<summary>Scans for connected Android devices.</summary>
	abstract class DeviceScanner : INotifyPropertyChanged {
		protected readonly ObservableCollection<IDeviceConnection> devices;
		protected DeviceScanner() {
			Devices = new ReadOnlyObservableCollection<IDeviceConnection>(devices);
		}

		///<summary>Rescans for connected devices.  Calling this will immediately clear the <see cref="Devices"/> collection.</summary>
		public abstract Task Scan();

		///<summary>The currently discovered devices.  This will be empty until <see cref="Scan"/> is first called.</summary>
		public ReadOnlyObservableCollection<IDeviceConnection> Devices { get; }

		string errors;
		///<summary>Gets any errors that occurred while discovering devices.</summary>
		public string Errors {
			get { return errors; }
			protected set { errors = value; OnPropertyChanged(); }
		}
		void AppendError(string line) {
			while (true) {
				var original = Errors;
				var newLog = string.IsNullOrEmpty(original) ? line : original + Environment.NewLine + line;
				if (Interlocked.CompareExchange(ref errors, newLog, original) == original)
					break;
			}
			OnPropertyChanged(nameof(Errors));
		}


		///<summary>Occurs when a property value is changed.</summary>
		public event PropertyChangedEventHandler PropertyChanged;
		///<summary>Raises the PropertyChanged event.</summary>
		///<param name="name">The name of the property that changed.</param>
		protected virtual void OnPropertyChanged([CallerMemberName] string name = null) {
			OnPropertyChanged(new PropertyChangedEventArgs(name));
		}
		///<summary>Raises the PropertyChanged event.</summary>
		///<param name="e">An EventArgs object that provides the event data.</param>
		protected virtual void OnPropertyChanged(PropertyChangedEventArgs e) {
			if (PropertyChanged != null)
				PropertyChanged(this, e);
		}
	}


	///<summary>Interacts with a single Android device.</summary>
	interface IDeviceConnection {
		Task RebootAsync();
		Task PushFileAsync(string localPath, string devicePath, CancellationToken token = default(CancellationToken), IProgress<double> progress = null);
		Task PullFileAsync(string devicePath, string localPath, CancellationToken token = default(CancellationToken), IProgress<double> progress = null);

		///<summary>Executes a shell command on the device, exposing the output of the command as it executes.  This method returns immediately.</summary>
		ICommandResult ExecuteShellCommand(string command);
	}

	///<summary>Reports the result of a shell command executing on the device.</summary>
	interface ICommandResult : INotifyPropertyChanged {
		///<summary>Resolved when the command has exited.</summary>
		Task Complete { get; }
		///<summary>The current output of the command.  This will update as the command prints more output, and will raise <see cref="INotifyPropertyChanged.PropertyChanged"/>.</summary>
		string Output { get; }
	}
}
