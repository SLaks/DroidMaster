using System;
using System.Collections.Generic;
using System.ComponentModel.Composition.Hosting;
using System.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
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

		[ImportingConstructor]
		public EditorWorkspaceCreator(SVsServiceProvider serviceProvider)
			: base(new EditorWorkspace(MefV1HostServices.Create(GetExportProvider(serviceProvider)))) {
		}

		private static ExportProvider GetExportProvider(SVsServiceProvider serviceProvider)
			=> ((IComponentModel)serviceProvider.GetService(typeof(SComponentModel))).DefaultExportProvider;


		readonly Dictionary<string, ITextDocument> fileDocuments = new Dictionary<string, ITextDocument>(StringComparer.OrdinalIgnoreCase);
		///<summary>Maps full paths on disk to open <see cref="ITextDocument"/>s.</summary>
		public IReadOnlyDictionary<string, ITextDocument> FileDocuments => fileDocuments;

		readonly Dictionary<string, IElisionBuffer> editorBuffers = new Dictionary<string, IElisionBuffer>(StringComparer.OrdinalIgnoreCase);
		///<summary>Maps full paths on disk to <see cref="IElisionBuffer"/>s to show in the editor.</summary>
		public IReadOnlyDictionary<string, IElisionBuffer> EditorBuffers => editorBuffers;

		protected override MetadataReference CreateAssemblyReference(string assemblyName) {
			// TODO: Better check for framework vs. non-framework assemblies.
			if (assemblyName != typeof(WorkspaceCreator).Assembly.GetName().Name)
				return EditorWorkspace.CreateFrameworkReference(assemblyName);

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
			// The inner buffers only exist to provide content for the outer
			// buffer, so they have ContentType text.  The projection buffer
			// exists to provide Roslyn's language services; it has a Roslyn
			// content type and is linked to the Workspace. Finally, we make
			// an elision buffer to display in the editor, eliding the outer
			// wrappers from the projection buffer.

			path = Path.GetFullPath(path);

			var contentType = ContentTypes.GetContentType(LanguageContentTypes[Workspace.CurrentSolution.GetProject(projectId).Language]);
			IElisionBuffer elisionBuffer;
			if (!EditorBuffers.TryGetValue(path, out elisionBuffer)) {
				var fileDocument = DocumentFactory.CreateAndLoadTextDocument(path, ContentTypes.GetContentType("text"));
				fileDocuments.Add(path, fileDocument);

				var prefixBuffer = BufferFactory.CreateTextBuffer(wrapper.Item1, contentType);
				var suffixBuffer = BufferFactory.CreateTextBuffer(wrapper.Item2, contentType);

				var fileSnapshot = fileDocument.TextBuffer.CurrentSnapshot;
				var outerBuffer = ProjectionFactory.CreateProjectionBuffer(null, new object[] {
					wrapper.Item1,
					fileSnapshot.CreateTrackingSpan(new Span(0, fileSnapshot.Length), SpanTrackingMode.EdgeInclusive),
					wrapper.Item2
				}, ProjectionBufferOptions.None, contentType);

				elisionBuffer = ProjectionFactory.CreateElisionBuffer(null, new NormalizedSnapshotSpanCollection(
					new SnapshotSpan(outerBuffer.CurrentSnapshot, wrapper.Item1.Length, fileSnapshot.Length)
				), ElisionBufferOptions.None);
				editorBuffers.Add(path, elisionBuffer);
			}

			Workspace.CreateDocument(projectId, elisionBuffer.SourceBuffer);
		}

		protected override void CloseDocument(DocumentId documentId) => Workspace.CloseDocument(documentId);
	}
}
