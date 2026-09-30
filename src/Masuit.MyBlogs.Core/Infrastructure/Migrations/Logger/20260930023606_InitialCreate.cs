using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Masuit.MyBlogs.Core.Infrastructure.Migrations.Logger
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PerformanceCounter",
                columns: table => new
                {
                    ServerIP = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Time = table.Column<long>(type: "bigint", nullable: false),
                    CpuLoad = table.Column<float>(type: "real", nullable: false),
                    ProcessCpuLoad = table.Column<float>(type: "real", nullable: false),
                    MemoryUsage = table.Column<float>(type: "real", nullable: false),
                    ProcessMemoryUsage = table.Column<float>(type: "real", nullable: false),
                    DiskRead = table.Column<float>(type: "real", nullable: false),
                    DiskWrite = table.Column<float>(type: "real", nullable: false),
                    Upload = table.Column<float>(type: "real", nullable: false),
                    Download = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerformanceCounter", x => new { x.ServerIP, x.Time });
                });

            migrationBuilder.CreateTable(
                name: "RequestLogDetail",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Time = table.Column<DateTime>(type: "timestamp", nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    RequestUrl = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    IP = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Location = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Country = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    City = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Network = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    TraceId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestLogDetail", x => new { x.Id, x.Time });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PerformanceCounter");

            migrationBuilder.DropTable(
                name: "RequestLogDetail");
        }
    }
}
