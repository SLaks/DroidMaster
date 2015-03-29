using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Composition;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using Microsoft.Win32;

namespace DroidMaster.Scripting.Editor {
	[Export]
	class ScriptEditorViewModel : NotifyPropertyChanged {
		public ObservableCollection<ScriptFileViewModel> Files { get; } = new ObservableCollection<ScriptFileViewModel>();
		public EditorWorkspaceCreator WorkspaceCreator { get; }

		[Import]
		public ITextEditorFactoryService EditorFactory { get; set; }
		[Import]
		public IEditorOptionsFactoryService OptionsFactory { get; set; }

		[ImportingConstructor]
		public ScriptEditorViewModel(ExportFactory<EditorWorkspaceCreator> workspaceFactory) {
			WorkspaceCreator = workspaceFactory.CreateExport().Value;
		}

		///<summary>Refreshes all cached buffers that are not open in any tab. This will apply changes in closed reference script to IntelliSense.</summary>
		public void RefreshClosedFiles() {
			var openFiles = Files.Select(f => f.Document);
			foreach (var otherFile in WorkspaceCreator.FileDocuments.Values.Except(openFiles)) {
				otherFile.Reload();
			}
		}

		public ICommand OpenFileCommand => new ActionCommand<string>(path =>
			OpenFile(Path.GetFullPath(Path.Combine(WorkspaceCreator.ScriptDirectory, path)))
		);

		public ICommand CloseFileCommand => new ActionCommand<ScriptFileViewModel>(vm => {
			if (vm.IsDirty) {
				switch (MessageBox.Show($"Do you want to save changes to {vm.FileName}?",
										"DroidMaster", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning)) {
					case MessageBoxResult.Cancel:
						return;
					case MessageBoxResult.Yes:
						vm.Document.Save();
						break;
					case MessageBoxResult.No:
						// If the user says not to save, we must actually blow
						// away the changes. Otherwise, reopening the document
						// would restore the changes from the cached buffer in
						// the WorkspaceCreator.  This is especially important
						// for reference scripts.
						vm.Document.Reload();
						break;
				}
			}
			Files.Remove(vm);
		});

		///<summary>Maps Roslyn language names to comment line prefixes.</summary>
		static readonly IReadOnlyDictionary<string, string> CommentPrefixes = new Dictionary<string, string> {
			{ LanguageNames.CSharp, "// " },
			{ LanguageNames.VisualBasic, "' " }
		};

		const string ScriptComment = @"
DroidMaster Device Script
—————————————————————————
This script will run against connected Android devices.
Write code using the pre-supplied `device` parameter to
control the current device.  Your code is wrapped in an
async method; you must await calls to every method. The
script also receives a cancellationToken parameter; you
can pass this token to external asynchronous calls like
Task.Delay(). All methods on device automatically honor
this token, so there is no need to explicitly pass it.

The folder containing the script is available in the
ScriptDirectory property.

To create reusable methods or classes, make a file that
starts with an underscore; it will be referenced by all
script files.";
		const string ReferenceComment = @"
DroidMaster Reference Script
————————————————————————————

This file will be referenced by every device script.  
You can write helper classes, methods, and extension
methods here, and they will be available in scripts.
All scripts run in the same AppDomain, so you should
not use any static mutable state.

Scripts in any language will reference all reference
files in all languages.  Reference files in the same
language also reference each-other.  Reference files
in different languages do not reference each-other.";

		public ICommand NewFileCommand => new ActionCommand(() => {
			var dialog = new SaveFileDialog {
				InitialDirectory = WorkspaceCreator.ScriptDirectory,
				Filter = string.Join("|", Scripting.WorkspaceCreator.LanguageExtensions
					.OrderBy(kvp => kvp.Key)
					.Select(kvp => $"{kvp.Value} script files|*{kvp.Key}"))
			};
			if (dialog.ShowDialog() != true)
				return;

			var isReference = Path.GetFileName(dialog.FileName).StartsWith("_");
			File.WriteAllText(dialog.FileName, (isReference ? ReferenceComment : ScriptComment)
				.Replace("\r\n", "\r\n" + CommentPrefixes[Scripting.WorkspaceCreator.LanguageExtensions[Path.GetExtension(dialog.FileName)]])
				.Trim()
			  + "\r\n");
			OpenFile(dialog.FileName);
		});

		private void OpenFile(string path) {
			if (Path.GetFileName(path).StartsWith("_"))
				WorkspaceCreator.RefreshReferenceProjects();
			else
				WorkspaceCreator.CreateScriptProject(path);

			var fileModel = new ScriptFileViewModel(WorkspaceCreator.FileDocuments[path], WorkspaceCreator.EditorBuffers[path], EditorFactory, OptionsFactory);
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
		public ScriptFileViewModel(ITextDocument doc, ITextBuffer editorBuffer, ITextEditorFactoryService editorFactory, IEditorOptionsFactoryService optionsFactory) {
			Document = doc;
			doc.DirtyStateChanged += (s, e) => OnPropertyChanged(nameof(IsDirty));

			TextView = editorFactory.CreateTextViewHost(
				editorFactory.CreateTextView(new TextDataModel(doc, editorBuffer), editorFactory.AllPredefinedRoles, optionsFactory.GlobalOptions), true
			);
			TextView.TextView.Options.SetOptionValue(DefaultTextViewHostOptions.LineNumberMarginId, true);
			TextView.TextView.Options.SetOptionValue(DefaultTextViewHostOptions.ChangeTrackingId, true);
			TextView.TextView.Options.SetOptionValue(DefaultTextViewHostOptions.ShowScrollBarAnnotationsOptionId, true);
			TextView.TextView.Options.SetOptionValue(DefaultTextViewHostOptions.ShowEnhancedScrollBarOptionId, true);
		}

		// This is necessary to tell TextView-level exports the correct
		// ContentType of the TextView, while leaving the TextBuffer be
		// type projection so that classifications are forwarded.
		class TextDataModel : ITextDataModel {
			public TextDataModel(ITextDocument document, ITextBuffer editorBuffer) {
				DataBuffer = editorBuffer;
				DocumentBuffer = document.TextBuffer;
			}
			public IContentType ContentType => DocumentBuffer.ContentType;
			public ITextBuffer DataBuffer { get; }
			public ITextBuffer DocumentBuffer { get; }
			public event EventHandler<TextDataModelContentTypeChangedEventArgs> ContentTypeChanged {
				add { }
				remove { }
			}
		}

		public ITextDocument Document { get; }
		public IWpfTextViewHost TextView { get; }
		public bool IsDirty => Document.IsDirty;
		public ICommand SaveCommand => new ActionCommand(Document.Save);
		public string FileName => Path.GetFileName(Document.FilePath);
	}
}
