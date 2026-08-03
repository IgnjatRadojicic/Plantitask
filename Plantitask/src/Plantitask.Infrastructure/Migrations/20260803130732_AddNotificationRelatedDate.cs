using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plantitask.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationRelatedDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RelatedDate",
                table: "Notifications",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RelatedDate",
                table: "Notifications");
        }
    }
}
