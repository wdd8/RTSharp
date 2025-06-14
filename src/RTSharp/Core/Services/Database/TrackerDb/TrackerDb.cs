using System;
using Dapper;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using Microsoft.Data.Sqlite;
using NetTools;
using RTSharp.Core.Services.Cache.TrackerDb.Migrations;
using RTSharp.Shared.Utils;
using SQLitePCL;
using System.Collections.Generic;

namespace RTSharp.Core.Services.Cache.TrackerDb
{
    public class TrackerDb
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
                    opt.Tags = new[] { nameof(TrackerDb) };
                });

            var runner = serviceCollection.BuildServiceProvider().GetRequiredService<IMigrationRunner>();
            runner.MigrateUp();
        }

        public async Task<TrackerInfo?> GetTrackerInfo(string Domain)
        {
            await using var conn = await New();

            var d = await conn.QueryFirstOrDefaultAsync<TrackerInfo>("select Name, ImageHash from TrackerDb where Domain = @Domain", new {
                Domain = Domain
            });

            return d;
        }

        public async Task<IEnumerable<TrackerInfo>> GetTrackerInfo(IEnumerable<string> Domain)
        {
            await using var conn = await New();

            var d = await conn.QueryAsync<TrackerInfo>("select Domain, Name, ImageHash from TrackerDb where Domain IN @Domain", new {
                Domain = Domain
            });

            return d;
        }

        public async Task AddOrUpdateTrackerInfo(string Domain, TrackerInfo Info)
        {
            await using var conn = await New();

            await conn.ExecuteAsync("insert into TrackerDb (Domain, Name, ImageHash) values (@Domain, @Name, @ImageHash) on conflict(Domain) do update set Name = @Name, ImageHash = @ImageHash", new {
                Domain = Domain,
                Name = Info.Name,
                ImageHash = Info.ImageHash 
            });
        }
    }
}
