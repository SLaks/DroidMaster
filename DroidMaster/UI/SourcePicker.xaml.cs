using System;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
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
using VSEmbed;

namespace DroidMaster.UI {
	/// <summary>
	/// Interaction logic for SourcePicker.xaml
	/// </summary>
	public partial class SourcePicker : Window {
		///<summary>Creates a SourceFilePicker.</summary>
		public SourcePicker() {
			InitializeComponent();
		}

		private async void OpenScript_Click(object sender, RoutedEventArgs e) {
			try {
				if (VsLoader.VsVersion == null) {
					VsLoader.Load(new Version(14, 0, 0, 0));
					VsServiceProvider.Initialize();
				}

				// Load the MEF container on a background thread, because it can be slow
				(await Task.Run(() => OpenEditor())).Show();
			} catch (Exception ex) {
				MessageBox.Show("Failed to initialize Roslyn editor.  Make sure that Visual Studio 2015 is installed.\r\n" + ex,
								"DroidMaster", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}
		// Must be JITted after VsLoader.Load so we can load ComponentModelHost
		Scripting.Editor.ScriptEditor OpenEditor() {
			return VsMefContainerBuilder
				.CreateDefault()
				.WithFilteredCatalogs(typeof(Scripting.WorkspaceCreator).Assembly)
				.Build()
				.GetService<ExportFactory<Scripting.Editor.ScriptEditor>>().CreateExport().Value;
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
		}
	}
}
