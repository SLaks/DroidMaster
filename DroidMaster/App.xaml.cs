using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace DroidMaster {
	/// <summary>
	/// Interaction logic for App.xaml
	/// </summary>
	public partial class App : Application {
		///<summary>Gets the directory to load scripts from.</summary>
		public static string ScriptDirectory { get; } = Path.Combine(Environment.CurrentDirectory, "Scripts");

		/// <inheritdoc />
		protected override void OnStartup(StartupEventArgs e) {
			base.OnStartup(e);
			Directory.CreateDirectory(ScriptDirectory);
		}
	}
}
