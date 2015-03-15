using System;

namespace DroidMaster {
	class DataEventArgs<T> : EventArgs {
		public DataEventArgs(T data) { Data = data; }
		public T Data { get; }
	}
}
