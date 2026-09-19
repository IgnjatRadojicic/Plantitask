using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plantitask.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Moves premium from seven columns on Users to a versioned plan catalogue plus grant rows.
    /// See docs/rewrite/services/entitlements.md.
    ///
    /// Order matters here and the scaffolded version had it wrong. The columns are dropped last,
    /// after live premium has been copied into grants, otherwise this migration silently deletes
    /// everyone's subscription.
    /// </summary>
    public partial class AddPlanCatalogAndGrants : Migration
    {
        // Fixed rather than generated so the backfill below and any later migration can name
        // these rows. Seeded data needs stable ids.
        private const string FreeV1 = "11111111-1111-1111-1111-111111111111";
        private const string PremiumV1 = "22222222-2222-2222-2222-222222222222";

        private static readonly DateTime SeededAt = new(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Plans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MaxGroups = table.Column<int>(type: "integer", nullable: false),
                    MaxStorageBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanVersions_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserPlanGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PayPalRef = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GrantedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    EndedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPlanGrants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPlanGrants_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserPlanGrants_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Plans_Name",
                table: "Plans",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanVersions_PlanId_PublishedAt_EffectiveFrom",
                table: "PlanVersions",
                columns: new[] { "PlanId", "PublishedAt", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanVersions_PlanId_Version",
                table: "PlanVersions",
                columns: new[] { "PlanId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPlanGrants_PayPalRef",
                table: "UserPlanGrants",
                column: "PayPalRef");

            migrationBuilder.CreateIndex(
                name: "IX_UserPlanGrants_PayPalRef_Open",
                table: "UserPlanGrants",
                column: "PayPalRef",
                unique: true,
                filter: "\"EndsAt\" IS NULL AND \"PayPalRef\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserPlanGrants_PlanVersionId",
                table: "UserPlanGrants",
                column: "PlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPlanGrants_UserId_StartsAt_EndsAt",
                table: "UserPlanGrants",
                columns: new[] { "UserId", "StartsAt", "EndsAt" });

            // Ids are the PlanTier enum values.
            migrationBuilder.InsertData(
                table: "Plans",
                columns: new[] { "Id", "Name", "DisplayName", "Description", "Color", "DisplayOrder", "IsActive" },
                values: new object[,]
                {
                    { 1, "free", "Free", "Everything you need to run a few trees", "#6c757d", 1, true },
                    { 2, "premium", "Premium", "More trees and more room for files", "#ffc107", 2, true }
                });

            migrationBuilder.InsertData(
                table: "PlanVersions",
                columns: new[] { "Id", "PlanId", "Version", "EffectiveFrom", "PublishedAt", "MaxGroups", "MaxStorageBytes", "CreatedAt" },
                values: new object[,]
                {
                    { Guid.Parse(FreeV1), 1, 1, SeededAt, SeededAt, 5, 50L * 1024 * 1024, SeededAt },
                    { Guid.Parse(PremiumV1), 2, 1, SeededAt, SeededAt, 10, 500L * 1024 * 1024, SeededAt }
                });

            // Carry live premium across before the columns holding it are dropped. Only premium
            // that is still active is worth a grant; expired rows were already meaningless and
            // become rows nobody would ever read.
            //
            // A null PremiumExpiresAt is a live recurring subscription, so it stays null here
            // and the grant is open ended, which is what enforcement reads as still paying.
            //
            // GrantedBy is the user themselves: these all came from their own purchase.
            migrationBuilder.Sql($"""
                INSERT INTO "UserPlanGrants" (
                    "Id", "UserId", "PlanVersionId", "StartsAt", "EndsAt",
                    "Source", "PayPalRef", "CancelledAt",
                    "GrantedBy", "EndedBy", "CreatedAt")
                SELECT
                    gen_random_uuid(),
                    u."Id",
                    '{PremiumV1}'::uuid,
                    COALESCE(u."PremiumStartedAt", now()),
                    u."PremiumExpiresAt",
                    CASE
                        WHEN u."SubscriptionType" = 'recurring' THEN 'paypal_subscription'
                        WHEN u."SubscriptionType" = 'onetime'   THEN 'paypal_onetime'
                        ELSE 'admin_grant'
                    END,
                    COALESCE(u."PayPalSubscriptionId", u."PayPalOrderId"),
                    NULL,
                    u."Id", NULL, now()
                FROM "Users" u
                WHERE u."IsPremium" = true
                  AND (u."PremiumExpiresAt" IS NULL OR u."PremiumExpiresAt" > now())
                  AND u."IsDeleted" = false;
                """);

            migrationBuilder.DropColumn(name: "IsPremium", table: "Users");
            migrationBuilder.DropColumn(name: "MaxGroups", table: "Users");
            migrationBuilder.DropColumn(name: "PayPalOrderId", table: "Users");
            migrationBuilder.DropColumn(name: "PayPalSubscriptionId", table: "Users");
            migrationBuilder.DropColumn(name: "PremiumExpiresAt", table: "Users");
            migrationBuilder.DropColumn(name: "PremiumStartedAt", table: "Users");
            migrationBuilder.DropColumn(name: "SubscriptionType", table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPremium",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxGroups",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<string>(
                name: "PayPalOrderId",
                table: "Users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PayPalSubscriptionId",
                table: "Users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PremiumExpiresAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PremiumStartedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionType",
                table: "Users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // Put live premium back in the columns before the grants holding it are dropped, so
            // rolling back does not cost anybody their subscription either. Best available grant
            // per user, matching the precedence enforcement uses: highest tier, then open ended,
            // then latest expiry.
            migrationBuilder.Sql("""
                UPDATE "Users" u
                SET "IsPremium" = true,
                    "MaxGroups" = 10,
                    "PremiumStartedAt" = g."StartsAt",
                    "PremiumExpiresAt" = g."EndsAt",
                    "SubscriptionType" = CASE
                        WHEN g."Source" = 'paypal_subscription' THEN 'recurring'
                        WHEN g."Source" = 'paypal_onetime'      THEN 'onetime'
                        ELSE NULL
                    END,
                    "PayPalSubscriptionId" = CASE WHEN g."Source" = 'paypal_subscription' THEN g."PayPalRef" END,
                    "PayPalOrderId"        = CASE WHEN g."Source" = 'paypal_onetime'      THEN g."PayPalRef" END
                FROM (
                    SELECT DISTINCT ON (gr."UserId")
                        gr."UserId", gr."StartsAt", gr."EndsAt", gr."Source", gr."PayPalRef"
                    FROM "UserPlanGrants" gr
                    JOIN "PlanVersions" pv ON pv."Id" = gr."PlanVersionId"
                    WHERE gr."StartsAt" <= now()
                      AND (gr."EndsAt" IS NULL OR gr."EndsAt" > now())
                    ORDER BY gr."UserId", pv."PlanId" DESC, (gr."EndsAt" IS NULL) DESC, gr."EndsAt" DESC
                ) g
                WHERE u."Id" = g."UserId";
                """);

            migrationBuilder.DropTable(name: "UserPlanGrants");
            migrationBuilder.DropTable(name: "PlanVersions");
            migrationBuilder.DropTable(name: "Plans");
        }
    }
}
