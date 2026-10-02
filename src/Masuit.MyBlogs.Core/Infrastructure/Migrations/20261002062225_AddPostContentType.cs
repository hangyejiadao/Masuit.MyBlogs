using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Masuit.MyBlogs.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPostContentType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContentType",
                table: "Post",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "Post");
        }
    }
}
