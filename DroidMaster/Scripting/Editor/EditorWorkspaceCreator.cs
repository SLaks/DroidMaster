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


		//static ExportProvider GetExportProvider(IServiceProvider ser

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

			var outerBuffer = ProjectionFactory.CreateProjectionBuffer(null,
				new[] { prefixBuffer, innerTextDocument.TextBuffer, suffixBuffer }.Select(b =>
					b.CurrentSnapshot.CreateTrackingSpan(
						new Span(0, b.CurrentSnapshot.Length),
						SpanTrackingMode.EdgeInclusive)
				).ToList<object>(),
				ProjectionBufferOptions.None
			);
			Workspace.CreateDocument(projectId, outerBuffer);
		}
	}
}
