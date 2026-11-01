using Dapper;

using Microsoft.Data.Sqlite;

namespace RTSharp.Daemon.Services;

public sealed class DaemonDatabase
{
    private readonly string ConnectionString;

    public string DatabasePath { get; }

    public DaemonDatabase(string DatabasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DatabasePath);
        if (!Path.IsPathFullyQualified(DatabasePath))
            throw new ArgumentException("An absolute database file path is required.", nameof(DatabasePath));

        this.DatabasePath = Path.GetFullPath(DatabasePath);
        ConnectionString = new SqliteConnectionStringBuilder {
            DataSource = this.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            ForeignKeys = true,
            DefaultTimeout = 30,
            Pooling = true
        }.ToString();

        Directory.CreateDirectory(Path.GetDirectoryName(this.DatabasePath)!);
        Initialize();
    }

    private void Initialize()
    {
        using var connection = OpenConnection();
        var journalMode = connection.QuerySingle<string>("PRAGMA journal_mode = WAL");
        if (!string.Equals(journalMode, "WAL", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Could not enable WAL for {this.DatabasePath}.");

        connection.Execute("""
            CREATE TABLE IF NOT EXISTS __RTSharpSchemaVersions (
                Owner TEXT NOT NULL PRIMARY KEY,
                Version INTEGER NOT NULL CHECK (Version >= 0)
            )
            """);
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        try {
            connection.Open();
            return connection;
        } catch {
            connection.Dispose();
            throw;
        }
    }

    public void Migrate(string Owner, params Action<SqliteConnection, SqliteTransaction>[] Migrations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Owner);
        ArgumentNullException.ThrowIfNull(Migrations);
        foreach (var migration in Migrations)
            ArgumentNullException.ThrowIfNull(migration);

        using var connection = OpenConnection();

        using var transaction = connection.BeginTransaction(deferred: false);
        var version = connection.QuerySingleOrDefault<int>(
            "SELECT Version FROM __RTSharpSchemaVersions WHERE Owner = @Owner", new { Owner }, transaction);
        if (version > Migrations.Length)
            throw new InvalidOperationException($"Schema for '{Owner}' is version {version}, but only {Migrations.Length} migrations were supplied.");

        for (var i = version; i < Migrations.Length; i++)
            Migrations[i](connection, transaction);

        if (version != Migrations.Length) {
            connection.Execute("""
                INSERT INTO __RTSharpSchemaVersions (Owner, Version) VALUES (@Owner, @Version)
                ON CONFLICT (Owner) DO UPDATE SET Version = excluded.Version
                """, new { Owner, Version = Migrations.Length }, transaction);
        }
        transaction.Commit();
    }
}
