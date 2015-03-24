using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DroidMaster {
	static class Extensions {
		public static void RunAsync(this SynchronizationContext context, Action a)
			=> context.Post(o => ((Action)o)(), a);
	}
}
