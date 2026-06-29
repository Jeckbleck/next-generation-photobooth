using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Photobooth.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(Photobooth.Data.PhotoboothDbContext))]
    [Migration("20260630000001_AddTextSecondaryColorColumn")]
    public partial class AddTextSecondaryColorColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TextSecondaryColor",
                table: "Events",
                maxLength: 20,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TextSecondaryColor", table: "Events");
        }
    }
}
