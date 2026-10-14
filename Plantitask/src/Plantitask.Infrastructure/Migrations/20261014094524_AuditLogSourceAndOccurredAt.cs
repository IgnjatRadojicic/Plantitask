using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plantitask.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditLogSourceAndOccurredAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "UserName",
                table: "AuditLogs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "AuditLogs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "UserEmail",
                table: "AuditLogs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256);

            migrationBuilder.AlterColumn<string>(
                name: "UserAgent",
                table: "AuditLogs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "IpAddress",
                table: "AuditLogs",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(45)",
                oldMaxLength: 45);

            // Added nullable, backfilled, then made required, so no default is left behind on either
            // column. The generated version gave them '' and year 1, which the Source check rejects
            // and which would quietly fill any insert that forgot them.
            migrationBuilder.AddColumn<DateTime>(
                name: "OccurredAt",
                table: "AuditLogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "AuditLogs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Every existing row came from a signed in request and was written right after it.
            migrationBuilder.Sql("UPDATE \"AuditLogs\" SET \"Source\" = 'Request', \"OccurredAt\" = \"CreatedAt\";");

            migrationBuilder.AlterColumn<DateTime>(
                name: "OccurredAt",
                table: "AuditLogs",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Source",
                table: "AuditLogs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditLogs_ActorComplete",
                table: "AuditLogs",
                sql: "(\"UserId\" IS NULL) = (\"UserName\" IS NULL) AND (\"UserId\" IS NULL) = (\"UserEmail\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditLogs_RequestHasActor",
                table: "AuditLogs",
                sql: "\"Source\" <> 'Request' OR \"UserId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuditLogs_Source",
                table: "AuditLogs",
                sql: "\"Source\" IN ('Request', 'Webhook', 'Job')");
        }

        /// <inheritdoc />
        // Lossy once a webhook or job row exists: UserId cannot become required again over null rows.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditLogs_ActorComplete",
                table: "AuditLogs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditLogs_RequestHasActor",
                table: "AuditLogs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuditLogs_Source",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "OccurredAt",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "AuditLogs");

            migrationBuilder.AlterColumn<string>(
                name: "UserName",
                table: "AuditLogs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "AuditLogs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "UserEmail",
                table: "AuditLogs",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(256)",
                oldMaxLength: 256,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "UserAgent",
                table: "AuditLogs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "IpAddress",
                table: "AuditLogs",
                type: "character varying(45)",
                maxLength: 45,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(45)",
                oldMaxLength: 45,
                oldNullable: true);
        }
    }
}
