using System;
using System.Collections.Generic;
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
			viewModel.WorkspaceCreator.ScriptDirectory = Environment.CurrentDirectory;	// TODO: Change?
		}

		private void OpenMenu_SubmenuOpened(object sender, RoutedEventArgs e) {
			openMenu.ItemsSource = Directory
				.EnumerateFiles(viewModel.WorkspaceCreator.ScriptDirectory)
				.Where(p => WorkspaceCreator.LanguageExtensions.ContainsKey(Path.GetExtension(p)))
				.Select(Path.GetFileName)
				.ToList();
			viewModel.WorkspaceCreator.RefreshReferenceProjects();
		}
	}
}
