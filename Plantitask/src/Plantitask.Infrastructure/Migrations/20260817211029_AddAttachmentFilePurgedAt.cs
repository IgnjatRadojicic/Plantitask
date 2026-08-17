using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plantitask.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachmentFilePurgedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FilePurgedAt",
                table: "TaskAttachments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskAttachments_PendingPurge",
                table: "TaskAttachments",
                column: "DeletedAt",
                filter: "\"IsDeleted\" = true AND \"FilePurgedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaskAttachments_PendingPurge",
                table: "TaskAttachments");

            migrationBuilder.DropColumn(
                name: "FilePurgedAt",
                table: "TaskAttachments");
        }
    }
}
