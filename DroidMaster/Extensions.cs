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

		public static async Task<IDisposable> DisposableWaitAsync(this SemaphoreSlim semaphore) {
			await semaphore.WaitAsync().ConfigureAwait(false);
			return new DisposableAction(() => semaphore.Release());
		}

		public static async Task<IDisposable> DisposableWaitAsync(this SemaphoreSlim semaphore, CancellationToken cancellationToken) {
			await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
			return new DisposableAction(() => semaphore.Release());
		}
		class DisposableAction : IDisposable {
			readonly Action action;
			public DisposableAction(Action action) { this.action = action; }
			public void Dispose() {
				action();
			}
		}
	}
}
