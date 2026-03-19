using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plantitask.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxGroupsToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxGroups",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 5);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxGroups",
                table: "Users");
        }
    }
}
