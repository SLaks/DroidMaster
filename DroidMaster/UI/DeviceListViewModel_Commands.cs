using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
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

		public static Task EachDevice(IEnumerable<DeviceViewModel> selectedDevices, Func<DeviceViewModel, Task> action) {
			return Task.WhenAll(selectedDevices.Select(async d => {
				await d.HandleErrors(action);
				await d.Refresh();
			}));
		}
		static ICommand CreateSelectionCommand(Func<DeviceViewModel, Task> action) =>
			new ActionCommand<IEnumerable>(selectedDevices => EachDevice(selectedDevices.Cast<DeviceViewModel>(), action));
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
		public IEnumerable<ScriptCommand> Scripts =>
			Directory.EnumerateFiles(App.ScriptDirectory)
				.Where(s => !Path.GetFileName(s).StartsWith("_"))
				.Where(s => WorkspaceCreator.LanguageExtensions.ContainsKey(Path.GetExtension(s)))
				.Select(s => new ScriptCommand(this, s));

		public ICommand CancelScriptsCommand => CreateSelectionCommand(d => {
			d.Device.CancellationToken?.Cancel();
			return Task.CompletedTask;
		});
	}

	class ScriptCommand : ICommand {
		readonly DeviceListViewModel deviceListViewModel;
		readonly string scriptFile;

		public ScriptCommand(DeviceListViewModel deviceListViewModel, string scriptFile) {
			this.deviceListViewModel = deviceListViewModel;
			this.scriptFile = scriptFile;
		}

		public event EventHandler CanExecuteChanged { add { } remove { } }

		public bool CanExecute(object parameter) => true;
		public override string ToString() => Path.GetFileName(scriptFile);

		public async void Execute(object parameter) {
			DeviceScript script;
			try {
				var workspace = new RuntimeWorkspaceCreator { ScriptDirectory = Path.GetDirectoryName(scriptFile) };
				script = await workspace.CompileScript(scriptFile);
				Environment.CurrentDirectory = workspace.ScriptDirectory;
			} catch (Exception ex) {
				MessageBox.Show($"An error occurred while compiling {this}:\r\n{ex.Message}");
				return;
			}

			// First, clear the Finished/Failed status from any earlier scripts, for a clean grid.
			foreach (var device in deviceListViewModel.Devices) {
				// This method will wait for any executing scripts to finish
				// before clearing the status. Do not await its task; I want
				// to start running scripts against other devices ASAP.
				device.ClearScriptStatus().ToString();
			}

			var selectedDevices = ((IEnumerable)parameter).Cast<DeviceViewModel>();
			await DeviceListViewModel.EachDevice(selectedDevices, d => d.RunScript(script, ToString()));
		}
	}
	#endregion
}
