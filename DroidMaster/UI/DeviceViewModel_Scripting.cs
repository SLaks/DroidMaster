using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DroidMaster.Models;
using DroidMaster.Scripting;
using Microsoft.Win32;
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
		public async Task RunScript(DeviceScript script, ScriptContext context, string name) {
			using (await scriptLock.LockAsync()) {
				ScriptStatus = ScriptStatus.Running;
				ScriptName = name;
				Device.CancellationToken = new CancellationTokenSource();
				Log($"Running script {name}...");

				try {
					await script(this, context, Device.CancellationToken.Token);
					ScriptStatus = ScriptStatus.Success;
				} catch (TaskCanceledException) {
					ScriptStatus = ScriptStatus.Cancelled;
				} catch (OperationCanceledException) {
					ScriptStatus = ScriptStatus.Cancelled;
				} catch (Exception ex) {
					ScriptStatus = ScriptStatus.Failure;
					Status = ex.Message;
					Log($"An error occurred while running {name}:\r\n{ex}");
				}
				Device.CancellationToken = null;
				(Status as AggregateProgressModel)?.Dispose();	// Don't leak progress suppression
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
		Failure,
		///<summary>The script was cancelled by the user.</summary>
		Cancelled
	}

	///<summary>An object that is shared among all instances of a script running against multiple devices, allowing user prompts to happen just once.</summary>
	public class ScriptContext {
		readonly Dictionary<string, object> globalValues = new Dictionary<string, object>();

		///<summary>Computes a global value, exactly once per script execution.  All devices will share the same value.</summary>
		/// <param name="key">The key of the value to compute.  This must be a unique string; it's used to link calls across devices.</param>
		/// <param name="initializer">The function to compute the initial value.  This will be called exactly once, by the first script calling this method; all other calls will return the same result.</param>
		public T GlobalValue<T>(string key, Func<T> initializer) {
			object value;
			if (globalValues.TryGetValue(key, out value))
				return (T)value;
			var result = initializer();
			globalValues[key] = result;
			return result;
		}

		///<summary>Prompts the user to select a file, once per batch of devices.</summary>
		/// <param name="title">The title of the dialog.  This is also used as the global context key, and must be unique.</param>
		/// <param name="filter">The options for the file type, separated by | characters.</param>
		public string PickFile(string title, string filter) {
			return GlobalValue(title, () => {
				var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter };
				if (dialog.ShowDialog() != true)
					return null;
				return dialog.FileName;
			});
		}
		///<summary>Prompts the user to select a directory, once per batch of devices.</summary>
		/// <param name="title">The description of the dialog.  This is also used as the global context key, and must be unique.</param>
		public string PickFolder(string title) {
			return GlobalValue(title, () => {
				var dialog = new FolderBrowserDialog { Description = title };
				if (dialog.ShowDialog() == DialogResult.Cancel)
					return null;
				return dialog.SelectedPath;
			});
		}
	}
}
