using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Photobooth.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(Photobooth.Data.PhotoboothDbContext))]
    [Migration("20260618000001_AddGreetingTextColumns")]
    public partial class AddGreetingTextColumns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GreetingEyebrow",
                table: "Events",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GreetingTitle",
                table: "Events",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GreetingSubtitle",
                table: "Events",
                maxLength: 500,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "GreetingEyebrow",  table: "Events");
            migrationBuilder.DropColumn(name: "GreetingTitle",    table: "Events");
            migrationBuilder.DropColumn(name: "GreetingSubtitle", table: "Events");
        }
    }
}
