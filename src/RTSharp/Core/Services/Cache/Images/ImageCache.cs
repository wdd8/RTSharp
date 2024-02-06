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

        private Bitmap CacheInMemory(byte[] ImageHash, byte[] Image)
        {
            Bitmap? bitmap;
            if (Images.TryGetValue(ImageHash, out var weakRef))
            {
                if (weakRef.TryGetTarget(out bitmap))
                {
                    return bitmap;
                }
            }

            using var mem = new MemoryStream(Image);
            bitmap = new Bitmap(mem);

            if (weakRef != null)
            {
                weakRef.SetTarget(bitmap);
                return bitmap;
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

            Images[ImageHash] = new WeakReference<Bitmap>(bitmap);

            return bitmap;
        }

        public async ValueTask<Bitmap?> GetCachedImage(byte[] ImageHash)
        {
            Bitmap? bitmap;
            if (Images.TryGetValue(ImageHash, out var weakRef))
            {
                if (weakRef.TryGetTarget(out bitmap))
                {
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

            return CacheInMemory(ImageHash, d.Image);
        }

        public Task<(byte[] Hash, Bitmap Image)> AddImage(Stream Image)
        {
            if (Image is MemoryStream memStream) {
                return AddImage(memStream.ToArray());
            }

            using var memoryStream = new MemoryStream();
            Image.CopyTo(memoryStream);
            return AddImage(memoryStream.ToArray());
        }


        public async Task<(byte[] Hash, Bitmap Image)> AddImage(byte[] Image)
        {
            await using var conn = await New();

            var sha256 = SHA256.HashData(Image);

            Bitmap? alreadyCached;
            if ((alreadyCached = await GetCachedImage(sha256)) != null) {
                return (sha256, alreadyCached);
            }

            await conn.ExecuteAsync("insert into Images (ImageHash, Image) values (@ImageHash, @Image)", new
            {
                ImageHash = sha256,
                Image
            });

            return (sha256, CacheInMemory(sha256, Image));
        }
    }
}
