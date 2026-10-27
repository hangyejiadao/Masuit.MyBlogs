using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Masuit.MyBlogs.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAppOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppOrder",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrderNo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UserInfoId = table.Column<int>(type: "integer", nullable: false),
                    ApplicationInfoId = table.Column<int>(type: "integer", nullable: false),
                    PricePlanId = table.Column<int>(type: "integer", nullable: false),
                    PlanName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlanPrice = table.Column<decimal>(type: "numeric", nullable: false),
                    PlanValidityDays = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    OrderStatus = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    AlipayTradeNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IssuedLicenseId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppOrder", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppOrder_AppLicense_IssuedLicenseId",
                        column: x => x.IssuedLicenseId,
                        principalTable: "AppLicense",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AppOrder_AppPricePlan_PricePlanId",
                        column: x => x.PricePlanId,
                        principalTable: "AppPricePlan",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppOrder_ApplicationInfo_ApplicationInfoId",
                        column: x => x.ApplicationInfoId,
                        principalTable: "ApplicationInfo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppOrder_UserInfo_UserInfoId",
                        column: x => x.UserInfoId,
                        principalTable: "UserInfo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppOrder_ApplicationInfoId",
                table: "AppOrder",
                column: "ApplicationInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_AppOrder_CreatedAt",
                table: "AppOrder",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_AppOrder_IssuedLicenseId",
                table: "AppOrder",
                column: "IssuedLicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_AppOrder_OrderNo",
                table: "AppOrder",
                column: "OrderNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppOrder_PricePlanId",
                table: "AppOrder",
                column: "PricePlanId");

            migrationBuilder.CreateIndex(
                name: "IX_AppOrder_UserInfoId",
                table: "AppOrder",
                column: "UserInfoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppOrder");
        }
    }
}
