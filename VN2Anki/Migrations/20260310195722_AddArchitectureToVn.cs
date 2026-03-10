using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VN2Anki.Migrations
{
    /// <inheritdoc />
    public partial class AddArchitectureToVn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Architecture",
                table: "VisualNovels",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Architecture",
                table: "VisualNovels");
        }
    }
}
