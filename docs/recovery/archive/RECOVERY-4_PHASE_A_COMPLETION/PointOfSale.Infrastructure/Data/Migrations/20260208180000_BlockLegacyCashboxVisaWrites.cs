using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <summary>
    /// PHASE LCR-5: Block legacy writes to CashboxTransactions and VisaTransactions.
    /// 
    /// This migration adds database triggers to prevent direct inserts to legacy tables,
    /// enforcing all financial transactions to go through the Ledger via the Outbox pattern.
    /// 
    /// IMPORTANT: Triggers only block inserts when LCR_ENFORCEMENT_ENABLED setting is TRUE.
    /// This allows gradual rollout and rollback if needed.
    /// </summary>
    public partial class BlockLegacyCashboxVisaWrites : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create a settings table to control enforcement (if not exists)
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""SystemSettings"" (
                    ""Key"" text PRIMARY KEY,
                    ""Value"" text NOT NULL,
                    ""Description"" text,
                    ""UpdatedAt"" timestamp with time zone DEFAULT now()
                );
            ");
            
            // Insert enforcement setting (disabled by default for safe rollout)
            migrationBuilder.Sql(@"
                INSERT INTO ""SystemSettings"" (""Key"", ""Value"", ""Description"")
                VALUES ('LCR_ENFORCEMENT_ENABLED', 'false', 'When true, blocks legacy CashboxTransactions and VisaTransactions inserts')
                ON CONFLICT (""Key"") DO NOTHING;
            ");

            // Trigger function for CashboxTransactions
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION block_legacy_cashbox_insert()
                RETURNS TRIGGER AS $$
                DECLARE
                    enforcement_enabled boolean;
                BEGIN
                    -- Check if enforcement is enabled
                    SELECT COALESCE(""Value""::boolean, false) INTO enforcement_enabled
                    FROM ""SystemSettings""
                    WHERE ""Key"" = 'LCR_ENFORCEMENT_ENABLED';
                    
                    IF enforcement_enabled THEN
                        -- Allow only specific allowed transaction types (legacy admin operations)
                        -- These are: OpenDrawer(1), CloseDrawer(2), CashIn(7), CashOut(8) - typically from Treasury
                        IF NEW.""TransactionType"" NOT IN (1, 2, 7, 8) THEN
                            RAISE EXCEPTION 'PHASE_LCR_VIOLATION: Direct insert to CashboxTransactions is blocked. Use Treasury/Ledger instead. TransactionType=%', NEW.""TransactionType""
                            USING ERRCODE = 'P0001';
                        END IF;
                    END IF;
                    
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
            ");

            // Trigger function for VisaTransactions
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION block_legacy_visa_insert()
                RETURNS TRIGGER AS $$
                DECLARE
                    enforcement_enabled boolean;
                BEGIN
                    -- Check if enforcement is enabled
                    SELECT COALESCE(""Value""::boolean, false) INTO enforcement_enabled
                    FROM ""SystemSettings""
                    WHERE ""Key"" = 'LCR_ENFORCEMENT_ENABLED';
                    
                    IF enforcement_enabled THEN
                        -- Block all VisaTransactions inserts - all should go through Treasury/Ledger
                        RAISE EXCEPTION 'PHASE_LCR_VIOLATION: Direct insert to VisaTransactions is blocked. Use Treasury/Ledger instead.'
                        USING ERRCODE = 'P0001';
                    END IF;
                    
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
            ");

            // Create triggers
            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS tr_block_legacy_cashbox_insert ON ""CashboxTransactions"";
                CREATE TRIGGER tr_block_legacy_cashbox_insert
                BEFORE INSERT ON ""CashboxTransactions""
                FOR EACH ROW
                EXECUTE FUNCTION block_legacy_cashbox_insert();
            ");

            migrationBuilder.Sql(@"
                DROP TRIGGER IF EXISTS tr_block_legacy_visa_insert ON ""VisaTransactions"";
                CREATE TRIGGER tr_block_legacy_visa_insert
                BEFORE INSERT ON ""VisaTransactions""
                FOR EACH ROW
                EXECUTE FUNCTION block_legacy_visa_insert();
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop triggers
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS tr_block_legacy_cashbox_insert ON ""CashboxTransactions"";");
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS tr_block_legacy_visa_insert ON ""VisaTransactions"";");
            
            // Drop functions
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS block_legacy_cashbox_insert();");
            migrationBuilder.Sql(@"DROP FUNCTION IF EXISTS block_legacy_visa_insert();");
            
            // Remove setting (optional - keep for audit trail)
            migrationBuilder.Sql(@"DELETE FROM ""SystemSettings"" WHERE ""Key"" = 'LCR_ENFORCEMENT_ENABLED';");
        }
    }
}
