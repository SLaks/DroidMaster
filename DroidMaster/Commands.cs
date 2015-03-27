using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace DroidMaster {
	class ActionCommand : ICommand {
		readonly Action action;
		public ActionCommand(Action action) { this.action = action; }

		public event EventHandler CanExecuteChanged { add { } remove { } }
		public bool CanExecute(object parameter) => true;
		public void Execute(object parameter) => action();
	}
	class ActionCommand<T> : ICommand {
		readonly Action<T> action;
		public ActionCommand(Action<T> action) { this.action = action; }

		public event EventHandler CanExecuteChanged { add { } remove { } }
		public bool CanExecute(object parameter) => true;
		public void Execute(object parameter) => action((T)parameter);
	}
}
