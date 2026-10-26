using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Masuit.MyBlogs.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppPricePlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PaidPrice",
                table: "AppLicense",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PricePlanId",
                table: "AppLicense",
                type: "integer",
                nullable: true);

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

            migrationBuilder.CreateTable(
                name: "AppPricePlan",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApplicationInfoId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    ValidityDays = table.Column<int>(type: "integer", nullable: false),
                    Sort = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppPricePlan", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppPricePlan_ApplicationInfo_ApplicationInfoId",
                        column: x => x.ApplicationInfoId,
                        principalTable: "ApplicationInfo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppLicense_PricePlanId",
                table: "AppLicense",
                column: "PricePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_AppPricePlan_ApplicationInfoId",
                table: "AppPricePlan",
                column: "ApplicationInfoId");

            migrationBuilder.AddForeignKey(
                name: "FK_AppLicense_AppPricePlan_PricePlanId",
                table: "AppLicense",
                column: "PricePlanId",
                principalTable: "AppPricePlan",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppLicense_AppPricePlan_PricePlanId",
                table: "AppLicense");

            migrationBuilder.DropTable(
                name: "AppPricePlan");

            migrationBuilder.DropIndex(
                name: "IX_AppLicense_PricePlanId",
                table: "AppLicense");

            migrationBuilder.DropColumn(
                name: "PaidPrice",
                table: "AppLicense");

            migrationBuilder.DropColumn(
                name: "PricePlanId",
                table: "AppLicense");

            migrationBuilder.DropColumn(
                name: "PackageUrl",
                table: "ApplicationInfo");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "ApplicationInfo");
        }
    }
}
