using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistillForge.Data.Migrations
{
    /// <inheritdoc />
    public partial class RunProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Phase",
                table: "ExtractionRuns",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WindowCount",
                table: "ExtractionRuns",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WindowIndex",
                table: "ExtractionRuns",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Phase",
                table: "ExtractionRuns");

            migrationBuilder.DropColumn(
                name: "WindowCount",
                table: "ExtractionRuns");

            migrationBuilder.DropColumn(
                name: "WindowIndex",
                table: "ExtractionRuns");
        }
    }
}
