using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DroidMaster.Models {
	partial class DeviceModel {
		///<summary>
		/// Collects progress from all <see cref="ProgressModel"/>s added 
		/// after this call into an aggregate progress bar.  The returned 
		/// object should be wrapped in a using statement.
		///</summary>
		/// <param name="desccription">The caption to display in the status column in the grid.</param>
		public AggregateProgressModel AggregateProgress(string desccription) {
			return new AggregateProgressModel(desccription, this);
		}
	}

	///<summary>A WPF-bindable status model that aggregates all active <see cref="ProgressModel"/>s for a device.</summary>
	public class AggregateProgressModel : ProgressModel, IDisposable {
		readonly DeviceModel deviceModel;
		ImmutableStack<ProgressModel> contributors = ImmutableStack.Create<ProgressModel>();

		internal AggregateProgressModel(string description, DeviceModel deviceModel) : base(description) {
			this.deviceModel = deviceModel;
			deviceModel.Status = this;
			((INotifyCollectionChanged)deviceModel.LogItems).CollectionChanged += Log_CollectionChanged;
		}

		private void Log_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) {
			foreach (var item in e.NewItems.OfType<ProgressModel>()) {
				ImmutableInterlocked.Push(ref contributors, item);
				item.PropertyChanged += delegate { RefreshProgress(); };
			}
			RefreshProgress();
		}

		void RefreshProgress() {
			Progress = contributors
				.Select(p => p.Progress)
				.DefaultIfEmpty()
				.Average();
		}

		///<summary>Indicates whether the operation is still active, and should suppress changes to <see cref="DeviceModel.Status"/>.</summary>
		public bool IsActive { get; private set; } = true;

		///<summary>Stops aggregating progress, clearing the <see cref="DeviceModel.Status"/> property.</summary>
		public void Dispose() {
			((INotifyCollectionChanged)deviceModel.LogItems).CollectionChanged -= Log_CollectionChanged;
			IsActive = false;
		}
	}
}
