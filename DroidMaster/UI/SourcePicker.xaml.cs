using System;
using System.Collections.Generic;
using System.Composition;
using System.Globalization;
using System.Linq;
using System.Net;
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
		}
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
			if (value != null && targetType == typeof(IPAddress))
				return IPAddress.Parse(value.ToString());
			return value;
		}
	}
}
