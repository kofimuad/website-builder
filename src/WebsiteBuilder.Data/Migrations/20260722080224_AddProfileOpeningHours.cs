using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileOpeningHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF scaffolds defaultValue: "", which Postgres rejects as invalid jsonb. An empty
            // list of opening hours is the valid equivalent for existing rows.
            migrationBuilder.AddColumn<string>(
                name: "OpeningHours",
                table: "BusinessProfiles",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OpeningHours",
                table: "BusinessProfiles");
        }
    }
}
