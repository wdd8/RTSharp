using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentMigrator;
using FluentMigrator.Infrastructure;

namespace RTSharp.Core.Services.Cache.TorrentFileCache.Migrations
{
    [Migration(1, "Initial create")]
    [Tags(nameof(TorrentFileCache))]
    public class Initial : Migration
    {
        public override void Up()
        {
            Create.Table("FileCache")
                .WithColumn("TorrentHash").AsBinary().Indexed()
                .WithColumn("OrderId").AsInt64()
                .WithColumn("Path").AsString()
                .WithColumn("Size").AsInt64();
        }

        public override void Down()
        {
            Delete.Table("FileCache");
        }
    }
}
