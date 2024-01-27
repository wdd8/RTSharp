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

        public async Task<IList<CachedTorrentPath>> GetCachedFileEntries(byte[] TorrentHash)
        {
            await using var conn = await New();

            var d = await conn.QueryAsync<CachedTorrentPath>("select Path, Size from FileCache where TorrentHash = @TorrentHash order by OrderId", new
            {
                TorrentHash
            });

            return d.ToList();
        }

        public async Task AddCachedFileEntries(byte[] TorrentHash, IEnumerable<Shared.Abstractions.File> In)
        {
            await using var conn = await New();

            foreach (var block in In.Select((x, idx) => (idx, x)).Chunk(500))
            {
                await conn.ExecuteAsync("insert into FileCache (TorrentHash, OrderId, Path, Size) values (@TorrentHash, @OrderId, @Path, @Size)", block.Select(x => new
                {
                    OrderId = x.idx,
                    TorrentHash,
                    x.x.Path,
                    x.x.Size
                }));
            }
        }
    }
}
