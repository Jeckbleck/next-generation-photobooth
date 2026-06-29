using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Photobooth.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(Photobooth.Data.PhotoboothDbContext))]
    [Migration("20260629000001_AddTextColorColumn")]
    public partial class AddTextColorColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TextColor",
                table: "Events",
                maxLength: 20,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TextColor", table: "Events");
        }
    }
}
