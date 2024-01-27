using FluentMigrator;

namespace RTSharp.Core.Services.Cache.ASCache.Migrations
{
    [Migration(1, "Initial create")]
    [Tags(nameof(ASCache))]
    public class Initial : Migration
    {
        public override void Up()
        {
            Create.Table("ASCache")
                .WithColumn("IPLowStart").AsInt64().Indexed()
                .WithColumn("IPHighStart").AsInt64().Indexed()
                .WithColumn("IPLowEnd").AsInt64().Indexed()
                .WithColumn("IPHighEnd").AsInt64().Indexed()
                .WithColumn("Domain").AsString().Nullable()
                .WithColumn("Organization").AsString().Nullable()
				.WithColumn("Country").AsFixedLengthAnsiString(2).Nullable()
				.WithColumn("ImageHash").AsBinary().Nullable();
        }

        public override void Down()
        {
            Delete.Table("ASCache");
        }
    }
}
