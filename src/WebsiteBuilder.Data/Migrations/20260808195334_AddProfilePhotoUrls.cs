using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebsiteBuilder.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProfilePhotoUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every existing profile predates photo uploads and has none. Without the default the
            // ALTER fails outright on any table that already has rows — and migrations run at
            // boot, so that is not a failed migration, it is a site that will not start.
            migrationBuilder.AddColumn<List<string>>(
                name: "PhotoUrls",
                table: "BusinessProfiles",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhotoUrls",
                table: "BusinessProfiles");
        }
    }
}
