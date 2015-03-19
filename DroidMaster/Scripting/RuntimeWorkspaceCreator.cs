using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DroidMaster.Scripting {
	///<summary>A <see cref="WorkspaceCreator"/> that creates workspaces used to compile and run scripts.  This does not depend on Visual Studio.</summary>
	class RuntimeWorkspaceCreator : WorkspaceCreator {
		public RuntimeWorkspaceCreator(string scriptDirectory) : base(new AdhocWorkspace(), scriptDirectory) { }

		protected override DocumentInfo OpenDocument(ProjectId projectId, string path, Tuple<string, string> wrapper) {
			var text = File.ReadAllText(path);
			text = wrapper.Item1 + text + wrapper.Item2;

			return DocumentInfo.Create(
				DocumentId.CreateNewId(projectId, Path.GetFileName(path)),
				Path.GetFileNameWithoutExtension(path),
				sourceCodeKind: SourceCodeKind.Script,
				loader: TextLoader.From(TextAndVersion.Create(SourceText.From(text), VersionStamp.Create(), path)),
				filePath: path
			);
		}

		protected override MetadataReference CreateAssemblyReference(string assemblyName) {
			return MetadataReference.CreateFromAssembly(Assembly.Load(assemblyName));
		}
	}
}
