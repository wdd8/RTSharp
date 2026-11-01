namespace RTSharp.Daemon.RuntimeCompilation
{
	public static class ScriptEnvironment
	{
		public const string BuiltInUsingsList =
			"System;" +
			"System.Threading;" +
			"System.Threading.Tasks;" +
			"System.Collections.Generic;" +
			"Dapper;" +
			"Microsoft.Data.Sqlite;" +
			"RTSharp.Daemon.Services;" +
			"RTSharp.Daemon;" +
			"RTSharp.Shared.Abstractions;" +
			"RTSharp.Shared.Utils";

		public static readonly string[] BuiltInUsings = BuiltInUsingsList.Split(';');
	}
}
