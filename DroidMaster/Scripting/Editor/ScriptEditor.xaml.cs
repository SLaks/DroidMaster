using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Composition;
using System.IO;
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

namespace DroidMaster.Scripting.Editor {
	[Export]
	partial class ScriptEditor : Window {
		readonly ScriptEditorViewModel viewModel;
		[ImportingConstructor]
		public ScriptEditor(ExportFactory<ScriptEditorViewModel> viewModelFactory) {
			InitializeComponent();
			DataContext = viewModel = viewModelFactory.CreateExport().Value;
			viewModel.WorkspaceCreator.ScriptDirectory = App.ScriptDirectory;
			RefreshOpenMenu();

			Application.Current.Resources.MergedDictionaries.Add(new VSEmbed.VsThemeDictionary { ThemeIndex = 3 });
			Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary {
				Source = new Uri("/Microsoft.VisualStudio.Editor.Implementation;component/Themes/Generic.xaml", UriKind.Relative)
			});
		}

		private void OpenMenu_SubmenuOpened(object sender, RoutedEventArgs e) {
			RefreshOpenMenu();
			viewModel.RefreshClosedFiles();
			viewModel.WorkspaceCreator.RefreshReferenceProjects();
		}

		void RefreshOpenMenu() {
			openMenu.ItemsSource = Directory
				.EnumerateFiles(viewModel.WorkspaceCreator.ScriptDirectory)
					.Where(p => WorkspaceCreator.LanguageExtensions.ContainsKey(Path.GetExtension(p)))
					.Select(Path.GetFileName)
					.OrderBy(f => f)
					.ToList();
		}

		protected override void OnClosing(CancelEventArgs e) {
			base.OnClosing(e);
			var unsavedDocuments = viewModel.Files.Where(f => f.IsDirty);
			if (!unsavedDocuments.Any())
				return;
			switch (MessageBox.Show($"Do you want to save changes to {string.Join(", ", unsavedDocuments.Select(vm => vm.FileName))}?",
									"DroidMaster", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning)) {
				case MessageBoxResult.Cancel:
					e.Cancel = true;
					return;
				case MessageBoxResult.Yes:
					foreach (var vm in unsavedDocuments)
						vm.Document.Save();
					break;
				case MessageBoxResult.No:
					break;
			}
		}

		private void OpenMenu_Click(object sender, RoutedEventArgs e) {
			RefreshOpenMenu();
			viewModel.WorkspaceCreator.RefreshReferenceProjects();
			if (!openMenu.HasItems) {
				MessageBox.Show("There are no files in the script directory.\r\nYou can create a script by clicking New Script.");
			}
		}
	}
}