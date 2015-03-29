using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editing;

namespace DroidMaster.Scripting {
	///<summary>Loads device scripts into a Roslyn Workspace.</summary>
	abstract class WorkspaceCreator {
		///<summary>Namespaces referenced in every source file.</summary>
		static readonly IReadOnlyCollection<string> StandardNamespaces = new[] {
			"System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Text",
			"System.Threading", "System.Threading.Tasks", "System.Xml.Linq",
			"DroidMaster", "DroidMaster.Models"
		};

		static IReadOnlyCollection<string> FrameworkReferences = new[] {
			"mscorlib", "System", "System.Core", "System.Xml.Linq", "Microsoft.VisualBasic"
		};
		static IReadOnlyCollection<Assembly> LocalReferences = new[] { typeof(WorkspaceCreator).Assembly };

		///<summary>Maps file extensions to Roslyn language names.</summary>
		public static readonly Dictionary<string, string> LanguageExtensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			{ ".cs", LanguageNames.CSharp },
			{ ".vb", LanguageNames.VisualBasic },
		};

		///<summary>Maps Roslyn language names to fixed source files to add to each reference project.</summary>
		///<remarks>These files prevent compiler errors from using static statements for empty reference projects.</remarks>
		static readonly IReadOnlyDictionary<string, string> ReferenceFiles = new Dictionary<string, string> {
			{ LanguageNames.CSharp, "public static partial class ReferenceCS { }" },
			{ LanguageNames.VisualBasic, "Public Partial Module ReferenceVB\r\nEnd Module" }
		};

		///<summary>Maps Roslyn language names to the prefix and suffix to wrap reference files in.</summary>
		static readonly IReadOnlyDictionary<string, Tuple<string, string>> ReferenceWrappers = new Dictionary<string, Tuple<string, string>> {
			{ LanguageNames.CSharp, Tuple.Create(string.Concat(StandardNamespaces.Select(n => $"using {n};\r\n"))
											   + "public static partial class ReferenceCS {\r\n",
												 "\r\n}") },
			{ LanguageNames.VisualBasic, Tuple.Create(string.Concat(StandardNamespaces.Select(n => $"Imports {n}\r\n"))
													+ "Public Partial Module ReferenceVB\r\n",
													  "\r\nEnd Module") }
		};

		///<summary>Maps Roslyn language names to the prefix and suffix to wrap scripts in.</summary>
		static readonly IReadOnlyDictionary<string, Tuple<string, string>> ScriptWrappers = new Dictionary<string, Tuple<string, string>> {
			{ LanguageNames.CSharp, Tuple.Create(
				string.Concat(StandardNamespaces.Select(n => $"using {n};\r\n"))
			  + "\r\nusing static ReferenceCS;\r\n"
			  + "\r\nusing static ReferenceVB;\r\n"
			  + "\r\npublic static async Task Run(DeviceModel device, CancellationToken cancellationToken) {\r\n",
				"\r\n}"	// We append a constant field initialized to the path, following this hard-coded documentation comment.
			  + "\r\n///<summary>Gets the full path to the directory containing the script, including the trailing \\.</summary>") },
			{ LanguageNames.VisualBasic, Tuple.Create(
				string.Concat(StandardNamespaces.Select(n => $"Imports {n}\r\n"))
			  + "\r\nImports ReferenceCS\r\n"	// ReferenceVB is a module, so we don't need to import it
			  + "\r\nPublic Shared Async Function Run(device As DeviceModel, cancellationToken As CancellationToken) As Task\r\n",
				"\r\nEnd Function"
			  + "\r\n'''<summary>Gets the full path to the directory containing the script, including the trailing \\.</summary>") }
		};


		///<summary>Gets or sets the directory to load scripts from.</summary>
		public string ScriptDirectory { get; set; }

		///<summary>Gets the Workspace manipulated by this instance.</summary>
		public Workspace Workspace { get; }

		///<summary>Gets project references to add to every script.</summary>
		public IReadOnlyCollection<ProjectId> ReferenceProjects { get; private set; }

		protected WorkspaceCreator(Workspace workspace) {
			Workspace = workspace;
		}

