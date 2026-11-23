using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistillForge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserTier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Tier",
                table: "AppUsers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Lite");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Tier",
                table: "AppUsers");
        }
    }
}
