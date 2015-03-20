using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.Win32;

namespace DroidMaster.Scripting.Editor {
	[Export]
	class ScriptEditorViewModel : NotifyPropertyChanged {
		public ObservableCollection<ScriptFileViewModel> Files { get; } = new ObservableCollection<ScriptFileViewModel>();
		public EditorWorkspaceCreator WorkspaceCreator { get; }

		[Import]
		public ITextEditorFactoryService EditorFactory { get; set; }

		[ImportingConstructor]
		public ScriptEditorViewModel(ExportFactory<EditorWorkspaceCreator> workspaceFactory) {
			WorkspaceCreator = workspaceFactory.CreateExport().Value;
		}

		public ICommand OpenFileCommand => new ActionCommand<string>(path =>
			OpenFile(Path.GetFullPath(Path.Combine(WorkspaceCreator.ScriptDirectory, path)))
		);

		public ICommand CloseFileCommand => new ActionCommand<ScriptFileViewModel>(vm => Files.Remove(vm));
		public ICommand NewFileCommand => new ActionCommand(() => {
			var dialog = new SaveFileDialog {
				InitialDirectory = WorkspaceCreator.ScriptDirectory,
				Filter = string.Join("|", Scripting.WorkspaceCreator.LanguageExtensions
					.OrderBy(kvp => kvp.Key)
					.Select(kvp => $"{kvp.Value} script files|*{kvp.Key}"))
			};
			if (dialog.ShowDialog() != true)
				return;

			File.WriteAllText(dialog.FileName, "");
			OpenFile(dialog.FileName);
		});

		private void OpenFile(string path) {
			if (Path.GetFileName(path).StartsWith("_"))
				WorkspaceCreator.RefreshReferenceProjects();
			else
				WorkspaceCreator.CreateScriptProject(path);

			var fileModel = new ScriptFileViewModel(WorkspaceCreator.FileDocuments[path], WorkspaceCreator.EditorBuffers[path], EditorFactory);
			Files.Add(fileModel);
			SelectedFile = fileModel;
		}

		ScriptFileViewModel selectedFile;
		public ScriptFileViewModel SelectedFile {
			get { return selectedFile; }
			set { selectedFile = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedFile)); }
		}
		public bool HasSelectedFile => SelectedFile != null;
	}

	class ScriptFileViewModel : NotifyPropertyChanged {
		public ScriptFileViewModel(ITextDocument doc, ITextBuffer editorBuffer, ITextEditorFactoryService editorFactory) {
			Document = doc;
			doc.DirtyStateChanged += (s, e) => OnPropertyChanged(nameof(IsDirty));

			TextView = editorFactory.CreateTextViewHost(
				editorFactory.CreateTextView(editorBuffer, editorFactory.CreateTextViewRoleSet(
					PredefinedTextViewRoles.Analyzable, PredefinedTextViewRoles.Document, PredefinedTextViewRoles.Editable,
					PredefinedTextViewRoles.Interactive, PredefinedTextViewRoles.PrimaryDocument,
					PredefinedTextViewRoles.Structured, PredefinedTextViewRoles.Zoomable)
				), true
			);
			TextView.TextView.Options.SetOptionValue(DefaultTextViewHostOptions.LineNumberMarginId, true);
			TextView.TextView.Options.SetOptionValue(DefaultTextViewHostOptions.ChangeTrackingId, true);
			TextView.TextView.Options.SetOptionValue(DefaultTextViewHostOptions.ShowScrollBarAnnotationsOptionId, true);
			TextView.TextView.Options.SetOptionValue(DefaultTextViewHostOptions.ShowEnhancedScrollBarOptionId, true);
		}

		public ITextDocument Document { get; }
		public IWpfTextViewHost TextView { get; }
		public bool IsDirty => Document.IsDirty;
		public ICommand SaveCommand => new ActionCommand(Document.Save);
		public string FileName => Path.GetFileName(Document.FilePath);
	}

	class ActionCommand : ICommand {
		readonly Action action;
		public ActionCommand(Action action) { this.action = action; }

		public event EventHandler CanExecuteChanged { add { } remove { } }
		public bool CanExecute(object parameter) => true;
		public void Execute(object parameter) => action();
	}
	class ActionCommand<T> : ICommand {
		readonly Action<T> action;
		public ActionCommand(Action<T> action) { this.action = action; }

		public event EventHandler CanExecuteChanged { add { } remove { } }
		public bool CanExecute(object parameter) => true;
		public void Execute(object parameter) => action((T)parameter);
	}
}
