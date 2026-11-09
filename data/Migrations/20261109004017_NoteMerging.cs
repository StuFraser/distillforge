using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistillForge.Data.Migrations
{
    /// <inheritdoc />
    public partial class NoteMerging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoId",
                table: "ActionItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ActionItemLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionItemAId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionItemBId = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Dismissed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionItemLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActionItemLinks_ActionItems_ActionItemAId",
                        column: x => x.ActionItemAId,
                        principalTable: "ActionItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActionItemLinks_ActionItems_ActionItemBId",
                        column: x => x.ActionItemBId,
                        principalTable: "ActionItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActionItemLinks_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionItems_MergedIntoId",
                table: "ActionItems",
                column: "MergedIntoId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionItemLinks_ActionItemAId_ActionItemBId",
                table: "ActionItemLinks",
                columns: new[] { "ActionItemAId", "ActionItemBId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActionItemLinks_ActionItemBId",
                table: "ActionItemLinks",
                column: "ActionItemBId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionItemLinks_WorkspaceId",
                table: "ActionItemLinks",
                column: "WorkspaceId");

            migrationBuilder.AddForeignKey(
                name: "FK_ActionItems_ActionItems_MergedIntoId",
                table: "ActionItems",
                column: "MergedIntoId",
                principalTable: "ActionItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActionItems_ActionItems_MergedIntoId",
                table: "ActionItems");

            migrationBuilder.DropTable(
                name: "ActionItemLinks");

            migrationBuilder.DropIndex(
                name: "IX_ActionItems_MergedIntoId",
                table: "ActionItems");

            migrationBuilder.DropColumn(
                name: "MergedIntoId",
                table: "ActionItems");
        }
    }
}
