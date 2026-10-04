using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plantitask.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class GroupTimeZoneAndTaskDueMoment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_DueDate",
                table: "Tasks");

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Groups",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Europe/Belgrade");

            // The default only fills the groups that already exist. New groups must set the zone themselves.
            migrationBuilder.Sql(@"ALTER TABLE ""Groups"" ALTER COLUMN ""TimeZoneId"" DROP DEFAULT;");

            migrationBuilder.AddColumn<DateTime>(
                name: "DueAt",
                table: "Tasks",
                type: "timestamp with time zone",
                nullable: true);

            // Casting timestamptz to date uses the session zone and the connection sets none so UTC is named.
            // Existing rows hold the picked day as utc midnight.
            migrationBuilder.Sql(@"ALTER TABLE ""Tasks"" ALTER COLUMN ""DueDate"" TYPE date USING (""DueDate"" AT TIME ZONE 'UTC')::date;");

            migrationBuilder.Sql(@"
                UPDATE ""Tasks"" t
                SET ""DueAt"" = (t.""DueDate"" + 1)::timestamp AT TIME ZONE g.""TimeZoneId""
                FROM ""Groups"" g
                WHERE g.""Id"" = t.""GroupId"" AND t.""DueDate"" IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_DueAt",
                table: "Tasks",
                column: "DueAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_DueAt",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "DueAt",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Groups");

            migrationBuilder.Sql(@"ALTER TABLE ""Tasks"" ALTER COLUMN ""DueDate"" TYPE timestamp with time zone USING (""DueDate""::timestamp AT TIME ZONE 'UTC');");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_DueDate",
                table: "Tasks",
                column: "DueDate");
        }
    }
}
