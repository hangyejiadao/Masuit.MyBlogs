using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Masuit.MyBlogs.Core.Infrastructure.Migrations;

/// <summary>
/// 为端口转发规则增加反向隧道模式与令牌摘要字段。
/// </summary>
public partial class AddReverseTunnelFields : Migration
{
	/// <summary>
	/// 增加模式、隧道编号和令牌摘要字段，旧规则保持普通直连模式。
	/// </summary>
	/// <param name="migrationBuilder">迁移构建器。</param>
	protected override void Up(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.AddColumn<int>(
			name: "Mode",
			table: "PortForwardRule",
			type: "integer",
			nullable: false,
			defaultValue: 0);

		migrationBuilder.AddColumn<string>(
			name: "TunnelId",
			table: "PortForwardRule",
			type: "character varying(64)",
			maxLength: 64,
			nullable: true);

		migrationBuilder.AddColumn<string>(
			name: "TunnelTokenHash",
			table: "PortForwardRule",
			type: "character varying(64)",
			maxLength: 64,
			nullable: true);

		migrationBuilder.CreateIndex(
			name: "IX_PortForwardRule_TunnelId",
			table: "PortForwardRule",
			column: "TunnelId",
			unique: true,
			filter: "\"TunnelId\" IS NOT NULL");
	}

	/// <summary>
	/// 回滚反向隧道字段和编号索引。
	/// </summary>
	/// <param name="migrationBuilder">迁移构建器。</param>
	protected override void Down(MigrationBuilder migrationBuilder)
	{
		migrationBuilder.DropIndex(
			name: "IX_PortForwardRule_TunnelId",
			table: "PortForwardRule");

		migrationBuilder.DropColumn(
			name: "Mode",
			table: "PortForwardRule");

		migrationBuilder.DropColumn(
			name: "TunnelId",
			table: "PortForwardRule");

		migrationBuilder.DropColumn(
			name: "TunnelTokenHash",
			table: "PortForwardRule");
	}
}
