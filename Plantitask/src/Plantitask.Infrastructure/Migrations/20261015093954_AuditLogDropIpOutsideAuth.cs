using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plantitask.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditLogDropIpOutsideAuth : Migration
    {
        // Rows written before the auth only rule still carry an ip and user agent. Same four
        // actions as AuditService.AuthActions, written out because a migration must keep meaning
        // what it meant on the day it ran even if the constants change later.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE \"AuditLogs\" SET \"IpAddress\" = NULL, \"UserAgent\" = NULL " +
                "WHERE \"Action\" NOT IN ('Registered', 'Login', 'Logout', 'PasswordReset');");
        }

        // Nothing to restore. The values are gone on purpose.
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
