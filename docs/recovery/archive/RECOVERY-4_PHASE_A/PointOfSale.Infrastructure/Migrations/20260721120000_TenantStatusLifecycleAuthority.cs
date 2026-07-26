using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260721120000_TenantStatusLifecycleAuthority")]
public sealed class TenantStatusLifecycleAuthority : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "Tenants"
            SET "Status" = 3,
                "IsActive" = FALSE,
                "UpdatedAtUtc" = COALESCE("UpdatedAtUtc", NOW())
            WHERE "DeletedAtUtc" IS NOT NULL
              AND "Status" <> 3;
            """);

        migrationBuilder.Sql("""
            UPDATE "Tenants"
            SET "Status" = 1,
                "UpdatedAtUtc" = COALESCE("UpdatedAtUtc", NOW())
            WHERE "DeletedAtUtc" IS NULL
              AND "IsActive" = TRUE
              AND "Status" = 0;
            """);

        migrationBuilder.Sql("""
            UPDATE "Tenants"
            SET "IsActive" = ("Status" = 1 AND "DeletedAtUtc" IS NULL),
                "UpdatedAtUtc" = COALESCE("UpdatedAtUtc", NOW())
            WHERE "IsActive" <> ("Status" = 1 AND "DeletedAtUtc" IS NULL);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "Tenants"
            SET "IsActive" = ("Status" = 1 AND "DeletedAtUtc" IS NULL),
                "UpdatedAtUtc" = COALESCE("UpdatedAtUtc", NOW())
            WHERE "IsActive" <> ("Status" = 1 AND "DeletedAtUtc" IS NULL);
            """);
    }
}
