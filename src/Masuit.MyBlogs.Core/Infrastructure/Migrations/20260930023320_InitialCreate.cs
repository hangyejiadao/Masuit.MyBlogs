using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Masuit.MyBlogs.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Advertisement",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ImageUrl = table.Column<string>(type: "text", nullable: true),
                    ThumbImgUrl = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    CreateTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdateTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: false),
                    Types = table.Column<string>(type: "text", nullable: false),
                    DisplayCount = table.Column<int>(type: "integer", nullable: false),
                    CategoryIds = table.Column<string>(type: "text", nullable: true),
                    ExpireTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    RegionMode = table.Column<int>(type: "integer", nullable: false),
                    Regions = table.Column<string>(type: "text", nullable: true),
                    Merchant = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Advertisement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Category",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    Path = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Category", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Category_Category_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Category",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Donate",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NickName = table.Column<string>(type: "text", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: true),
                    QQorWechat = table.Column<string>(type: "text", nullable: true),
                    Amount = table.Column<string>(type: "text", nullable: true),
                    Via = table.Column<string>(type: "text", nullable: true),
                    DonateTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Donate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailBlocklist",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailBlocklist", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FastShare",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Link = table.Column<string>(type: "text", nullable: true),
                    Sort = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FastShare", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InternalMessage",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Content = table.Column<string>(type: "text", nullable: true),
                    Link = table.Column<string>(type: "text", nullable: true),
                    Time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Read = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InternalMessage", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IpInterceptLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IP = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestUrl = table.Column<string>(type: "text", nullable: true),
                    Referer = table.Column<string>(type: "text", nullable: true),
                    Address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true),
                    Time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Remark = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    HttpVersion = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Headers = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpInterceptLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IpReportLogs",
                columns: table => new
                {
                    IP = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IpReportLogs", x => x.IP);
                });

            migrationBuilder.CreateTable(
                name: "LeaveMessage",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NickName = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    PostDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: true),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    Browser = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OperatingSystem = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IsMaster = table.Column<bool>(type: "boolean", nullable: false),
                    IP = table.Column<string>(type: "text", nullable: true),
                    Location = table.Column<string>(type: "text", nullable: true),
                    GroupTag = table.Column<string>(type: "text", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveMessage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveMessage_LeaveMessage_ParentId",
                        column: x => x.ParentId,
                        principalTable: "LeaveMessage",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Links",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Url = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UrlBase = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Except = table.Column<bool>(type: "boolean", nullable: false),
                    Recommend = table.Column<bool>(type: "boolean", nullable: false),
                    UpdateTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Links", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Menu",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: true),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Sort = table.Column<int>(type: "integer", nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    MenuType = table.Column<int>(type: "integer", nullable: false),
                    NewTab = table.Column<bool>(type: "boolean", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Menu", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Menu_Menu_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Menu",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Misc",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    PostDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifyDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Misc", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notice",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    PostDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifyDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ViewCount = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    EndTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    NoticeStatus = table.Column<int>(type: "integer", nullable: false),
                    StrongAlert = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notice", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PostTag",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: true),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostTag", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Keywords = table.Column<string>(type: "text", nullable: false),
                    ResultCount = table.Column<int>(type: "integer", nullable: false),
                    SearchTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IP = table.Column<string>(type: "text", nullable: true),
                    Elapsed = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchDetails", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Seminar",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false),
                    SubTitle = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Seminar", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemSetting",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSetting", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserInfo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "text", nullable: false),
                    NickName = table.Column<string>(type: "text", nullable: false),
                    Password = table.Column<string>(type: "text", nullable: false),
                    SaltKey = table.Column<string>(type: "text", nullable: false),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: true),
                    QQorWechat = table.Column<string>(type: "text", nullable: true),
                    Avatar = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserInfo", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Variables",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Variables", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisementClickRecord",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AdvertisementId = table.Column<int>(type: "integer", nullable: false),
                    IP = table.Column<string>(type: "text", nullable: true),
                    Location = table.Column<string>(type: "text", nullable: true),
                    Referer = table.Column<string>(type: "text", nullable: true),
                    Time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisementClickRecord", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdvertisementClickRecord_Advertisement_AdvertisementId",
                        column: x => x.AdvertisementId,
                        principalTable: "Advertisement",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Post",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Author = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ProtectContent = table.Column<string>(type: "text", nullable: true),
                    ProtectContentMode = table.Column<int>(type: "integer", nullable: false),
                    PostDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ModifyDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsFixedTop = table.Column<bool>(type: "boolean", nullable: false),
                    CategoryId = table.Column<int>(type: "integer", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Modifier = table.Column<string>(type: "text", nullable: true),
                    ModifierEmail = table.Column<string>(type: "text", nullable: true),
                    Label = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Keyword = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    VoteUpCount = table.Column<int>(type: "integer", nullable: false),
                    VoteDownCount = table.Column<int>(type: "integer", nullable: false),
                    AverageViewCount = table.Column<double>(type: "double precision", nullable: false),
                    TotalViewCount = table.Column<int>(type: "integer", nullable: false),
                    IP = table.Column<string>(type: "text", nullable: true),
                    DisableComment = table.Column<bool>(type: "boolean", nullable: false),
                    DisableCopy = table.Column<bool>(type: "boolean", nullable: false),
                    LimitMode = table.Column<int>(type: "integer", nullable: true),
                    Regions = table.Column<string>(type: "text", nullable: true),
                    ExceptRegions = table.Column<string>(type: "text", nullable: true),
                    ProtectContentLimitMode = table.Column<int>(type: "integer", nullable: true),
                    ProtectContentRegions = table.Column<string>(type: "text", nullable: true),
                    ProtectPassword = table.Column<string>(type: "text", nullable: true),
                    Rss = table.Column<bool>(type: "boolean", nullable: false),
                    Locked = table.Column<bool>(type: "boolean", nullable: false),
                    Redirect = table.Column<string>(type: "text", nullable: true),
                    ExpireAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsNsfw = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Post", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Post_Category_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Category",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LinkLoopback",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Referer = table.Column<string>(type: "text", nullable: true),
                    IP = table.Column<string>(type: "text", nullable: true),
                    Time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LinkId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkLoopback", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LinkLoopback_Links_LinkId",
                        column: x => x.LinkId,
                        principalTable: "Links",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LoginRecord",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IP = table.Column<string>(type: "text", nullable: true),
                    LoginTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PhysicAddress = table.Column<string>(type: "text", nullable: true),
                    LoginType = table.Column<int>(type: "integer", nullable: false),
                    UserInfoId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginRecord", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoginRecord_UserInfo_UserInfoId",
                        column: x => x.UserInfoId,
                        principalTable: "UserInfo",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Comment",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NickName = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Email = table.Column<string>(type: "text", nullable: true),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    PostId = table.Column<int>(type: "integer", nullable: false),
                    CommentDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Browser = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OperatingSystem = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IsMaster = table.Column<bool>(type: "boolean", nullable: false),
                    VoteCount = table.Column<int>(type: "integer", nullable: false),
                    AgainstCount = table.Column<int>(type: "integer", nullable: false),
                    IP = table.Column<string>(type: "text", nullable: true),
                    Location = table.Column<string>(type: "text", nullable: true),
                    GroupTag = table.Column<string>(type: "text", nullable: false),
                    Path = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comment_Comment_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Comment",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Comment_Post_PostId",
                        column: x => x.PostId,
                        principalTable: "Post",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostHistoryVersion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ProtectContent = table.Column<string>(type: "text", nullable: true),
                    ViewCount = table.Column<int>(type: "integer", nullable: false),
                    ModifyDate = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CategoryId = table.Column<int>(type: "integer", nullable: false),
                    PostId = table.Column<int>(type: "integer", nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Modifier = table.Column<string>(type: "text", nullable: true),
                    ModifierEmail = table.Column<string>(type: "text", nullable: true),
                    Label = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostHistoryVersion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostHistoryVersion_Category_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Category",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PostHistoryVersion_Post_PostId",
                        column: x => x.PostId,
                        principalTable: "Post",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostMergeRequest",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PostId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Content = table.Column<string>(type: "text", nullable: true),
                    Modifier = table.Column<string>(type: "text", nullable: true),
                    ModifierEmail = table.Column<string>(type: "text", nullable: true),
                    MergeState = table.Column<int>(type: "integer", nullable: false),
                    SubmitTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IP = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostMergeRequest", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostMergeRequest_Post_PostId",
                        column: x => x.PostId,
                        principalTable: "Post",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostVisitRecord",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PostId = table.Column<int>(type: "integer", nullable: false),
                    IP = table.Column<string>(type: "text", nullable: true),
                    Location = table.Column<string>(type: "text", nullable: true),
                    Referer = table.Column<string>(type: "text", nullable: true),
                    RequestUrl = table.Column<string>(type: "text", nullable: true),
                    Time = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostVisitRecord", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostVisitRecord_Post_PostId",
                        column: x => x.PostId,
                        principalTable: "Post",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostVisitRecordStats",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PostId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    Count = table.Column<int>(type: "integer", nullable: false),
                    UV = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostVisitRecordStats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostVisitRecordStats_Post_PostId",
                        column: x => x.PostId,
                        principalTable: "Post",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeminarPost",
                columns: table => new
                {
                    PostId = table.Column<int>(type: "integer", nullable: false),
                    SeminarId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeminarPost", x => new { x.PostId, x.SeminarId });
                    table.ForeignKey(
                        name: "FK_SeminarPost_Post_PostId",
                        column: x => x.PostId,
                        principalTable: "Post",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeminarPost_Seminar_SeminarId",
                        column: x => x.SeminarId,
                        principalTable: "Seminar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeminarPostHistoryVersion",
                columns: table => new
                {
                    PostHistoryVersionId = table.Column<int>(type: "integer", nullable: false),
                    SeminarId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeminarPostHistoryVersion", x => new { x.PostHistoryVersionId, x.SeminarId });
                    table.ForeignKey(
                        name: "FK_SeminarPostHistoryVersion_PostHistoryVersion_PostHistoryVer~",
                        column: x => x.PostHistoryVersionId,
                        principalTable: "PostHistoryVersion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeminarPostHistoryVersion_Seminar_SeminarId",
                        column: x => x.SeminarId,
                        principalTable: "Seminar",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Advertisement_Price",
                table: "Advertisement",
                column: "Price",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisementClickRecord_AdvertisementId",
                table: "AdvertisementClickRecord",
                column: "AdvertisementId");

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisementClickRecord_Time",
                table: "AdvertisementClickRecord",
                column: "Time");

            migrationBuilder.CreateIndex(
                name: "IX_Category_ParentId",
                table: "Category",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Comment_ParentId",
                table: "Comment",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Comment_PostId",
                table: "Comment",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveMessage_ParentId",
                table: "LeaveMessage",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_LeaveMessage_PostDate",
                table: "LeaveMessage",
                column: "PostDate",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_LinkLoopback_LinkId",
                table: "LinkLoopback",
                column: "LinkId");

            migrationBuilder.CreateIndex(
                name: "IX_Links_Recommend",
                table: "Links",
                column: "Recommend");

            migrationBuilder.CreateIndex(
                name: "IX_LoginRecord_UserInfoId",
                table: "LoginRecord",
                column: "UserInfoId");

            migrationBuilder.CreateIndex(
                name: "IX_Menu_ParentId",
                table: "Menu",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Menu_Sort",
                table: "Menu",
                column: "Sort");

            migrationBuilder.CreateIndex(
                name: "IX_Notice_ModifyDate",
                table: "Notice",
                column: "ModifyDate",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Post_AverageViewCount",
                table: "Post",
                column: "AverageViewCount",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Post_CategoryId",
                table: "Post",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Post_ModifyDate",
                table: "Post",
                column: "ModifyDate",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Post_TotalViewCount",
                table: "Post",
                column: "TotalViewCount",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_PostHistoryVersion_CategoryId",
                table: "PostHistoryVersion",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PostHistoryVersion_PostId",
                table: "PostHistoryVersion",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_PostMergeRequest_PostId",
                table: "PostMergeRequest",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_PostVisitRecord_PostId",
                table: "PostVisitRecord",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_PostVisitRecord_Time",
                table: "PostVisitRecord",
                column: "Time",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_PostVisitRecordStats_Date",
                table: "PostVisitRecordStats",
                column: "Date",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_PostVisitRecordStats_PostId",
                table: "PostVisitRecordStats",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_SearchDetails_SearchTime",
                table: "SearchDetails",
                column: "SearchTime",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_SeminarPost_SeminarId",
                table: "SeminarPost",
                column: "SeminarId");

            migrationBuilder.CreateIndex(
                name: "IX_SeminarPostHistoryVersion_SeminarId",
                table: "SeminarPostHistoryVersion",
                column: "SeminarId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdvertisementClickRecord");

            migrationBuilder.DropTable(
                name: "Comment");

            migrationBuilder.DropTable(
                name: "Donate");

            migrationBuilder.DropTable(
                name: "EmailBlocklist");

            migrationBuilder.DropTable(
                name: "FastShare");

            migrationBuilder.DropTable(
                name: "InternalMessage");

            migrationBuilder.DropTable(
                name: "IpInterceptLogs");

            migrationBuilder.DropTable(
                name: "IpReportLogs");

            migrationBuilder.DropTable(
                name: "LeaveMessage");

            migrationBuilder.DropTable(
                name: "LinkLoopback");

            migrationBuilder.DropTable(
                name: "LoginRecord");

            migrationBuilder.DropTable(
                name: "Menu");

            migrationBuilder.DropTable(
                name: "Misc");

            migrationBuilder.DropTable(
                name: "Notice");

            migrationBuilder.DropTable(
                name: "PostMergeRequest");

            migrationBuilder.DropTable(
                name: "PostTag");

            migrationBuilder.DropTable(
                name: "PostVisitRecord");

            migrationBuilder.DropTable(
                name: "PostVisitRecordStats");

            migrationBuilder.DropTable(
                name: "SearchDetails");

            migrationBuilder.DropTable(
                name: "SeminarPost");

            migrationBuilder.DropTable(
                name: "SeminarPostHistoryVersion");

            migrationBuilder.DropTable(
                name: "SystemSetting");

            migrationBuilder.DropTable(
                name: "Variables");

            migrationBuilder.DropTable(
                name: "Advertisement");

            migrationBuilder.DropTable(
                name: "Links");

            migrationBuilder.DropTable(
                name: "UserInfo");

            migrationBuilder.DropTable(
                name: "PostHistoryVersion");

            migrationBuilder.DropTable(
                name: "Seminar");

            migrationBuilder.DropTable(
                name: "Post");

            migrationBuilder.DropTable(
                name: "Category");
        }
    }
}
