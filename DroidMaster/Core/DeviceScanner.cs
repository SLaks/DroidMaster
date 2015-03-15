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
	abstract class DeviceScanner {

		///<summary>Rescans for connected devices.</summary>
		public abstract Task Scan();

		///<summary>Attempts to reconnect to the specified connection ID, raising <see cref="DeviceDiscovered"/> if found.  This may have no effect.</summary>
		public abstract Task ScanFor(string connectionId);

		protected void LogError(string text) { OnDiscoveryError(new DataEventArgs<string>(text)); }

		///<summary>Occurs when a device is discovered.</summary>
		public event EventHandler<DataEventArgs<IDeviceConnection>> DeviceDiscovered;
		///<summary>Raises the DeviceDiscovered event.</summary>
		///<param name="e">A DataEventArgs object that provides the event data.</param>
		internal protected virtual void OnDeviceDiscovered(DataEventArgs<IDeviceConnection> e) => DeviceDiscovered?.Invoke(this, e);
		///<summary>Occurs when an error or warning is encountered during device discovery.</summary>
		public event EventHandler<DataEventArgs<string>> DiscoveryError;
		///<summary>Raises the DiscoveryError event.</summary>
		///<param name="e">A DataEventArgs object that provides the event data.</param>
		internal protected virtual void OnDiscoveryError(DataEventArgs<string> e) => DiscoveryError?.Invoke(this, e);
	}


	///<summary>Interacts with a single Android device.</summary>
	interface IDeviceConnection : IDisposable {
		Task RebootAsync();
		Task PushFileAsync(string localPath, string devicePath, CancellationToken token = default(CancellationToken), IProgress<double> progress = null);
		Task PullFileAsync(string devicePath, string localPath, CancellationToken token = default(CancellationToken), IProgress<double> progress = null);

		///<summary>Executes a shell command on the device, exposing the output of the command as it executes.  This method returns immediately.</summary>
		ICommandResult ExecuteShellCommand(string command);

		///<summary>Gets a unique identifier for this connection.  This identifier may not be stable across reconnections to the same physical device.</summary>
		string ConnectionId { get; }

		///<summary>Gets the <see cref="DeviceScanner"/> that discovered this device.</summary>
		DeviceScanner Owner { get; }
	}

	///<summary>Reports the result of a shell command executing on the device.</summary>
	interface ICommandResult : INotifyPropertyChanged {
		///<summary>Resolves to the complete output, after the command has exited.</summary>
		Task<string> Complete { get; }
		///<summary>The current output of the command.  This will update as the command prints more output, and will raise <see cref="INotifyPropertyChanged.PropertyChanged"/>.</summary>
		string Output { get; }
	}
}
