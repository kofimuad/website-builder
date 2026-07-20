using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EF scaffolds defaultValue: "" here, which Postgres rejects as invalid jsonb.
            // An empty definition at the current schema version is the valid equivalent.
            migrationBuilder.AddColumn<string>(
                name: "Draft",
                table: "Sites",
                type: "jsonb",
                nullable: false,
                defaultValue: """{"schemaVersion":1,"meta":{},"theme":{},"sections":[]}""");

            migrationBuilder.AddColumn<string>(
                name: "Published",
                table: "Sites",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedUtc",
                table: "Sites",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Draft",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "Published",
                table: "Sites");

            migrationBuilder.DropColumn(
                name: "PublishedUtc",
                table: "Sites");
        }
    }
}
