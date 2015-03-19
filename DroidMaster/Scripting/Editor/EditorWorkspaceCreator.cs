using System;
using System.Collections.Generic;
using System.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Projection;
using Microsoft.VisualStudio.Utilities;
using VSEmbed.Roslyn;

namespace DroidMaster.Scripting.Editor {
	[Export]
	class EditorWorkspaceCreator : WorkspaceCreator {
		public new EditorWorkspace Workspace => (EditorWorkspace)base.Workspace;

		[Import]
		public IProjectionBufferFactoryService ProjectionFactory { get; set; }
		[Import]
		public ITextBufferFactoryService BufferFactory { get; set; }
		[Import]
		public ITextDocumentFactoryService DocumentFactory { get; set; }
		[Import]
		public IContentTypeRegistryService ContentTypes { get; set; }

		public EditorWorkspaceCreator(HostServices host, string scriptDirectory) : base(new EditorWorkspace(host), scriptDirectory) { }

		readonly Dictionary<string, ITextDocument> openDocuments = new Dictionary<string, ITextDocument>(StringComparer.OrdinalIgnoreCase);
		///<summary>Maps full paths on disk to open <see cref="ITextDocument"/>s.</summary>
		public IReadOnlyDictionary<string, ITextDocument> OpenDocuments => openDocuments;

		protected override MetadataReference CreateAssemblyReference(string assemblyName) {
			// TODO: Better check for framework vs. non-framework assemblies.
			if (assemblyName != typeof(WorkspaceCreator).Assembly.GetName().Name)
				return Workspace.CreateFrameworkReference(assemblyName);

			var location = Assembly.Load(assemblyName).Location;
			var xmlDocFile = Path.ChangeExtension(location, ".xml");
			return MetadataReference.CreateFromFile(location,
				documentation: File.Exists(xmlDocFile) ? new XmlDocumentationProvider(xmlDocFile) : null
			);
		}

		static readonly IReadOnlyDictionary<string, string> LanguageContentTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
			{ LanguageNames.CSharp, "CSharp" },
			{ LanguageNames.VisualBasic, "Basic" }
		};

		protected override void OpenDocument(ProjectId projectId, string path, Tuple<string, string> wrapper) {
			// I create an inner buffer containing the contents of the file,
			// then wrap it in a projection buffer with the prefix & suffix.

			path = Path.GetFullPath(path);

			var contentType = ContentTypes.GetContentType(LanguageContentTypes[Workspace.CurrentSolution.GetProject(projectId).Language]);
			ITextDocument innerTextDocument;
			if (!OpenDocuments.TryGetValue(path, out innerTextDocument)) {
				innerTextDocument = DocumentFactory.CreateAndLoadTextDocument(path, contentType);
				openDocuments.Add(path, innerTextDocument);
			}

			var prefixBuffer = BufferFactory.CreateTextBuffer(wrapper.Item1, contentType);
			var suffixBuffer = BufferFactory.CreateTextBuffer(wrapper.Item2, contentType);

			var outerBuffer = ProjectionFactory.CreateProjectionBuffer(null, new[] { prefixBuffer, innerTextDocument.TextBuffer, suffixBuffer }, ProjectionBufferOptions.None);
			Workspace.CreateDocument(projectId, outerBuffer);
		}
	}
}
