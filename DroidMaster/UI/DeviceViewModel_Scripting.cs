using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DroidMaster.Models;
using DroidMaster.Scripting;
using Nito.AsyncEx;

namespace DroidMaster.UI {
	partial class DeviceViewModel {
		readonly AsyncLock scriptLock = new AsyncLock();

		ScriptStatus scriptStatus;
		///<summary>Gets or sets the status of the currently executing script, if any.</summary>
		public ScriptStatus ScriptStatus {
			get { return scriptStatus; }
			set { scriptStatus = value; OnPropertyChanged(); }
		}

		string scriptName;
		///<summary>Gets or sets the name of the currently executing script, or null if no script is running.</summary>
		public string ScriptName {
			get { return scriptName; }
			set { scriptName = value; OnPropertyChanged(); }
		}


		///<summary>Resets the <see cref="ScriptStatus"/> to indicate that no script is running.  Use this method to clear the grid column.</summary>
		/// <returns>A <see cref="Task"/> that will not resolve until any currently running (or queued) scripts have completed.</returns>
		public async Task ClearScriptStatus() {
			// If there is a script running, wait for it to finish.
			using (await scriptLock.LockAsync()) {
				ScriptStatus = ScriptStatus.None;
				ScriptName = null;
			}
		}

		///<summary>Runs the specified script against the device, updating the script status properties as necessary.</summary>
		public async Task RunScript(DeviceScript script, string name) {
			using (await scriptLock.LockAsync()) {
				ScriptStatus = ScriptStatus.Running;
				ScriptName = name;
				Log($"Running script {name}...");

				try {
					await script(this);
					ScriptStatus = ScriptStatus.Success;
				} catch (Exception ex) {
					ScriptStatus = ScriptStatus.Failure;
					Status = ex.Message;
					Log($"An error occurred while running {name}:\r\n{ex}");
				}
			}
		}
	}

	///<summary>Represents the status of the script (if any) currently executing on a <see cref="DeviceViewModel"/>.</summary>
	enum ScriptStatus {
		///<summary>No script is running.</summary>
		None,
		///<summary>The script has not finished yet.</summary>
		Running,
		///<summary>The script completed successfully.</summary>
		Success,
		///<summary>The script threw an exception.</summary>
		Failure
	}
}
