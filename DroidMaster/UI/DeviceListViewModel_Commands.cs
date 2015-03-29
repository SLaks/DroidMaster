using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using DroidMaster.Scripting;

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

	#region Scripting
	partial class DeviceListViewModel {
		public static string ScriptDirectory { get; set; } = Environment.CurrentDirectory;

		public IEnumerable<ScriptCommand> Scripts =>
			Directory.EnumerateFiles(ScriptDirectory)
				.Where(s => !Path.GetFileName(s).StartsWith("_"))
				.Where(s => WorkspaceCreator.LanguageExtensions.ContainsKey(Path.GetExtension(s)))
				.Select(s => new ScriptCommand(this, s));
	}

	class ScriptCommand : ICommand {
		readonly DeviceListViewModel deviceListViewModel;
		readonly string scriptPath;

		public ScriptCommand(DeviceListViewModel deviceListViewModel, string scriptPath) {
			this.deviceListViewModel = deviceListViewModel;
			this.scriptPath = scriptPath;
		}

		public event EventHandler CanExecuteChanged { add { } remove { } }

		public bool CanExecute(object parameter) => ((IEnumerable<object>)parameter).Any();

		public void Execute(object parameter) {
			DeviceScript script;
			try {
				var workspace = new RuntimeWorkspaceCreator { ScriptDirectory = Path.GetDirectoryName(scriptPath) };
				script = workspace.CompileScript(
			} catch (Exception ex) {

				throw;
			}
		}
	}
	#endregion
}
