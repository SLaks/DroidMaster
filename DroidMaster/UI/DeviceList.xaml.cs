using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace DroidMaster.UI {
	partial class DeviceList : Window {
		readonly CancellationTokenSource onClosed = new CancellationTokenSource();
		readonly DeviceListViewModel viewModel;
		public DeviceList(IEnumerable<Core.DeviceScanner> sources) {
			InitializeComponent();
			DataContext = viewModel = new DeviceListViewModel(sources, onClosed.Token);
		}

		protected override void OnClosed(EventArgs e) {
			base.OnClosed(e);
			viewModel.Dispose();
			onClosed.Cancel();
		}

		private void ScriptMenu_Click(object sender, RoutedEventArgs e) {
			scriptMenu.GetBindingExpression(ItemsControl.ItemsSourceProperty).UpdateTarget();
			if (!viewModel.Scripts.Any())
				MessageBox.Show($"There are no scripts in {App.ScriptDirectory}.\r\nCreate .cs or .vb files there to run device scripts.  If Visual Studio 2015 is installed, click Open Script Editor for an IDE.");
		}

		private void ScriptMenu_SubmenuOpened(object sender, RoutedEventArgs e) {
			scriptMenu.GetBindingExpression(ItemsControl.ItemsSourceProperty).UpdateTarget();
		}
	}

	/// <summary>
	/// Provides a data template selector which honors data templates targeting interfaces implemented by the
	/// data context.  Source: http://badecho.com/2012/07/adding-interface-support-to-datatemplates/
	/// </summary>
	sealed class InterfaceTemplateSelector : DataTemplateSelector {
		public override DataTemplate SelectTemplate(object item, DependencyObject container) {
			var containerElement = container as FrameworkElement;

			if (item == null || containerElement == null)
				return base.SelectTemplate(item, container);

			var itemType = item.GetType();
			var dataTypes = Enumerable.Repeat(itemType, 1).Concat(itemType.GetInterfaces());

			return dataTypes
					.Select(t => new DataTemplateKey(t))
					.Select(containerElement.TryFindResource)
					.OfType<DataTemplate>()
					.FirstOrDefault()
					?? base.SelectTemplate(item, container);
		}
	}
}
