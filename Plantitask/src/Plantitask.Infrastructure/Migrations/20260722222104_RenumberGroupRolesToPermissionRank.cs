using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Plantitask.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenumberGroupRolesToPermissionRank : Migration
    {
        // GroupRoleLookup.Id now equals the permission rank (== the GroupRole enum value).
        // Old Id -> new Id: Owner 1->100, Manager 2->75, TeamLead 3->50, Member 4->25.
        // GroupMembers.RoleId is a Restrict FK into GroupRoles so the order must be
        // insert the new rows first then remap the memberships then delete the old rows.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "GroupRoles",
                columns: new[] { "Id", "Color", "Description", "DisplayName", "DisplayOrder", "IsActive", "Name", "PermissionLevel" },
                values: new object[,]
                {
                    { 25, "#6c757d", "Can view and work on tasks", "Member", 4, true, "Member", 25 },
                    { 50, "#0dcaf0", "Can manage tasks", "Team Lead", 3, true, "TeamLead", 50 },
                    { 75, "#ffc107", "Can manage members and tasks", "Manager", 2, true, "Manager", 75 },
                    { 100, "#dc3545", "Full control over the group", "Owner", 1, true, "Owner", 100 }
                });

            migrationBuilder.Sql(@"
                UPDATE ""GroupMembers"" SET ""RoleId"" = CASE ""RoleId""
                    WHEN 1 THEN 100
                    WHEN 2 THEN 75
                    WHEN 3 THEN 50
                    WHEN 4 THEN 25
                    ELSE ""RoleId""
                END;");

            migrationBuilder.DeleteData(table: "GroupRoles", keyColumn: "Id", keyValue: 1);
            migrationBuilder.DeleteData(table: "GroupRoles", keyColumn: "Id", keyValue: 2);
            migrationBuilder.DeleteData(table: "GroupRoles", keyColumn: "Id", keyValue: 3);
            migrationBuilder.DeleteData(table: "GroupRoles", keyColumn: "Id", keyValue: 4);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "GroupRoles",
                columns: new[] { "Id", "Color", "Description", "DisplayName", "DisplayOrder", "IsActive", "Name", "PermissionLevel" },
                values: new object[,]
                {
                    { 1, "#dc3545", "Full control over the group", "Owner", 1, true, "Owner", 100 },
                    { 2, "#ffc107", "Can manage members and tasks", "Manager", 2, true, "Manager", 75 },
                    { 3, "#0dcaf0", "Can manage tasks", "Team Lead", 3, true, "TeamLead", 50 },
                    { 4, "#6c757d", "Can view and work on tasks", "Member", 4, true, "Member", 25 }
                });

            migrationBuilder.Sql(@"
                UPDATE ""GroupMembers"" SET ""RoleId"" = CASE ""RoleId""
                    WHEN 100 THEN 1
                    WHEN 75 THEN 2
                    WHEN 50 THEN 3
                    WHEN 25 THEN 4
                    ELSE ""RoleId""
                END;");

            migrationBuilder.DeleteData(table: "GroupRoles", keyColumn: "Id", keyValue: 25);
            migrationBuilder.DeleteData(table: "GroupRoles", keyColumn: "Id", keyValue: 50);
            migrationBuilder.DeleteData(table: "GroupRoles", keyColumn: "Id", keyValue: 75);
            migrationBuilder.DeleteData(table: "GroupRoles", keyColumn: "Id", keyValue: 100);
        }
    }
}
