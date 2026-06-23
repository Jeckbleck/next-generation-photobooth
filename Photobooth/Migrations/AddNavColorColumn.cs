using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Photobooth.Migrations
{
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(Photobooth.Data.PhotoboothDbContext))]
    [Migration("20260622000001_AddNavColorColumn")]
    public partial class AddNavColorColumn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NavColor",
                table: "Events",
                maxLength: 20,
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "NavColor", table: "Events");
        }
    }
}
