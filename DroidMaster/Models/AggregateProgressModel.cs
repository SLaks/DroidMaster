using System;
using System.Collections.Generic;
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
		readonly List<ProgressModel> contributors = new List<ProgressModel>();

		internal AggregateProgressModel(string description, DeviceModel deviceModel) : base(description) {
			this.deviceModel = deviceModel;
			deviceModel.Status = this;
			((INotifyCollectionChanged)deviceModel).CollectionChanged += Log_CollectionChanged;
        }

		private void Log_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) {
			foreach (var item in e.NewItems.OfType<ProgressModel>()) {
				contributors.Add(item);
				item.PropertyChanged += delegate { RefreshProgress(); };
			}
			RefreshProgress();
		}

		void RefreshProgress() {
			Progress = contributors.Average(p => p.Progress);
		}

		///<summary>Stops aggregating progress, clearing the <see cref="DeviceModel.Status"/> property.</summary>
		public void Dispose() {
			((INotifyCollectionChanged)deviceModel).CollectionChanged -= Log_CollectionChanged;
			deviceModel.Status = null;
		}
	}
}
