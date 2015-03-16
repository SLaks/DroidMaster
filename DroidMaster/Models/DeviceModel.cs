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
	///<summary>A wrapper around <see cref="PersistentDevice"/>.</summary>
	///<remarks>The public members of this class form the API  for device scripts.</remarks>
	public class DeviceModel : INotifyPropertyChanged {
		internal DeviceModel(PersistentDevice device) {
			Device = device;
		}

		internal PersistentDevice Device { get; }

		object status;
		///<summary>Gets or sets a WPF-bindable short status model object (eg, a string or a <see cref="ProgressModel"/>) to display in the grid.</summary>
		public object Status {
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

	///<summary>A WPF-bindable view model that reports the progress of an operation.</summary>
	public class ProgressModel : IProgress<double>, INotifyPropertyChanged {
		public void Report(double value) => Progress = value;

		double progress;
		///<summary>Gets or sets the progress of the operation, between 0 and 1.</summary>
		public double Progress {
			get { return progress; }
			set { progress = value; OnPropertyChanged(); }
		}

		string description;
		///<summary>Gets or sets a short description of the operation taking place.</summary>
		public string Description {
			get { return description; }
			set { description = value; OnPropertyChanged(); }
		}

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
