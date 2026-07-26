using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentWebhookLogUniqueConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // First, deduplicate existing PaymentWebhookLogs
            // Prefer terminal statuses (Completed/Skipped) over non-terminal (Pending/Processing/Failed)
            // to prevent reprocessing of already-completed webhooks.
            // Per CheckWebhookIdempotencyAsync logic:
            //   - Completed/Skipped → treated as duplicate (no retry)
            //   - Processing/Failed → allows retry
            // Order by terminal status first, then by Id to keep the most reliable record.
            migrationBuilder.Sql(@"
                WITH DuplicateRecords AS (
                    SELECT 
                        Id,
                        ROW_NUMBER() OVER (
                            PARTITION BY TenantId, Provider, ProviderEventId 
                            ORDER BY 
                                CASE 
                                    WHEN ProcessingStatus IN (2, 4) THEN 0  -- Completed=2, Skipped=4 (terminal)
                                    ELSE 1                                    -- Pending=0, Processing=1, Failed=3 (non-terminal)
                                END ASC,
                                Id ASC
                        ) AS RowNum
                    FROM PaymentWebhookLogs
                    WHERE ProviderEventId IS NOT NULL
                )
                DELETE FROM PaymentWebhookLogs
                WHERE Id IN (
                    SELECT Id 
                    FROM DuplicateRecords 
                    WHERE RowNum > 1
                );
            ");

            // Add unique constraint on PaymentWebhookLog (TenantId, Provider, ProviderEventId)
            // This ensures DB-level idempotency protection when Redis fails
            migrationBuilder.CreateIndex(
                name: "IX_PaymentWebhookLogs_TenantId_Provider_ProviderEventId",
                table: "PaymentWebhookLogs",
                columns: new[] { "TenantId", "Provider", "ProviderEventId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentWebhookLogs_TenantId_Provider_ProviderEventId",
                table: "PaymentWebhookLogs");
        }
    }
}
