using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Masuit.MyBlogs.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppVersion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApplicationInfoId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PackageUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    ChangeLog = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Sort = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppVersion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppVersion_ApplicationInfo_ApplicationInfoId",
                        column: x => x.ApplicationInfoId,
                        principalTable: "ApplicationInfo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppVersion_ApplicationInfoId_Version",
                table: "AppVersion",
                columns: new[] { "ApplicationInfoId", "Version" },
                unique: true);

            // 数据搬运：把 ApplicationInfo 上原有的单个版本搬成该应用的第一条版本记录。
            // 旧 Version 为空时用 '1.0.0' 占位；旧 PackageUrl 为空时存空串（版本日志旧数据没有，留空）；Status=1 表示可用。
            migrationBuilder.Sql(
                """
                INSERT INTO "AppVersion" ("ApplicationInfoId", "Version", "PackageUrl", "ChangeLog", "Sort", "Status")
                SELECT "Id",
                       COALESCE(NULLIF(BTRIM("Version"), ''), '1.0.0'),
                       COALESCE(NULLIF(BTRIM("PackageUrl"), ''), ''),
                       NULL,
                       0,
                       1
                FROM "ApplicationInfo"
                WHERE ("Version" IS NOT NULL AND BTRIM("Version") <> '')
                   OR ("PackageUrl" IS NOT NULL AND BTRIM("PackageUrl") <> '');
                """);

            migrationBuilder.DropColumn(
                name: "PackageUrl",
                table: "ApplicationInfo");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "ApplicationInfo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PackageUrl",
                table: "ApplicationInfo",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Version",
                table: "ApplicationInfo",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // 回滚时把每个应用的最新一条版本（按列表排序：Sort 升序、Id 降序取第一条）写回应用的旧字段，避免数据丢失。
            migrationBuilder.Sql(
                """
                UPDATE "ApplicationInfo" a
                SET "Version" = v."Version", "PackageUrl" = v."PackageUrl"
                FROM (
                    SELECT DISTINCT ON ("ApplicationInfoId") "ApplicationInfoId", "Version", "PackageUrl"
                    FROM "AppVersion"
                    ORDER BY "ApplicationInfoId", "Sort" ASC, "Id" DESC
                ) v
                WHERE a."Id" = v."ApplicationInfoId";
                """);

            migrationBuilder.DropTable(
                name: "AppVersion");
        }
    }
}
