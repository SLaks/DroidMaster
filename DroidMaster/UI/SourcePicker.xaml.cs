using System;
using System.Collections.Generic;
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

namespace DroidMaster.UI {
	/// <summary>
	/// Interaction logic for SourcePicker.xaml
	/// </summary>
	public partial class SourcePicker : Window {
		///<summary>Creates a SourceFilePicker.</summary>
		public SourcePicker() {
			InitializeComponent();
		}

		private void OpenScript_Click(object sender, RoutedEventArgs e) {

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
