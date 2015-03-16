using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using DroidMaster.Core;

namespace DroidMaster.Models {
	///<summary>A wrapper around <see cref="PersistentDevice"/></summary>
	public class DeviceModel : INotifyPropertyChanged {
		internal DeviceModel(PersistentDevice device) {
			Device = device;
		}

		internal PersistentDevice Device { get; }

		string status;
		///<summary>Gets or sets a short status message to display in the grid.</summary>
		public string Status {
			get { return status; }
			set { status = value; OnPropertyChanged(); }
		}

		///<summary>Gets a collection of WPF-bindable objects containing output from script commands.</summary>
		public ObservableCollection<object> Log { get; } = new ObservableCollection<object>();

		///<summary>Occurs when a property value is changed.</summary>
		public event PropertyChangedEventHandler PropertyChanged;
		///<summary>Raises the PropertyChanged event.</summary>
		///<param name="name">The name of the property that changed.</param>
		protected virtual void OnPropertyChanged([CallerMemberName] string name = null) => OnPropertyChanged(new PropertyChangedEventArgs(name));
		///<summary>Raises the PropertyChanged event.</summary>
		///<param name="e">An EventArgs object that provides the event data.</param>
		protected virtual void OnPropertyChanged(PropertyChangedEventArgs e) => PropertyChanged?.Invoke(this, e);
	}
}
