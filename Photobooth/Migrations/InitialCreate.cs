using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Photobooth.Data;

#nullable disable

namespace Photobooth.Migrations
{
    [DbContext(typeof(PhotoboothDbContext))]
    [Migration("20260603000000_InitialCreate")]
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id                    = table.Column<int>(nullable: false)
                                               .Annotation("Sqlite:Autoincrement", true),
                    Slug                  = table.Column<string>(maxLength: 100, nullable: false),
                    Name                  = table.Column<string>(maxLength: 200, nullable: false),
                    CreatedAt             = table.Column<DateTime>(nullable: false),
                    ArchivedAt            = table.Column<DateTime>(nullable: true),
                    PaywallEnabled        = table.Column<bool>(nullable: false),
                    SaveImagesEnabled     = table.Column<bool>(nullable: false),
                    PrintLimitPerEvent    = table.Column<int>(nullable: true),
                    PrintLimitPerSession  = table.Column<int>(nullable: true),
                    AccentColor           = table.Column<string>(maxLength: 20, nullable: true),
                    BackgroundColor       = table.Column<string>(maxLength: 20, nullable: true),
                    SurfaceColor          = table.Column<string>(maxLength: 20, nullable: true),
                    BackgroundImagePath   = table.Column<string>(nullable: true),
                    PhotostripTemplatePath= table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id         = table.Column<int>(nullable: false)
                                     .Annotation("Sqlite:Autoincrement", true),
                    EventId    = table.Column<int>(nullable: false),
                    CreatedAt  = table.Column<DateTime>(nullable: false),
                    PrintCount = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sessions_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Photos",
                columns: table => new
                {
                    Id         = table.Column<int>(nullable: false)
                                     .Annotation("Sqlite:Autoincrement", true),
                    SessionId  = table.Column<int>(nullable: false),
                    Sequence   = table.Column<int>(nullable: false),
                    FilePath   = table.Column<string>(nullable: true),
                    IsEnhanced = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Photos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Photos_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EnhancedVariants",
                columns: table => new
                {
                    Id        = table.Column<int>(nullable: false)
                                    .Annotation("Sqlite:Autoincrement", true),
                    PhotoId   = table.Column<int>(nullable: false),
                    StyleId   = table.Column<string>(nullable: false),
                    StyleName = table.Column<string>(nullable: false),
                    FilePath  = table.Column<string>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnhancedVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EnhancedVariants_Photos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "Photos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_Slug",
                table: "Events",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_EventId",
                table: "Sessions",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Photos_SessionId",
                table: "Photos",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_EnhancedVariants_PhotoId_StyleId",
                table: "EnhancedVariants",
                columns: new[] { "PhotoId", "StyleId" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EnhancedVariants");
            migrationBuilder.DropTable(name: "Photos");
            migrationBuilder.DropTable(name: "Sessions");
            migrationBuilder.DropTable(name: "Events");
        }
    }
}
