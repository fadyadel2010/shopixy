using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260510210000_TenantScopedSystemSettingsAuthority")]
public sealed class TenantScopedSystemSettingsAuthority : Migration
{
    private static readonly string[] TenantScopedKeys =
    [
        "Company.Name",
        "Company.Address",
        "Company.Phone",
        "Company.Email",
        "Company.Logo",
        "Tax.CurrencyCode",
        "Tax.CurrencySymbol",
        "Tax.Enable",
        "Tax.Rate",
        "Receipt.Header",
        "Receipt.Footer",
        "Receipt.ShowLogo"
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "TenantId",
            table: "SystemSettings",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_SystemSettings_TenantId_Key",
            table: "SystemSettings",
            columns: new[] { "TenantId", "Key" },
            unique: true,
            filter: "\"TenantId\" IS NOT NULL AND NOT \"IsDeleted\"");

        migrationBuilder.AddForeignKey(
            name: "FK_SystemSettings_Tenants_TenantId",
            table: "SystemSettings",
            column: "TenantId",
            principalTable: "Tenants",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        var scopedKeysSql = string.Join(", ", TenantScopedKeys.Select(key => $"'{key}'"));
        migrationBuilder.Sql($"""
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM "SystemSettings"
                    WHERE "TenantId" IS NULL
                      AND NOT "IsDeleted"
                      AND "Key" IN ({scopedKeysSql})
                    GROUP BY "Key"
                    HAVING COUNT(*) > 1
                ) THEN
                    RAISE EXCEPTION 'TenantScopedSystemSettingsAuthority migration requires reconciliation because duplicate global tenant-facing SystemSettings rows exist. Deduplicate the affected keys before applying this migration.';
                END IF;
            END
            $$;
            """);

        migrationBuilder.Sql($"""
            INSERT INTO "SystemSettings" (
                "Id",
                "TenantId",
                "Key",
                "Value",
                "Description",
                "Category",
                "CreatedAt",
                "UpdatedAt",
                "CreatedBy",
                "UpdatedBy",
                "IsDeleted")
            SELECT
                gen_random_uuid(),
                tenant."Id",
                setting."Key",
                setting."Value",
                setting."Description",
                setting."Category",
                setting."CreatedAt",
                setting."UpdatedAt",
                setting."CreatedBy",
                setting."UpdatedBy",
                FALSE
            FROM "SystemSettings" AS setting
            CROSS JOIN "Tenants" AS tenant
            WHERE setting."TenantId" IS NULL
              AND NOT setting."IsDeleted"
              AND setting."Key" IN ({scopedKeysSql})
              AND NOT EXISTS (
                  SELECT 1
                  FROM "SystemSettings" AS existing
                  WHERE existing."TenantId" = tenant."Id"
                    AND existing."Key" = setting."Key"
                    AND NOT existing."IsDeleted");
            """);

        // NOTE: The original global rows (TenantId IS NULL) are intentionally left intact
        // for rollback safety. If this migration is rolled back via Down(), the tenant-scoped
        // rows are deleted and the system reverts to using the global rows.
        // A follow-up migration can soft-delete the global rows after production verification.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        var scopedKeysSql = string.Join(", ", TenantScopedKeys.Select(key => $"'{key}'"));
        migrationBuilder.Sql($"""
            DELETE FROM "SystemSettings"
            WHERE "TenantId" IS NOT NULL
              AND "Key" IN ({scopedKeysSql});
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_SystemSettings_Tenants_TenantId",
            table: "SystemSettings");

        migrationBuilder.DropIndex(
            name: "IX_SystemSettings_TenantId_Key",
            table: "SystemSettings");

        migrationBuilder.DropColumn(
            name: "TenantId",
            table: "SystemSettings");
    }
}
