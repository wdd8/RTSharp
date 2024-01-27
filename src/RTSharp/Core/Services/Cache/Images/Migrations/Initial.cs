using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentMigrator;
using FluentMigrator.Infrastructure;

namespace RTSharp.Core.Services.Cache.Images.Migrations
{
    [Migration(1, "Initial create")]
    [Tags(nameof(ImageCache))]
    public class Initial : Migration
    {
        public override void Up()
        {
            Create.Table("Images")
                .WithColumn("ImageHash").AsBinary().Indexed()
                .WithColumn("Image").AsBinary();
        }

        public override void Down()
        {
            Delete.Table("Images");
        }
    }
}
