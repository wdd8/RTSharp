using FluentMigrator;

namespace RTSharp.Core.Services.Cache.TrackerDb.Migrations
{
    [Migration(1, "Initial create")]
    [Tags(nameof(TrackerDb))]
    public class Initial : Migration
    {
        public override void Up()
        {
            Create.Table("TrackerDb")
                .WithColumn("Domain").AsString().PrimaryKey()
                .WithColumn("Name").AsString().Nullable()
                .WithColumn("ImageHash").AsBinary().Nullable();
        }

        public override void Down()
        {
            Delete.Table("TrackerDb");
        }
    }
}
