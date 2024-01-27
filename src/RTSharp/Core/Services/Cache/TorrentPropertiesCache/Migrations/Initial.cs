using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentMigrator;
using FluentMigrator.Infrastructure;

namespace RTSharp.Core.Services.Cache.TorrentPropertiesCache.Migrations
{
    [Migration(1, "Initial create")]
    [Tags(nameof(TorrentPropertiesCache))]
    public class Initial : Migration
    {
        public override void Up()
        {
            Create.Table("TorrentPropertiesCache")
                .WithColumn("TorrentHash").AsBinary().Indexed()
                .WithColumn("IsMultiFile").AsBoolean();
        }

        public override void Down()
        {
            Delete.Table("TorrentPropertiesCache");
        }
    }
}
