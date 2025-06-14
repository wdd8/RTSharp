using System.Collections.Generic;
using Dapper;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using System;
using System.IO;
using System.Security.Cryptography;
using Avalonia.Media.Imaging;
using RTSharp.Shared.Utils;
using RTSharp.Core.Services.Cache.Images.Migrations;
using Microsoft.Data.Sqlite;
using Avalonia;

namespace RTSharp.Core.Services.Cache.Images
{
    public class ImageCache
    {
        private readonly Config Config;

        public ImageCache(Config Config)
        {
            this.Config = Config;
            if (Images == null)
            {
                Images = new(Config.Caching.Value.InMemoryImages, new ByteArrayComparer());
            }
        }

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
                    opt.Tags = new[] { nameof(ImageCache) };
                });

            var runner = serviceCollection.BuildServiceProvider().GetRequiredService<IMigrationRunner>();
            runner.MigrateUp();
        }

        private static Dictionary<byte[], WeakReference<Bitmap>> Images;

        private Bitmap CacheInMemory(byte[] ImageHash, Bitmap Image)
        {
            if (Images.TryGetValue(ImageHash, out var weakRef))
            {
                if (weakRef.TryGetTarget(out var cached))
                {
                    return cached;
                }
            }

            if (weakRef != null)
            {
                weakRef.SetTarget(Image);
                return Image;
            }

            if (Images.Count > Config.Caching.Value.InMemoryImages)
            {
                var toRemove = new List<byte[]>();
                foreach (var img in Images)
                {
                    if (!img.Value.TryGetTarget(out var _))
                    {
                        toRemove.Add(img.Key);
                    }

                    if (Images.Count - toRemove.Count <= Config.Caching.Value.InMemoryImages)
                    {
                        break;
                    }
                }

                foreach (var item in toRemove)
                {
                    Images.Remove(item);
                }

                while (Images.Count == 0 || Images.Count > Config.Caching.Value.InMemoryImages)
                {
                    Images.Remove(Images.First().Key);
                }
            }

            Images[ImageHash] = new WeakReference<Bitmap>(Image);

            return Image;
        }

        public async ValueTask<Bitmap?> GetCachedImage(byte[] ImageHash)
        {
            if (Images.TryGetValue(ImageHash, out var weakRef)) {
                if (weakRef.TryGetTarget(out var bitmap)) {
                    return bitmap;
                }
            }

            await using var conn = await New();

            var d = await conn.QueryFirstOrDefaultAsync<CachedImage>("select Image from Images where ImageHash = @Hash", new
            {
                Hash = ImageHash
            });

            if (d == default)
                return null;

            using var mem = new MemoryStream();
            mem.Write(d.Image);
            mem.Position = 0;
            var image = new Bitmap(mem);

            return CacheInMemory(ImageHash, image);
        }

        public async Task<(byte[] Hash, Bitmap Image)?> AddImage(Stream Image)
        {
            await using var conn = await New();

            Bitmap scaled;
            try {
                using var bitmap = new Bitmap(Image);
                scaled = bitmap.CreateScaledBitmap(new PixelSize(64, 64));
            } catch {
                return null;
            }

            var mem = new MemoryStream();
            scaled.Save(mem);
            mem.Position = 0;

            var sha256 = SHA256.HashData(mem);
            mem.Position = 0;
            var bytes = mem.ToArray();

            Bitmap? alreadyCached;
            if ((alreadyCached = await GetCachedImage(sha256)) != null) {
                return (sha256, alreadyCached);
            }

            await conn.ExecuteAsync("insert into Images (ImageHash, Image) values (@ImageHash, @Image)", new CachedImage(ImageHash: sha256, Image: bytes));

            return (sha256, CacheInMemory(sha256, scaled));
        }
    }
}
