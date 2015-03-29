using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DroidMaster.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DroidMaster.Scripting {
	///<summary>A delegate that contains a compiled script to run against a device.</summary>
	delegate Task DeviceScript(DeviceModel device);

	///<summary>A <see cref="WorkspaceCreator"/> that creates workspaces used to compile and run scripts.  This does not depend on Visual Studio.</summary>
	class RuntimeWorkspaceCreator : WorkspaceCreator {
		public RuntimeWorkspaceCreator() : base(new AdhocWorkspace()) { }

		protected override void OpenDocument(ProjectId projectId, string path, Tuple<string, string> wrapper) {
			var text = File.ReadAllText(path);
			text = wrapper.Item1 + text + wrapper.Item2;

			Workspace.TryApplyChanges(Workspace.CurrentSolution
				.GetProject(projectId)
				.AddDocument(Path.GetFileName(path), SourceText.From(text), filePath: path)
				.Project.Solution
			);
		}

		protected override MetadataReference CreateFrameworkReference(string assemblyName) 
			=> MetadataReference.CreateFromAssembly(
				Assembly.Load(assemblyName + ", Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")
			);
		protected override MetadataReference CreateLocalReference(Assembly assembly)
			=> MetadataReference.CreateFromAssembly(assembly);

		static async Task<Assembly> LoadProject(Project project, CancellationToken cancellationToken) {
			var stream = new MemoryStream();
			var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
			var result = compilation.Emit(stream, cancellationToken: cancellationToken);
			if (!result.Success)
				throw new Exception($"Errors occurred while compiling {project.Name}:\r\n{string.Join("\r\n", result.Diagnostics)}");
			return Assembly.Load(stream.ToArray());
		}

		///<summary>Compiles and loads a script, returning a delegate in the loaded assembly.</summary>
		public async Task<DeviceScript> CompileScript(string scriptFile, CancellationToken cancellationToken = default(CancellationToken)) {
			if (Path.GetFileName(scriptFile).StartsWith("_"))
				throw new ArgumentException("Reference scripts cannot be compiled directly.", nameof(scriptFile));

			var assemblies = await Task.WhenAll(new[] { CreateScriptProject(scriptFile) }
				.Concat(ReferenceProjects.Select(Workspace.CurrentSolution.GetProject))
				.Select(p => LoadProject(p, cancellationToken))
			).ConfigureAwait(false);
			var scriptType = assemblies.First().GetType("Script");
			return (DeviceScript)Delegate.CreateDelegate(typeof(DeviceScript), scriptType, "Run");
		}
	}
}
