using System;
using Dapper;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using Microsoft.Data.Sqlite;
using NetTools;
using RTSharp.Core.Services.Cache.ASCache.Migrations;
using RTSharp.Shared.Utils;
using SQLitePCL;

namespace RTSharp.Core.Services.Cache.ASCache
{
    public class ASCache
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
                    opt.Tags = new[] { nameof(ASCache) };
                });

            var runner = serviceCollection.BuildServiceProvider().GetRequiredService<IMigrationRunner>();
            runner.MigrateUp();
        }

        public async Task<CachedAS> GetCachedAS(IPAddress In)
        {
            await using var conn = await New();

			conn.CreateFunction(
				"ulong_gteq",
				(long l, long r) => {
                    unchecked {
                        ulong lul = (ulong)l;
                        ulong rul = (ulong)r;

                        return lul >= rul;
                    }
				}
			);

			conn.CreateFunction(
				"ulong_lteq",
				(long l, long r) => {
					unchecked {
						ulong lul = (ulong)l;
						ulong rul = (ulong)r;

						return lul <= rul;
					}
				}
			);

			var (high, low) = UInt128IPAddress.IPAddressToUInt128(In).ToHighLow();

			var d = await conn.QueryFirstOrDefaultAsync<CachedAS>("select Domain, Organization, Country, ImageHash from (select * from ASCache where ulong_lteq(IPLowStart,@IpLowPart) AND ulong_gteq(IPLowEnd,@IpLowPart)) where ulong_lteq(IPHighStart,@IpHighPart) AND ulong_gteq(IPHighEnd,@IpHighPart)", new {
                IpLowPart = low,
                IpHighPart = high
			});

            return d;
        }

        public async Task AddCachedAS(IPAddressRange Range, CachedAS In)
        {
            await using var conn = await New();

            var (ipHighStartUl, ipLowStartUl) = UInt128IPAddress.IPAddressToUInt128(Range.Begin).ToHighLow();
			var (ipHighEndUl, ipLowEndUl) = UInt128IPAddress.IPAddressToUInt128(Range.End).ToHighLow();

            unchecked {
                long ipHighStart = (long)ipHighStartUl;
                long ipLowStart = (long)ipLowStartUl;
                long ipHighEnd = (long)ipHighEndUl;
                long ipLowEnd = (long)ipLowEndUl;

				await conn.ExecuteAsync("insert into ASCache (IPLowStart, IPLowEnd, IPHighStart, IPHighEnd, Domain, Organization, Country, ImageHash) values (@IPLowStart, @IPLowEnd, @IPHighStart, @IPHighEnd, @Domain, @Organization, @Country, @ImageHash)", new {
					IPLowStart = ipLowStart,
					IPLowEnd = ipLowEnd,
					IPHighStart = ipHighStart,
					IPHighEnd = ipHighEnd,
					Domain = In.Domain,
					Organization = In.Organization,
	                Country = In.Country,
					ImageHash = In.ImageHash 
				});
			}
		}
    }
}
