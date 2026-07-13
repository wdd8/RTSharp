using RTSharp.Daemon.RuntimeCompilation.Exceptions;

using System.Reflection;
using System.Runtime.Loader;

namespace RTSharp.Daemon.RuntimeCompilation
{
	public class DynamicScript<T> : IDisposable
	{
		private class UnloadableLoadContext : AssemblyLoadContext
		{
			public UnloadableLoadContext()
				: base(true)
			{

			}

			protected override Assembly Load(AssemblyName assemblyName)
			{
				return null!;
			}
		}

		private Type? _classType { get; set; }

		public MemoryStream Assembly { get; set; }
		public Type ClassType => _classType!;
		public string[] Tags { get; }

		private UnloadableLoadContext? Alc { get; set; }
		private bool IsDisposed { get; set; }

		private int RefCount;
		private readonly Lock DisposeLock = new();

		private readonly TimeSpan IdleTimeout;
		private readonly Timer IdleTimer;

		private readonly Action<DynamicScript<T>> OnIdleTimeout;

		public DynamicScript(MemoryStream Assembly, string[] Tags, TimeSpan IdleTimeout, Action<DynamicScript<T>> OnIdleTimeout)
		{
			this.Assembly = Assembly;
			this.Tags = Tags;
			this.IdleTimeout = IdleTimeout;
			this.OnIdleTimeout = OnIdleTimeout;

			IdleTimer = new Timer(static state => {
				var self = (DynamicScript<T>)state!;
				self.OnIdleTimeout(self);
			}, this, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

			Alc = new UnloadableLoadContext();
			try {
				var asm = Alc.LoadFromStream(Assembly);

				_classType = asm.GetExportedTypes().FirstOrDefault(x => typeof(T).IsAssignableFrom(x));
			} catch (Exception) {
				Dispose(true);
				throw;
			}

			if (_classType == null) {
				Dispose(true);
				throw new InterfaceNotFoundException($"No classes implement {typeof(T).Name}");
			}

			IdleTimer.Change(IdleTimeout, Timeout.InfiniteTimeSpan);
		}

		public bool TryAddRef()
		{
			lock (DisposeLock) {
				if (IsDisposed)
					return false;

				RefCount++;
				IdleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
				return true;
			}
		}

		public void RemoveRef()
		{
			lock (DisposeLock) {
				if (IsDisposed)
					return;

				if (--RefCount <= 0) {
					IdleTimer.Change(IdleTimeout, Timeout.InfiniteTimeSpan);
				}
			}
		}

		public bool TryExpire()
		{
			lock (DisposeLock) {
				if (IsDisposed || RefCount > 0)
					return false;

				Dispose();
				return true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool Disposing)
		{
			if (!Disposing) {
				if (!IsDisposed) {
					throw new Exception($"Should not happen 5362 {this.GetHashCode()}");
				}
				return;
			}

			lock (DisposeLock) {
				if (IsDisposed)
					return;

				IdleTimer.Dispose();

				_classType = null;
				Alc!.Unload();
				Alc = null;
				try { Assembly?.Dispose(); } catch { }
				Assembly = null!;

				IsDisposed = true;
			}
		}

		~DynamicScript()
		{
			Dispose(false);
		}
	}
}