		///<summary>Refreshes the list of projects referenced by every script, updating the references for all script projects.</summary>
		public void RefreshReferenceProjects() {
			foreach (var projectId in ReferenceProjects ?? new ProjectId[0]) {
				foreach (var documentId in Workspace.CurrentSolution.GetProject(projectId).DocumentIds)
					CloseDocument(documentId);
				Workspace.TryApplyChanges(Workspace.CurrentSolution.RemoveProject(projectId));
			}

			ReferenceProjects = LanguageExtensions.Select(kvp => {
				var projectName = "DroidMaster.References." + kvp.Value;
				var project = Workspace.CurrentSolution
					.AddProject(projectName, projectName + "-" + Guid.NewGuid(), kvp.Value)
					.AddMetadataReferences(FrameworkReferences.Select(CreateFrameworkReference))
					.AddMetadataReferences(LocalReferences.Select(CreateLocalReference))
					.AddDocument("Reference" + kvp.Key, ReferenceFiles[kvp.Value]).Project;

				project = project.WithCompilationOptions(project.CompilationOptions
					.WithOutputKind(OutputKind.DynamicallyLinkedLibrary));

				// Reference projects cannot use Script documents, because they wrap
				// everything in an internal Script class.  Instead, I make a normal
				// document, and wrap all of the source in a public static class.  I
				// add a using static directive for this class to every script file,
				// allowing public top-level classes and members to be used directly
				// (extension methods work without any modifications).
				Workspace.TryApplyChanges(project.Solution);

				foreach (var path in Directory.EnumerateFiles(ScriptDirectory, "*" + kvp.Key)
											  .Where(f => Path.GetFileName(f).StartsWith("_"))) {
					OpenDocument(project.Id, path, ReferenceWrappers[kvp.Value]);
				}
				return project.Id;
			}).ToList();

			foreach (var scriptProject in Workspace.CurrentSolution.ProjectIds.Except(ReferenceProjects)) {
				Workspace.TryApplyChanges(Workspace.CurrentSolution
					.WithProjectReferences(scriptProject, ReferenceProjects.Select(p => new ProjectReference(p)))
				);
			}
		}

		///<summary>Creates a project for the specified script file.</summary>
		public Project CreateScriptProject(string scriptFile) {
			if (ReferenceProjects == null) RefreshReferenceProjects();

			var projectName = "DroidMaster.Scripts." + Path.GetFileNameWithoutExtension(scriptFile);
			var language = LanguageExtensions[Path.GetExtension(scriptFile)];

			var project = Workspace.CurrentSolution
				.AddProject(projectName, projectName + "-" + Guid.NewGuid(), language)
					.AddMetadataReferences(FrameworkReferences.Select(CreateFrameworkReference))
					.AddMetadataReferences(LocalReferences.Select(CreateLocalReference))
				.AddProjectReferences(ReferenceProjects.Select(p => new ProjectReference(p)));

			project = project
				.WithParseOptions(project.ParseOptions.WithKind(SourceCodeKind.Script))
				.WithCompilationOptions(project.CompilationOptions
					.WithOutputKind(OutputKind.DynamicallyLinkedLibrary));
			Workspace.TryApplyChanges(project.Solution);

			// SyntaxGenerator cannot generate XML doc comments, so I put them at the end of the wrappers.
			var syntaxGenerator = project.LanguageServices.GetRequiredService<SyntaxGenerator>();
			var directoryField = syntaxGenerator.FieldDeclaration("ScriptDirectory",
				syntaxGenerator.TypeExpression(SpecialType.System_String),
				Accessibility.Private, DeclarationModifiers.Const,
				syntaxGenerator.LiteralExpression(Path.GetDirectoryName(scriptFile) + @"\")
			).NormalizeWhitespace();

			var wrappers = ScriptWrappers[language];
			OpenDocument(project.Id, scriptFile, Tuple.Create(wrappers.Item1, wrappers.Item2 + "\r\n" + directoryField.ToFullString()));

			return Workspace.CurrentSolution.GetProject(project.Id);
		}

		///<summary>Opens a file path into a Roslyn <see cref="Document"/>, and adds the document to a project in the current solution.</summary>
		/// <param name="projectId">The project to create the document in.</param>
		/// <param name="path">The path to the file to open.</param>
		/// <param name="wrapper">The strings to wrap the file contents in.</param>
		///<remarks>In editor scenarios, this should create a TextBuffer.</remarks>
		protected abstract void OpenDocument(ProjectId projectId, string path, Tuple<string, string> wrapper);

		///<summary>Closes a document that was previously opened by <see cref="OpenDocument"/>, if necessary.</summary>
		protected virtual void CloseDocument(DocumentId documentId) { }

		///<summary>Creates a <see cref="MetadataReference"/> to a BCL assembly.</summary>
		protected abstract MetadataReference CreateFrameworkReference(string assemblyName);

		///<summary>Creates a <see cref="MetadataReference"/> to an assembly that is already loaded.</summary>
		protected abstract MetadataReference CreateLocalReference(Assembly assembly);
	}
}
