using System;
using System.Collections.Generic;
using System.Composition;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DroidMaster.Core;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.Win32;
using Nito.AsyncEx;
using VSEmbed;

namespace DroidMaster.UI {
	/// <summary>
	/// Interaction logic for SourcePicker.xaml
	/// </summary>
	public partial class SourcePicker : Window {
		///<summary>Creates a SourceFilePicker.</summary>
		public SourcePicker() {
			InitializeComponent();
			LoadSettings();
		}

		private async void OpenScript_Click(object sender, RoutedEventArgs e) {
			try {
				if (VsLoader.VsVersion == null) {
					VsLoader.Load(new Version(14, 0, 0, 0));
					VsServiceProvider.Initialize();
				}

				await OpenEditor();
			} catch (Exception ex) {
				MessageBox.Show("Failed to initialize Roslyn editor.  Make sure that Visual Studio 2015 is installed.\r\n" + ex,
								"DroidMaster", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		// Must be JITted after VsLoader.Load so we can load ComponentModelHost
		static class LazyMefContainerHolder {
			// Load the MEF container on a background thread, because it can be slow
			public static readonly AsyncLazy<IComponentModel> Value = new AsyncLazy<IComponentModel>(() => Task.Run(() => VsMefContainerBuilder
			   .CreateDefault()
			   .WithFilteredCatalogs(typeof(Scripting.WorkspaceCreator).Assembly)
			   .Build()));
		}
		async Task OpenEditor() {
			(await LazyMefContainerHolder.Value)
				.GetService<ScriptEditorFactory>()
				.Factory
				.CreateExport()
				.Value
				.Show();
		}


		private void OpenGrid_Click(object sender, RoutedEventArgs e) {
			((SshDeviceScanner)sshPassword.DataContext).Password = sshPassword.Password;	// Password is not bindable
			var sources = new[] { enableAdb, enableSsh }
				.Where(c => c.IsChecked == true)
				.Select(c => (DeviceScanner)c.DataContext)
				.ToList();
			if (!sources.Any()) {
				MessageBox.Show("Please select at least one source.", "DroidMaster", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}
			var error = string.Join(Environment.NewLine, sources.Select(s => s.GetConfigError()));
			if (!string.IsNullOrWhiteSpace(error)) {
				MessageBox.Show(error, "DroidMaster", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}

			SaveSettings();

			new DeviceList(sources).Show();
		}

		#region Stored Settings
		const string SettingsKey = @"HKEY_CURRENT_USER\Software\SLaks\DroidMaster";
		void SaveSettings() {
			Registry.SetValue(SettingsKey, "Enable-ADB", enableAdb.IsChecked);
			Registry.SetValue(SettingsKey, "Enable-SSH", enableSsh.IsChecked);

			foreach (var property in typeof(SshDeviceScanner).GetProperties().Where(p => p.CanWrite)) {
				var value = property.GetValue(enableSsh.DataContext);
				if (property.Name == nameof(SshDeviceScanner.Password))
					value = ProtectedData.Protect(Encoding.UTF8.GetBytes((string)value), null, DataProtectionScope.CurrentUser);
				Registry.SetValue(SettingsKey, "SSH-" + property.Name, value);
			}
		}

		void LoadSettings() {
			try {
				enableAdb.IsChecked = bool.Parse(Registry.GetValue(SettingsKey, "Enable-ADB", null)?.ToString() ?? "False");
				enableSsh.IsChecked = bool.Parse(Registry.GetValue(SettingsKey, "Enable-SSH", null)?.ToString() ?? "False");

				// Replacing the DataContext entirely is the best way to refresh the bindings
				var newInstance = new SshDeviceScanner();
				foreach (var property in typeof(SshDeviceScanner).GetProperties().Where(p => p.CanWrite)) {
					var value = Registry.GetValue(SettingsKey, "SSH-" + property.Name, null) ?? "";
					if (property.PropertyType == typeof(IPAddress))
						value = IPAddress.Parse(value.ToString());
					else if (property.Name == nameof(SshDeviceScanner.Password))
						value = Encoding.UTF8.GetString(ProtectedData.Unprotect((byte[])value, null, DataProtectionScope.CurrentUser));
					else
						value = Convert.ChangeType(value, property.PropertyType);
					property.SetValue(newInstance, value);
				}
				wifiGroup.DataContext = newInstance;
				sshPassword.Password = newInstance.Password;	// Password is not bindable
			} catch { }
		}
		#endregion
	}

	[Export]
	class ScriptEditorFactory {
		[Import]
		public ExportFactory<Scripting.Editor.ScriptEditor> Factory { get; set; }
	}

	class IPAddressConverter : IValueConverter {
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
			return value?.ToString();
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
			try {
				if (value != null && targetType == typeof(IPAddress))
					return IPAddress.Parse(value.ToString());
			} catch (FormatException) { }
			return value;
		}
	}
}
