using System.Collections.Generic;
using Dapper;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using Microsoft.Data.Sqlite;
using RTSharp.Core.Services.Cache.TorrentPropertiesCache.Migrations;

namespace RTSharp.Core.Services.Cache.TorrentPropertiesCache
{
    public class TorrentPropertiesCache
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
                    opt.Tags = new[] { nameof(TorrentPropertiesCache) };
                });

            var runner = serviceCollection.BuildServiceProvider().GetRequiredService<IMigrationRunner>();
            runner.MigrateUp();
        }

        public async Task<CachedTorrentProperties?> GetCachedTorrentProperties(byte[] TorrentHash)
        {
            await using var conn = await New();

            var d = await conn.QueryFirstOrDefaultAsync<CachedTorrentProperties>("select * from TorrentPropertiesCache where TorrentHash = @TorrentHash", new
            {
                TorrentHash
            });

            return d;
        }

        public async Task AddCachedTorrentProperties(byte[] TorrentHash, bool IsMultiFile)
        {
            await using var conn = await New();

            await conn.ExecuteAsync("insert into TorrentPropertiesCache (TorrentHash, IsMultiFile) values (@TorrentHash, @IsMultiFile)", new
            {
                TorrentHash,
				IsMultiFile
			});
        }
    }
}
