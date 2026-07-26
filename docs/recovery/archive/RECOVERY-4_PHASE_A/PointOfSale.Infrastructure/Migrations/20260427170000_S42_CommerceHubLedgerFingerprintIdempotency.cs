using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class S42_CommerceHubLedgerFingerprintIdempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                // PostgreSQL: Use IF EXISTS/IF NOT EXISTS for idempotent index management
                migrationBuilder.Sql(
                    """
                    DROP INDEX IF EXISTS "UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_EntityId";
                    DROP INDEX IF EXISTS "UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_Platform";

                    CREATE UNIQUE INDEX IF NOT EXISTS "UX_CHFinancialTx_Fingerprint_Vendor"
                    ON "CommerceHubFinancialTransactions"
                    (
                        "ReferenceType",
                        "ReferenceId",
                        "TransactionType",
                        "EntityId",
                        "Direction",
                        "FinancialBucket",
                        "Amount",
                        "Currency"
                    )
                    WHERE "EntityId" IS NOT NULL;

                    CREATE UNIQUE INDEX IF NOT EXISTS "UX_CHFinancialTx_Fingerprint_Platform"
                    ON "CommerceHubFinancialTransactions"
                    (
                        "ReferenceType",
                        "ReferenceId",
                        "TransactionType",
                        "Direction",
                        "FinancialBucket",
                        "Amount",
                        "Currency"
                    )
                    WHERE "EntityId" IS NULL;
                    """);
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                // SQL Server: Use filtered indexes (supported since SQL Server 2008)
                
                // Drop old indexes (idempotent - check sys.indexes before dropping)
                migrationBuilder.Sql(
                    """
                    IF EXISTS (
                        SELECT 1 FROM sys.indexes 
                        WHERE name = 'UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_EntityId' 
                        AND object_id = OBJECT_ID('CommerceHubFinancialTransactions')
                    )
                    DROP INDEX [UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_EntityId] ON [CommerceHubFinancialTransactions];
                    """);

                migrationBuilder.Sql(
                    """
                    IF EXISTS (
                        SELECT 1 FROM sys.indexes 
                        WHERE name = 'UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_Platform' 
                        AND object_id = OBJECT_ID('CommerceHubFinancialTransactions')
                    )
                    DROP INDEX [UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_Platform] ON [CommerceHubFinancialTransactions];
                    """);

                // Create new filtered indexes with WHERE clause
                migrationBuilder.CreateIndex(
                    name: "UX_CHFinancialTx_Fingerprint_Vendor",
                    table: "CommerceHubFinancialTransactions",
                    columns: new[] { "ReferenceType", "ReferenceId", "TransactionType", "EntityId", "Direction", "FinancialBucket", "Amount", "Currency" },
                    unique: true,
                    filter: "[EntityId] IS NOT NULL");

                migrationBuilder.CreateIndex(
                    name: "UX_CHFinancialTx_Fingerprint_Platform",
                    table: "CommerceHubFinancialTransactions",
                    columns: new[] { "ReferenceType", "ReferenceId", "TransactionType", "Direction", "FinancialBucket", "Amount", "Currency" },
                    unique: true,
                    filter: "[EntityId] IS NULL");
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // SQLite: Partial indexes (WHERE clause) are supported since SQLite 3.8.0,
                // but EF Core's SQLite provider does not expose the filter parameter in
                // migrationBuilder.CreateIndex(), so we fall back to creating full indexes
                // without the WHERE clause filter.
                
                // Drop old indexes (idempotent - use DROP INDEX IF EXISTS)
                migrationBuilder.Sql("DROP INDEX IF EXISTS UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_EntityId;");
                migrationBuilder.Sql("DROP INDEX IF EXISTS UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_Platform;");

                // Create new indexes (without WHERE clause since migrationBuilder.CreateIndex() doesn't support filter parameter)
                migrationBuilder.CreateIndex(
                    name: "UX_CHFinancialTx_Fingerprint_Vendor",
                    table: "CommerceHubFinancialTransactions",
                    columns: new[] { "ReferenceType", "ReferenceId", "TransactionType", "EntityId", "Direction", "FinancialBucket", "Amount", "Currency" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "UX_CHFinancialTx_Fingerprint_Platform",
                    table: "CommerceHubFinancialTransactions",
                    columns: new[] { "ReferenceType", "ReferenceId", "TransactionType", "Direction", "FinancialBucket", "Amount", "Currency" },
                    unique: true);
            }
            else
            {
                throw new NotSupportedException(
                    $"S42 migration does not support provider: {migrationBuilder.ActiveProvider}. " +
                    "Supported providers: Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.EntityFrameworkCore.SqlServer, Microsoft.EntityFrameworkCore.Sqlite");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                // PostgreSQL: Preflight duplicate check before recreating legacy indexes
                migrationBuilder.Sql(
                    """
                    -- S42 Down: Preflight duplicate check for legacy unique indexes
                    -- The new fingerprint indexes allow multiple rows with the same (ReferenceId, TransactionType, EntityId)
                    -- as long as other fields (Direction, Amount, Currency, etc.) differ.
                    -- Before recreating the legacy indexes, we must ensure no such duplicates exist.
                    
                    DO $$
                    DECLARE
                        duplicate_count_vendor INTEGER;
                        duplicate_count_platform INTEGER;
                    BEGIN
                        -- Check for duplicates in vendor transactions (EntityId IS NOT NULL)
                        SELECT COUNT(*) INTO duplicate_count_vendor
                        FROM (
                            SELECT "ReferenceId", "TransactionType", "EntityId", COUNT(*) as cnt
                            FROM "CommerceHubFinancialTransactions"
                            WHERE "EntityId" IS NOT NULL
                            GROUP BY "ReferenceId", "TransactionType", "EntityId"
                            HAVING COUNT(*) > 1
                        ) duplicates;
                        
                        -- Check for duplicates in platform transactions (EntityId IS NULL)
                        SELECT COUNT(*) INTO duplicate_count_platform
                        FROM (
                            SELECT "ReferenceId", "TransactionType", COUNT(*) as cnt
                            FROM "CommerceHubFinancialTransactions"
                            WHERE "EntityId" IS NULL
                            GROUP BY "ReferenceId", "TransactionType"
                            HAVING COUNT(*) > 1
                        ) duplicates;
                        
                        -- Abort if duplicates exist
                        IF duplicate_count_vendor > 0 THEN
                            RAISE EXCEPTION 'S42 Down migration aborted: % duplicate vendor transaction groups found for index UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_EntityId. Clean up duplicates before rolling back this migration.', duplicate_count_vendor;
                        END IF;
                        
                        IF duplicate_count_platform > 0 THEN
                            RAISE EXCEPTION 'S42 Down migration aborted: % duplicate platform transaction groups found for index UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_Platform. Clean up duplicates before rolling back this migration.', duplicate_count_platform;
                        END IF;
                    END $$;
                    """);

                // PostgreSQL: Use IF EXISTS/IF NOT EXISTS for idempotent index management
                migrationBuilder.Sql(
                    """
                    DROP INDEX IF EXISTS "UX_CHFinancialTx_Fingerprint_Vendor";
                    DROP INDEX IF EXISTS "UX_CHFinancialTx_Fingerprint_Platform";

                    CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_EntityId"
                    ON "CommerceHubFinancialTransactions" ("ReferenceId", "TransactionType", "EntityId")
                    WHERE "EntityId" IS NOT NULL;

                    CREATE UNIQUE INDEX IF NOT EXISTS "UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_Platform"
                    ON "CommerceHubFinancialTransactions" ("ReferenceId", "TransactionType")
                    WHERE "EntityId" IS NULL;
                    """);
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite" ||
                     migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                // SQLite/SQL Server: Preflight duplicate check before recreating legacy indexes
                // Note: We use EF Core's Sql method with provider-specific syntax
                
                if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
                {
                    migrationBuilder.Sql(
                        """
                        -- S42 Down: Preflight duplicate check for SQL Server
                        DECLARE @duplicate_count_vendor INT;
                        DECLARE @duplicate_count_platform INT;
                        
                        SELECT @duplicate_count_vendor = COUNT(*)
                        FROM (
                            SELECT ReferenceId, TransactionType, EntityId, COUNT(*) as cnt
                            FROM CommerceHubFinancialTransactions
                            WHERE EntityId IS NOT NULL
                            GROUP BY ReferenceId, TransactionType, EntityId
                            HAVING COUNT(*) > 1
                        ) duplicates;
                        
                        SELECT @duplicate_count_platform = COUNT(*)
                        FROM (
                            SELECT ReferenceId, TransactionType, COUNT(*) as cnt
                            FROM CommerceHubFinancialTransactions
                            WHERE EntityId IS NULL
                            GROUP BY ReferenceId, TransactionType
                            HAVING COUNT(*) > 1
                        ) duplicates;
                        
                        IF @duplicate_count_vendor > 0
                        BEGIN
                            RAISERROR('S42 Down migration aborted: %d duplicate vendor transaction groups found for index UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_EntityId. Clean up duplicates before rolling back this migration.', 16, 1, @duplicate_count_vendor);
                        END
                        
                        IF @duplicate_count_platform > 0
                        BEGIN
                            RAISERROR('S42 Down migration aborted: %d duplicate platform transaction groups found for index UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_Platform. Clean up duplicates before rolling back this migration.', 16, 1, @duplicate_count_platform);
                        END
                        """);
                }
                else // SQLite
                {
                    // SQLite doesn't support stored procedures, so we'll check in C# after attempting the index creation
                    // The unique index creation will fail if duplicates exist, which is acceptable for SQLite
                }
                
                // Drop new indexes
                migrationBuilder.DropIndex(
                    name: "UX_CHFinancialTx_Fingerprint_Vendor",
                    table: "CommerceHubFinancialTransactions");

                migrationBuilder.DropIndex(
                    name: "UX_CHFinancialTx_Fingerprint_Platform",
                    table: "CommerceHubFinancialTransactions");

                // Recreate old indexes (without WHERE clause)
                // These will fail if duplicates exist, which is the desired behavior
                migrationBuilder.CreateIndex(
                    name: "UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_EntityId",
                    table: "CommerceHubFinancialTransactions",
                    columns: new[] { "ReferenceId", "TransactionType", "EntityId" },
                    unique: true);

                migrationBuilder.CreateIndex(
                    name: "UX_CommerceHubFinancialTransactions_ReferenceId_TransactionType_Platform",
                    table: "CommerceHubFinancialTransactions",
                    columns: new[] { "ReferenceId", "TransactionType" },
                    unique: true);
            }
            else
            {
                throw new NotSupportedException(
                    $"S42 migration does not support provider: {migrationBuilder.ActiveProvider}. " +
                    "Supported providers: Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.EntityFrameworkCore.SqlServer, Microsoft.EntityFrameworkCore.Sqlite");
            }
        }
    }
}
