using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plantitask.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDescriptionTrigramIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Description",
                table: "Tasks",
                column: "Description")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_Description",
                table: "Tasks");
        }
    }
}
