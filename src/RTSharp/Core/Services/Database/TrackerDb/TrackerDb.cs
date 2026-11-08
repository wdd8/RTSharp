using Dapper;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using Microsoft.Data.Sqlite;
using RTSharp.Core.Services.Cache.TrackerDb.Migrations;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace RTSharp.Core.Services.Database.TrackerDb
{
    public class TrackerDb
    {
        private readonly ConcurrentDictionary<string, TrackerInfo> Cache = new();

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
                    opt.Tags = new[] { nameof(TrackerDb) };
                });

            var runner = serviceCollection.BuildServiceProvider().GetRequiredService<IMigrationRunner>();
            runner.MigrateUp();

            foreach (var info in await conn.QueryAsync<TrackerInfo>("select Domain, Name, ImageHash from TrackerDb"))
                Cache[info.Domain] = info;
        }

        public TrackerInfo? GetTrackerInfo(string Domain) => Cache.GetValueOrDefault(Domain);

        public IEnumerable<TrackerInfo> GetTrackerInfo(IEnumerable<string> Domain) => Domain.Distinct().Select(Cache.GetValueOrDefault).OfType<TrackerInfo>();

        public async Task ClearImageHashes()
        {
            await using var conn = await New();
            await conn.ExecuteAsync("UPDATE TrackerDb SET ImageHash = NULL");

            foreach (var (domain, info) in Cache)
                Cache[domain] = new TrackerInfo(domain, info.Name, null);
        }

        // https://github.com/DapperLib/DapperAOT/issues/226
        [DapperAot(false)]
        public async Task AddOrUpdateTrackerInfo(string Domain, TrackerInfo Info)
        {
            await using var conn = await New();

            await conn.ExecuteAsync("insert into TrackerDb (Domain, Name, ImageHash) values (@Domain, @Name, @ImageHash) on conflict(Domain) do update set Name = @Name, ImageHash = @ImageHash", new {
                Domain = Domain,
                Name = Info.Name,
                ImageHash = Info.ImageHash
            });

            Cache[Domain] = new TrackerInfo(Domain, Info.Name, Info.ImageHash);
        }
    }
}
