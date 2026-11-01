using System.Collections.Generic;
using Dapper;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using RTSharp.Core.Services.Cache.TorrentFileCache.Migrations;
using Microsoft.Data.Sqlite;

namespace RTSharp.Core.Services.Cache.TorrentFileCache
{
    public class TorrentFileCache
    {
        private static async Task<SqliteConnection> New()
        {
            var conn = new SqliteConnection(Consts.ConnectionString);
            await conn.OpenAsync();

            return conn;
        }

        public async Task Initialize()
        {
            await using var conn = await New();

            await conn.QueryAsync<string>("SELECT SQLITE_VERSION();");

            var serviceCollection = new ServiceCollection()
                .AddFluentMigratorCore()
                .ConfigureRunner(
                    builder => builder
                        .AddSQLite()
                        .WithGlobalConnectionString(Consts.ConnectionString)
                        .ScanIn(typeof(Initial).Assembly).For.Migrations())
                .Configure<RunnerOptions>(opt =>
                {
                    opt.Tags = new[] { nameof(TorrentFileCache) };
                });

            var runner = serviceCollection.BuildServiceProvider().GetRequiredService<IMigrationRunner>();
            runner.MigrateUp();
        }

        // https://github.com/DapperLib/DapperAOT/issues/226
        [DapperAot(false)]
        public async Task<IList<CachedTorrentPath>> GetCachedFileEntries(byte[] TorrentHash)
        {
            await using var conn = await New();

            var d = await conn.QueryAsync<CachedTorrentPath>("select Path, Size from FileCache where TorrentHash = @TorrentHash order by OrderId", new
            {
                TorrentHash
            });

            return d.ToList();
        }

        public async Task AddCachedFileEntries(byte[] TorrentHash, IList<Shared.Abstractions.File> In)
        {
            await using var conn = await New();
            await using var txn = (SqliteTransaction)await conn.BeginTransactionAsync();

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = "insert into FileCache (TorrentHash, OrderId, Path, Size) values (@TorrentHash, @OrderId, @Path, @Size)";

            var pHash = cmd.Parameters.Add("@TorrentHash", SqliteType.Blob);
            var pOrderId = cmd.Parameters.Add("@OrderId", SqliteType.Integer);
            var pPath = cmd.Parameters.Add("@Path", SqliteType.Text);
            var pSize = cmd.Parameters.Add("@Size", SqliteType.Integer);

            pHash.Value = TorrentHash;

            for (var x = 0; x < In.Count; x++) {
                var file = In[x];
                pOrderId.Value = x;
                pPath.Value = file.Path;
                pSize.Value = file.Size;
                await cmd.ExecuteNonQueryAsync();
            }

            await txn.CommitAsync();
        }

        public async Task<(long Count, long SizeBytes)> GetStats()
        {
            await using var conn = await New();
            var count = await conn.QuerySingleAsync<long>("SELECT COUNT(DISTINCT TorrentHash) FROM FileCache");
            var builder = new SqliteConnectionStringBuilder(Consts.ConnectionString);
            var info = new System.IO.FileInfo(builder.DataSource);
            return (count, info.Exists ? info.Length : 0L);
        }

        public async Task Clear()
        {
            await using var conn = await New();
            await conn.ExecuteAsync("DELETE FROM FileCache");
            await conn.ExecuteAsync("VACUUM");
        }
    }
}
