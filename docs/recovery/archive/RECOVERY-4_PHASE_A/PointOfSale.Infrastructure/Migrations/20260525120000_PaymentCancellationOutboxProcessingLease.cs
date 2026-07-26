using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260525120000_PaymentCancellationOutboxProcessingLease")]
    public partial class PaymentCancellationOutboxProcessingLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "PaymentCancellationOutboxMessages"
                    ADD COLUMN IF NOT EXISTS "ClaimedUntilUtc" timestamp with time zone NULL;
                """);

            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_PaymentCancellationOutbox_Processing";
                CREATE INDEX IF NOT EXISTS "IX_PaymentCancellationOutbox_Processing"
                    ON "PaymentCancellationOutboxMessages" ("ProcessedAtUtc", "IsFailed", "NextRetryAtUtc", "ClaimedUntilUtc")
                    INCLUDE ("CreatedAt");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_PaymentCancellationOutbox_Processing";
                CREATE INDEX IF NOT EXISTS "IX_PaymentCancellationOutbox_Processing"
                    ON "PaymentCancellationOutboxMessages" ("ProcessedAtUtc", "IsFailed", "NextRetryAtUtc")
                    INCLUDE ("CreatedAt");
                ALTER TABLE "PaymentCancellationOutboxMessages"
                    DROP COLUMN IF EXISTS "ClaimedUntilUtc";
                """);
        }
    }
}
