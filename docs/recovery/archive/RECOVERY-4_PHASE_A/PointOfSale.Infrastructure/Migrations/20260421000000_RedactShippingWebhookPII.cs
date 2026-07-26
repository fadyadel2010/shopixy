using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260421000000_RedactShippingWebhookPII")]
    public partial class RedactShippingWebhookPII : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1: Rename columns first. These checks keep the migration safe if the
            // companion SQL was applied manually before EF history was updated.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'RawPayload'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'PayloadSummary'
                    ) THEN
                        ALTER TABLE "ShippingWebhookLogs" RENAME COLUMN "RawPayload" TO "PayloadSummary";
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'SourceIp'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'SourceIpPrefix'
                    ) THEN
                        ALTER TABLE "ShippingWebhookLogs" RENAME COLUMN "SourceIp" TO "SourceIpPrefix";
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'UserAgent'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'UserAgentSummary'
                    ) THEN
                        ALTER TABLE "ShippingWebhookLogs" RENAME COLUMN "UserAgent" TO "UserAgentSummary";
                    END IF;
                END $$;
                """);

            // Step 2: Redact historical PII in existing rows
            // For compliance, we null out the sensitive data rather than attempting complex redaction in SQL
            // This is safer for large tables and ensures no PII remains
            
            // Clear PayloadSummary (previously RawPayload) - contains full webhook payloads with PII
            migrationBuilder.Sql(
                @"UPDATE ""ShippingWebhookLogs"" 
                  SET ""PayloadSummary"" = '[REDACTED - Historical data removed for compliance]' 
                  WHERE ""PayloadSummary"" IS NOT NULL AND ""PayloadSummary"" != ''");

            // Redact SourceIpPrefix (previously SourceIp) - convert full IPs to network prefixes
            // For IPv4: Keep first 3 octets, zero out last (e.g., 192.168.1.100 -> 192.168.1.0/24)
            // For IPv6 or complex cases: Use generic redaction
            // Uses strict IPv4 regex to ensure only valid numeric IPs are transformed
            migrationBuilder.Sql(
                @"UPDATE ""ShippingWebhookLogs"" 
                  SET ""SourceIpPrefix"" = 
                    CASE 
                      WHEN ""SourceIpPrefix"" ~ '^(25[0-5]|2[0-4][0-9]|1?[0-9]{1,2})(\.(25[0-5]|2[0-4][0-9]|1?[0-9]{1,2})){3}$' THEN
                        substring(""SourceIpPrefix"" FROM 1 FOR length(""SourceIpPrefix"") - position('.' IN reverse(""SourceIpPrefix""))) || '.0/24'
                      ELSE 
                        '[IP_REDACTED]'
                    END
                  WHERE ""SourceIpPrefix"" IS NOT NULL AND ""SourceIpPrefix"" NOT LIKE '%/24' AND ""SourceIpPrefix"" NOT LIKE '%/64' AND ""SourceIpPrefix"" != '[IP_REDACTED]'");

            // Truncate UserAgentSummary (previously UserAgent) - remove detailed version info
            migrationBuilder.Sql(
                @"UPDATE ""ShippingWebhookLogs"" 
                  SET ""UserAgentSummary"" = 
                    CASE 
                      WHEN length(""UserAgentSummary"") > 100 THEN substring(""UserAgentSummary"" FROM 1 FOR 100) || '...'
                      ELSE ""UserAgentSummary""
                    END
                  WHERE ""UserAgentSummary"" IS NOT NULL AND length(""UserAgentSummary"") > 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert column names
            // Note: Redacted data cannot be restored
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'PayloadSummary'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'RawPayload'
                    ) THEN
                        ALTER TABLE "ShippingWebhookLogs" RENAME COLUMN "PayloadSummary" TO "RawPayload";
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'SourceIpPrefix'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'SourceIp'
                    ) THEN
                        ALTER TABLE "ShippingWebhookLogs" RENAME COLUMN "SourceIpPrefix" TO "SourceIp";
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'UserAgentSummary'
                    ) AND NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'ShippingWebhookLogs' AND column_name = 'UserAgent'
                    ) THEN
                        ALTER TABLE "ShippingWebhookLogs" RENAME COLUMN "UserAgentSummary" TO "UserAgent";
                    END IF;
                END $$;
                """);
        }
    }
}
