using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistillForge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiModels_IsDefault",
                table: "AiModels");

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "Workspaces",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "AiProviders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerId",
                table: "AiModels",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ObjectId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUsers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_OwnerId",
                table: "Workspaces",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_AiProviders_OwnerId",
                table: "AiProviders",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_OwnerId",
                table: "AiModels",
                column: "OwnerId",
                unique: true,
                filter: "\"IsDefault\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_TenantId_ObjectId",
                table: "AppUsers",
                columns: new[] { "TenantId", "ObjectId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AiModels_AppUsers_OwnerId",
                table: "AiModels",
                column: "OwnerId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AiProviders_AppUsers_OwnerId",
                table: "AiProviders",
                column: "OwnerId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Workspaces_AppUsers_OwnerId",
                table: "Workspaces",
                column: "OwnerId",
                principalTable: "AppUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiModels_AppUsers_OwnerId",
                table: "AiModels");

            migrationBuilder.DropForeignKey(
                name: "FK_AiProviders_AppUsers_OwnerId",
                table: "AiProviders");

            migrationBuilder.DropForeignKey(
                name: "FK_Workspaces_AppUsers_OwnerId",
                table: "Workspaces");

            migrationBuilder.DropTable(
                name: "AppUsers");

            migrationBuilder.DropIndex(
                name: "IX_Workspaces_OwnerId",
                table: "Workspaces");

            migrationBuilder.DropIndex(
                name: "IX_AiProviders_OwnerId",
                table: "AiProviders");

            migrationBuilder.DropIndex(
                name: "IX_AiModels_OwnerId",
                table: "AiModels");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "AiProviders");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "AiModels");

            migrationBuilder.CreateIndex(
                name: "IX_AiModels_IsDefault",
                table: "AiModels",
                column: "IsDefault",
                unique: true,
                filter: "\"IsDefault\" = true");
        }
    }
}
