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

		public DynamicScript(MemoryStream Assembly, string[] Tags)
		{
			this.Assembly = Assembly;
			this.Tags = Tags;

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

			if (IsDisposed)
				return;

			_classType = null;
			Alc!.Unload();
			Alc = null;
			try { Assembly?.Dispose(); } catch { }
			Assembly = null!;

			IsDisposed = true;
		}

		~DynamicScript()
		{
			Dispose(false);
		}
	}
}
