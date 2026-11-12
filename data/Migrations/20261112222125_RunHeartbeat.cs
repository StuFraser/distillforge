using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistillForge.Data.Migrations
{
    /// <inheritdoc />
    public partial class RunHeartbeat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HeartbeatAt",
                table: "ExtractionRuns",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeartbeatAt",
                table: "ExtractionRuns");
        }
    }
}
