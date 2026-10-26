using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Masuit.MyBlogs.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPortForwardPath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Path",
                table: "PortForwardRule",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Path",
                table: "PortForwardRule");
        }
    }
}
