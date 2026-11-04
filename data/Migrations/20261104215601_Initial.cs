using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistillForge.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceText = table.Column<string>(type: "text", nullable: false),
                    Strategy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Colour = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Categories_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExtractionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    FinishReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PromptTokens = table.Column<int>(type: "integer", nullable: true),
                    CompletionTokens = table.Column<int>(type: "integer", nullable: true),
                    EstimatedPromptTokens = table.Column<int>(type: "integer", nullable: true),
                    ActionItemCount = table.Column<int>(type: "integer", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    Warnings = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractionRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExtractionRuns_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Segments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Start = table.Column<int>(type: "integer", nullable: false),
                    End = table.Column<int>(type: "integer", nullable: false),
                    TokenEstimate = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Segments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Segments_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtractionRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: true),
                    Owner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Due = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsManual = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionItems_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ActionItems_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Evidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SegmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quote = table.Column<string>(type: "text", nullable: false),
                    Start = table.Column<int>(type: "integer", nullable: false),
                    End = table.Column<int>(type: "integer", nullable: false),
                    MatchQuality = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Evidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Evidence_ActionItems_ActionItemId",
                        column: x => x.ActionItemId,
                        principalTable: "ActionItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Evidence_Segments_SegmentId",
                        column: x => x.SegmentId,
                        principalTable: "Segments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionItems_CategoryId",
                table: "ActionItems",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionItems_WorkspaceId_CategoryId",
                table: "ActionItems",
                columns: new[] { "WorkspaceId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_WorkspaceId",
                table: "Categories",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_ActionItemId",
                table: "Evidence",
                column: "ActionItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Evidence_SegmentId",
                table: "Evidence",
                column: "SegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionRuns_WorkspaceId",
                table: "ExtractionRuns",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Segments_WorkspaceId_Label",
                table: "Segments",
                columns: new[] { "WorkspaceId", "Label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Segments_WorkspaceId_Ordinal",
                table: "Segments",
                columns: new[] { "WorkspaceId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_IsArchived",
                table: "Workspaces",
                column: "IsArchived");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Evidence");

            migrationBuilder.DropTable(
                name: "ExtractionRuns");

            migrationBuilder.DropTable(
                name: "ActionItems");

            migrationBuilder.DropTable(
                name: "Segments");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "Workspaces");
        }
    }
}
