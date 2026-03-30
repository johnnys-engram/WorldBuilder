using FluentMigrator;

namespace WorldBuilder.Shared.Migrations;

[Migration(6, "Add ProjectDocuments table for portal table overlays and similar blobs")]
public class Migration_006_ProjectDocuments : Migration {
    public override void Up() {
        if (!Schema.Table("ProjectDocuments").Exists()) {
            Create.Table("ProjectDocuments")
                .WithColumn("Id").AsString().PrimaryKey()
                .WithColumn("Data").AsBinary(int.MaxValue).NotNullable()
                .WithColumn("Version").AsInt64().NotNullable();
        }
    }

    public override void Down() {
        if (Schema.Table("ProjectDocuments").Exists()) {
            Delete.Table("ProjectDocuments");
        }
    }
}
