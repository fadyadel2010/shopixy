using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <summary>
    /// S19C: Add promotion fallback tracking fields to Orders table.
    /// 
    /// CONTEXT:
    /// When promotion calculation fails during checkout, the system falls back to
    /// zero discounts and shows a warning banner. However, on duplicate order recovery
    /// (idempotent checkout), the warning was being dropped because the promotion
    /// fallback state wasn't persisted.
    /// 
    /// SOLUTION:
    /// Add PromotionCalculationFailed and PromotionWarningMessage to Orders table
    /// to preserve the fallback state for consistent duplicate order responses.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260423200000_S19C_AddPromotionFallbackFields")]
    public partial class S19CAddPromotionFallbackFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                // PostgreSQL: Use IF NOT EXISTS for idempotent column addition
                migrationBuilder.Sql(
                    """
                    ALTER TABLE "Orders"
                        ADD COLUMN IF NOT EXISTS "PromotionCalculationFailed" boolean NOT NULL DEFAULT false,
                        ADD COLUMN IF NOT EXISTS "PromotionWarningMessage" text;
                    """);
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // SQLite: Use standard AddColumn (EF Core handles idempotency)
                migrationBuilder.AddColumn<bool>(
                    name: "PromotionCalculationFailed",
                    table: "Orders",
                    type: "INTEGER",
                    nullable: false,
                    defaultValue: false);

                migrationBuilder.AddColumn<string>(
                    name: "PromotionWarningMessage",
                    table: "Orders",
                    type: "TEXT",
                    nullable: true);
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                // SQL Server: Use standard AddColumn (EF Core handles idempotency)
                migrationBuilder.AddColumn<bool>(
                    name: "PromotionCalculationFailed",
                    table: "Orders",
                    type: "bit",
                    nullable: false,
                    defaultValue: false);

                migrationBuilder.AddColumn<string>(
                    name: "PromotionWarningMessage",
                    table: "Orders",
                    type: "nvarchar(max)",
                    nullable: true);
            }
            else
            {
                throw new NotSupportedException(
                    $"S19C migration does not support provider: {migrationBuilder.ActiveProvider}. " +
                    "Supported providers: Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.EntityFrameworkCore.SqlServer, Microsoft.EntityFrameworkCore.Sqlite");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                // PostgreSQL: Use IF EXISTS for idempotent column removal
                migrationBuilder.Sql(
                    """
                    ALTER TABLE "Orders"
                        DROP COLUMN IF EXISTS "PromotionCalculationFailed",
                        DROP COLUMN IF EXISTS "PromotionWarningMessage";
                    """);
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ||
                     migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                // SQLite/SQL Server: Use standard DropColumn (EF Core handles idempotency)
                migrationBuilder.DropColumn(
                    name: "PromotionCalculationFailed",
                    table: "Orders");

                migrationBuilder.DropColumn(
                    name: "PromotionWarningMessage",
                    table: "Orders");
            }
        }
    }
}
