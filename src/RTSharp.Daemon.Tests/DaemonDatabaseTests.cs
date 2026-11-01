using Dapper;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using RTSharp.Daemon.RuntimeCompilation;
using RTSharp.Daemon.Services;
using RTSharp.Shared.Abstractions;

using Xunit;

namespace RTSharp.Daemon.Tests;

public sealed class DaemonDatabaseTests : IDisposable
{
    private readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "RTSharp.DatabaseTests", Guid.NewGuid().ToString("N"));
    private DaemonDatabase CreateDatabase() => new(Path.Combine(DirectoryPath, "daemon.db"));

    public sealed class Entry
    {
        public long Id { get; set; }
        public string Value { get; set; } = "";
    }

    [Fact]
    public void ConnectionsSharePersistentDataAndEnforceForeignKeys()
    {
        var database = CreateDatabase();
        using (var connection = database.OpenConnection()) {
            connection.Execute("CREATE TABLE Entries (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL)");
            connection.Execute("INSERT INTO Entries (Id, Value) VALUES (@Id, @Value)", new { Id = 1, Value = "it's persistent" });
            connection.Execute("CREATE TABLE Children (ParentId INTEGER REFERENCES Entries(Id))");
        }

        var reopened = CreateDatabase();
        using var first = database.OpenConnection();
        using var second = reopened.OpenConnection();
        Assert.NotSame(first, second);
        Assert.Equal("wal", second.QuerySingle<string>("PRAGMA journal_mode"));
        Assert.Equal(1, second.QuerySingle<int>("PRAGMA foreign_keys"));
        var entry = second.QuerySingle<Entry>("SELECT Id, Value FROM Entries WHERE Id = @Id", new { Id = 1 });
        Assert.Equal("it's persistent", entry.Value);
        Assert.Throws<SqliteException>(() => second.Execute("INSERT INTO Children (ParentId) VALUES (2)"));
    }

    [Fact]
    public void MigrationsAreIndependentRepeatableAndAtomic()
    {
        var database = CreateDatabase();
        Action<SqliteConnection, SqliteTransaction> initial = (connection, transaction) =>
            connection.Execute("CREATE TABLE Entries (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL)", transaction: transaction);
        database.Migrate("example.entries", initial);
        database.Migrate("example.entries", initial);
        database.Migrate("example.other", (connection, transaction) =>
            connection.Execute("CREATE TABLE OtherEntries (Id INTEGER PRIMARY KEY)", transaction: transaction));

        Assert.Throws<InvalidOperationException>(() => database.Migrate("example.entries", initial,
            (connection, transaction) => {
                connection.Execute("ALTER TABLE Entries ADD COLUMN Added TEXT", transaction: transaction);
                connection.Execute("INSERT INTO Entries (Id, Value) VALUES (1, 'rolled back')", transaction: transaction);
                throw new InvalidOperationException("Migration failed");
            }));

        using (var connection = database.OpenConnection()) {
            Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM Entries"));
            Assert.DoesNotContain("Added", connection.Query<string>("SELECT name FROM pragma_table_info('Entries')"));
        }

        database.Migrate("example.entries", initial, (connection, transaction) =>
            connection.Execute("ALTER TABLE Entries ADD COLUMN Added TEXT", transaction: transaction));
        Assert.Throws<InvalidOperationException>(() => database.Migrate("example.entries", initial));
    }

    [Fact]
    public async Task ConcurrentMigrationsRunOnce()
    {
        var database = CreateDatabase();
        var applications = 0;
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
            database.Migrate("example.concurrent", (connection, transaction) => {
                connection.Execute("CREATE TABLE ConcurrentEntries (Id INTEGER PRIMARY KEY)", transaction: transaction);
                Interlocked.Increment(ref applications);
            }))));
        Assert.Equal(1, applications);
    }

    [Fact]
    public void DisposingUncommittedTransactionRollsBack()
    {
        var database = CreateDatabase();
        using var connection = database.OpenConnection();
        connection.Execute("CREATE TABLE Entries (Id INTEGER PRIMARY KEY)");
        using (var transaction = connection.BeginTransaction()) {
            connection.Execute("INSERT INTO Entries (Id) VALUES (@Id)", new { Id = 1 }, transaction);
        }
        Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM Entries"));
    }

    [Fact]
    public async Task RuntimeScriptUsesInjectedDatabaseAndGeneratedDapperMappings()
    {
        var database = CreateDatabase();
        var (assembly, tags) = DynamicCompilation.CompileAssembly("""
            #pragma usings
            public sealed class DatabaseScript(DaemonDatabase Database) : IScript
            {
                public Task Execute(IScriptSession Session, CancellationToken CancellationToken)
                {
                    Database.Migrate("example.script", (connection, transaction) =>
                        connection.Execute("CREATE TABLE ScriptEntries (Id INTEGER PRIMARY KEY, Value TEXT NOT NULL)", transaction: transaction));
                    using var connection = Database.OpenConnection();
                    connection.Execute("INSERT INTO ScriptEntries (Id, Value) VALUES (@Id, @Value)", new { Id = 1, Value = "script value" });
                    var row = connection.QuerySingle<ScriptEntry>("SELECT Id, Value FROM ScriptEntries WHERE Id = @Id", new { Id = 1 });
                    if (row.Value != "script value")
                        throw new InvalidOperationException("Mapping failed");
                    return Task.CompletedTask;
                }
            }
            public sealed class ScriptEntry
            {
                public long Id { get; set; }
                public string Value { get; set; } = "";
            }
            """, "DatabaseScript");

        using var script = new DynamicScript<IScript>(assembly, tags, TimeSpan.FromMinutes(1), _ => { });
        Assert.Contains(script.ClassType.Assembly.GetTypes(), type => type.Namespace == "Dapper.AOT");
        using var services = new ServiceCollection().AddSingleton(database).BuildServiceProvider();
        var instance = (IScript)ActivatorUtilities.CreateInstance(services, script.ClassType);
        await instance.Execute(null!, TestContext.Current.CancellationToken);
        using var connection = database.OpenConnection();
        Assert.Equal("script value", connection.QuerySingle<string>("SELECT Value FROM ScriptEntries WHERE Id = 1"));
    }

    public void Dispose()
    {
        // Release this database's pooled handles before deleting its temporary files.
        if (!Directory.Exists(DirectoryPath))
            return;
        using var connection = CreateDatabase().OpenConnection();
        connection.Close();
        SqliteConnection.ClearPool(connection);
        Directory.Delete(DirectoryPath, recursive: true);
    }
}

