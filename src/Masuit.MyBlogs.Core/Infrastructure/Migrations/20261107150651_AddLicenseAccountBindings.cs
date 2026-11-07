using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Masuit.MyBlogs.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLicenseAccountBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LicenseAccountBinding",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AppLicenseId = table.Column<int>(type: "integer", nullable: false),
                    Account = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseAccountBinding", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicenseAccountBinding_AppLicense_AppLicenseId",
                        column: x => x.AppLicenseId,
                        principalTable: "AppLicense",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LicenseAccountBinding_Account",
                table: "LicenseAccountBinding",
                column: "Account");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseAccountBinding_AppLicenseId_Account",
                table: "LicenseAccountBinding",
                columns: new[] { "AppLicenseId", "Account" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LicenseAccountBinding");
        }
    }
}
