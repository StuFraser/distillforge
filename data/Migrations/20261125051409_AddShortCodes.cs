using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DistillForge.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShortCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Workspaces",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "ActionItems",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            // Existing rows get codes derived from their position. Multiplying by an odd constant modulo 2^24 is a bijection, so the
            // 6 hex digits (a subset of the code alphabet) cannot collide, which the unique indexes below need.
            migrationBuilder.Sql(@"
                UPDATE ""Workspaces"" t SET ""Code"" = upper(lpad(to_hex(((r.n * 2654435761) % 16777216)::int), 6, '0'))
                FROM (SELECT ""Id"", row_number() OVER (ORDER BY ""CreatedAt"", ""Id"") AS n FROM ""Workspaces"") r WHERE r.""Id"" = t.""Id"";");
            migrationBuilder.Sql(@"
                UPDATE ""ActionItems"" t SET ""Code"" = upper(lpad(to_hex(((r.n * 2654435761) % 16777216)::int), 6, '0'))
                FROM (SELECT ""Id"", row_number() OVER (ORDER BY ""CreatedAt"", ""Id"") AS n FROM ""ActionItems"") r WHERE r.""Id"" = t.""Id"";");

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_Code",
                table: "Workspaces",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActionItems_Code",
                table: "ActionItems",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Workspaces_Code",
                table: "Workspaces");

            migrationBuilder.DropIndex(
                name: "IX_ActionItems_Code",
                table: "ActionItems");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Workspaces");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "ActionItems");
        }
    }
}
