using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistillForge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AiProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AiModelId",
                table: "ExtractionRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ApiKeyEncrypted = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiModels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AiProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ContextTokens = table.Column<int>(type: "integer", nullable: false),
                    ReserveOutput = table.Column<int>(type: "integer", nullable: false),
                    ResponseFormat = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiModels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiModels_AiProviders_AiProviderId",
                        column: x => x.AiProviderId,
                        principalTable: "AiProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionRuns_AiModelId",
                table: "ExtractionRuns",
                column: "AiModelId");

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_AiProviderId_ModelId",
                table: "AiModels",
                columns: new[] { "AiProviderId", "ModelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_IsDefault",
                table: "AiModels",
                column: "IsDefault",
                unique: true,
                filter: "\"IsDefault\" = true");

            migrationBuilder.AddForeignKey(
                name: "FK_ExtractionRuns_AiModels_AiModelId",
                table: "ExtractionRuns",
                column: "AiModelId",
                principalTable: "AiModels",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ExtractionRuns_AiModels_AiModelId",
                table: "ExtractionRuns");

            migrationBuilder.DropTable(
                name: "AiModels");

            migrationBuilder.DropTable(
                name: "AiProviders");

            migrationBuilder.DropIndex(
                name: "IX_ExtractionRuns_AiModelId",
                table: "ExtractionRuns");

            migrationBuilder.DropColumn(
                name: "AiModelId",
                table: "ExtractionRuns");
        }
    }
}
