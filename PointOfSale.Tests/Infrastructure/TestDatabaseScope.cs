using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Infrastructure;

internal sealed class TestDatabaseScope : IDisposable, IAsyncDisposable
{
    private readonly ManagedTestDatabase _database;
    private bool _disposed;

    private TestDatabaseScope(ManagedTestDatabase database)
    {
        _database = database;
    }

    public static TestDatabaseScope Open([CallerFilePath] string callerFilePath = "")
    {
        var database = TestDatabaseRegistry.Open(callerFilePath);
        return new TestDatabaseScope(database);
    }

    public ApplicationDbContext CreateDbContext()
    {
        return new ApplicationDbContext(_database.CreateOptions());
    }

    public ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        return CreateDbContext(new TestTenantContext(tenantId));
    }

    public ApplicationDbContext CreateDbContext(ITenantContext tenantContext)
    {
        return new ApplicationDbContext(_database.CreateOptions(), tenantContext);
    }

    /// <summary>
    /// Synchronously disposes the TestDatabaseScope by blocking on DisposeAsync().
    /// NOTE: This synchronous blocking is intentional for test infrastructure cleanup
    /// and may deadlock in certain synchronization contexts. For async disposal,
    /// prefer calling DisposeAsync() directly. This pattern is acceptable here because
    /// test runners typically don't have problematic synchronization contexts.
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        return _database.ReleaseAsync();
    }
}

internal static class TestDatabaseRegistry
{
    private static readonly ConcurrentDictionary<string, Lazy<ManagedTestDatabase>> Databases = new(StringComparer.OrdinalIgnoreCase);

    public static ManagedTestDatabase Open(string callerFilePath)
    {
        var key = BuildKey(callerFilePath);
        var database = Databases.GetOrAdd(
            key,
            _ => new Lazy<ManagedTestDatabase>(
                () => new ManagedTestDatabase(key),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        database.Acquire();
        return database;
    }

    private static string BuildKey(string callerFilePath)
    {
        var relativePath = string.IsNullOrWhiteSpace(callerFilePath)
            ? "unknown"
            : Path.GetRelativePath(TestConfiguration.GetSolutionRoot(), callerFilePath);

        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        return $"{fileName}_{TestConfiguration.BuildDeterministicSuffix(relativePath)}";
    }
}

internal sealed class ManagedTestDatabase
{
    private const string SupplementalSqlBaselineMigration = "20260723214642_Baseline_Recovery3";
    // These SQL migrations exist in production but are not represented by EF migration Up() operations.
    // Apply them in a fixed order around the targeted EF migration chain used by reliability tests.
    private static readonly string[] EarlySupplementalSqlMigrations =
    {
        "20260320_AddCommerceHubCartPersistence.sql",
        "20260320_AddCommerceHubSearchFilterEvents.sql",
        "20260320_AddCommerceHubSubOrderShippingSelections.sql",
        "20260320_AddCommerceHubSubOrderShippingSnapshotJson.sql",
        "20260322_ProductionHardeningSecurityAndAudit.sql",
        "20260322_AddCommerceHubCheckoutLocksAndIdempotency.sql",
        "20260322_AddCommerceHubCodPaymentSnapshots.sql",
        "20260322_AddCommerceHubOrderNumberSequenceAndVendorIndexes.sql",
        "20260322_AddCommerceHubPaymentsCommissionAndEarnings.sql",
        "20260322_AddCommerceHubPayoutsRefundsAndFinance.sql",
        "20260322_HardenCommerceHubCartPersistence.sql",
        "20260322_HardenCommerceHubSubOrderSafety.sql",
        "20260322_ZZ_HardenCommerceHubFinanceReliability.sql",
        "20260322_ZZZ_AddSuperAdminAuthentication.sql",
        "20260323_ZZA_AddSuperAdminRefreshTokens.sql",
        "20260323_ZZB_FixSuperAdminRootTriggerAndTokenVersion.sql",
        "20260323_ZZC_AddCommerceHubDynamicCommissionRules.sql",
        "20260323_ZZD_AddCommerceHubCustomersAndOrderOwnership.sql",
        "20260323_ZZE_AddCommerceHubCustomerRefundRequests.sql",
        "20260323_ZZF_HardenCommerceHubRefundRequestSafety.sql",
        "20260324_ZZG_AddCommerceHubSuperAdminRefundControl.sql",
        "20260324_ZZH_AddCommerceHubRefundEvidenceSystem.sql",
        "20260324_ZZI_AddCommerceHubCustomerPayoutDetails.sql",
        "20260324_ZZJ_AddCommerceHubManualRefundExecution.sql",
        "20260324_ZZK_AddCommerceHubReturnManagementSystem.sql",
        "20260324_ZZL_AddCommerceHubFinancialLedger.sql",
        "20260324_ZZM_AddCommerceHubWalletQueryIndexes.sql",
        "20260324_ZZN_AddCommerceHubVendorFinancialControl.sql",
        "20260324_ZZO_HardenCommerceHubFinancialLedgerTraceability.sql",
        "20260325_ZZM_AddCommerceHubLedgerImmutabilityAndReturnSLA.sql",
        "20260325_ZZN_AddCommerceHubSettlementLedgerType.sql",
        "20260325_ZZP_HardenCommerceHubLedgerAuditSafety.sql",
        "20260325_ZZQ_AddCommerceHubVendorRepaymentWorkflow.sql",
        "20260325_ZZR_AddCommerceHubFinancialBuckets.sql",
        "20260325_ZZS_AddCommerceHubVendorPayoutMethods.sql",
        "20260326_ZZS_HardenCommerceHubInventoryConsistency.sql",
        "20260326_ZZT_AddCommerceHubFinancialSafetyGuards.sql",
        "20260326_ZZU_AddCommerceHubUserGovernance.sql",
        "20260326_ZZV_AddCommerceHubVendorVisibilityControl.sql",
        "20260327_ZZW_AddCommerceHubPromoCodes.sql",
        "20260327_ZZX_HardenCommerceHubFinancialDashboardAggregates.sql",
        "20260327_ZZY_AddCommerceHubFinancialReconciliationReports.sql",
        "20260327_ZZZ_AddHomepageSections.sql",
        "20260328_AAA_HardenHomepageSectionsDraftPublishing.sql",
        "20260329_AAB_AddCmsPages.sql",
        "20260329_AAC_AddMarketplaceCmsPages.sql",
        "20260329_AAD_AddStorefrontFooterAndSocial.sql",
        "20260329_AAE_AddMarketplaceFooterAndSocial.sql",
        "20260329_AAF_AddMarketplaceEventBackbone.sql"
    };
    private static readonly string[] LateSupplementalSqlMigrations =
    {
        "20260424170000_S32_AccountingOutboxProcessingLeases.sql",
        "20260424183000_S35_EventOrderingBoundaryHardening.sql",
        "20260426110000_S38_ReopenCriticalFinancialFailures.sql"
    };
    private static readonly Dictionary<string, int> SupplementalSqlMigrationOrder = EarlySupplementalSqlMigrations
        .Concat(LateSupplementalSqlMigrations)
        .Select((fileName, index) => new KeyValuePair<string, int>(fileName, index))
        .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    private static readonly string RunId = $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Environment.ProcessId}";
    private static readonly Regex InvalidCharacters = new("[^a-z0-9_]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _connectionString;
    private readonly string _adminConnectionString;
    private readonly string _databaseName;
    private bool _initialized;

    public ManagedTestDatabase(string key)
    {
        var baseConnection = new NpgsqlConnectionStringBuilder(TestConfiguration.GetRequiredConnectionString())
        {
            Pooling = false,
            IncludeErrorDetail = true,
            SslMode = SslMode.Disable,
            TrustServerCertificate = true
        };
        baseConnection["Timezone"] = "UTC";

        _databaseName = BuildDatabaseName(key);
        _connectionString = new NpgsqlConnectionStringBuilder(baseConnection.ConnectionString)
        {
            Database = _databaseName
        }.ConnectionString;

        _adminConnectionString = new NpgsqlConnectionStringBuilder(baseConnection.ConnectionString)
        {
            Database = "postgres"
        }.ConnectionString;
    }

    public void Acquire()
    {
        _gate.Wait();

        try
        {
            EnsureInitializedAsync().GetAwaiter().GetResult();
            ResetAsync().GetAwaiter().GetResult();
        }
        catch
        {
            _gate.Release();
            throw;
        }
    }

    public ValueTask ReleaseAsync()
    {
        _gate.Release();
        return ValueTask.CompletedTask;
    }

    public DbContextOptions<ApplicationDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_connectionString)
            .EnableDetailedErrors()
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return;
        }

        await CreateDatabaseIfMissingAsync();

        await using var context = new ApplicationDbContext(CreateOptions());
        await MigrateEfChainAsync(context, SupplementalSqlBaselineMigration);
        await ApplySupplementalSqlMigrationsAsync(EarlySupplementalSqlMigrations);
        await MigrateEfChainAsync(context);
        await ApplySupplementalSqlMigrationsAsync(LateSupplementalSqlMigrations);
        await NormalizeCriticalSchemaDriftAsync();

        _initialized = true;
    }

    private async Task NormalizeCriticalSchemaDriftAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = current_schema()
                      AND table_name = 'AccountingOutboxMessages'
                      AND column_name = 'CorrelationId'
                      AND data_type = 'uuid') THEN
                    ALTER TABLE "AccountingOutboxMessages"
                    ALTER COLUMN "CorrelationId" TYPE character varying(100)
                    USING "CorrelationId"::text;
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = current_schema()
                      AND table_name = 'ProductVariants') THEN
                    ALTER TABLE "ProductVariants"
                        ADD COLUMN IF NOT EXISTS "AttributeCombinationKey" character varying(80) NULL;
                    ALTER TABLE "ProductVariants"
                        ADD COLUMN IF NOT EXISTS "SemanticLifecycleState" integer NOT NULL DEFAULT 0;
                    ALTER TABLE "ProductVariants"
                        ADD COLUMN IF NOT EXISTS "SemanticReplacementVariantId" uuid NULL;
                    ALTER TABLE "ProductVariants"
                        ADD COLUMN IF NOT EXISTS "SemanticLineageRootVariantId" uuid NULL;
                    ALTER TABLE "ProductVariants"
                        ADD COLUMN IF NOT EXISTS "SemanticOperationId" uuid NULL;
                    ALTER TABLE "ProductVariants"
                        ADD COLUMN IF NOT EXISTS "SemanticOperationKind" character varying(64) NULL;
                    ALTER TABLE "ProductVariants"
                        ADD COLUMN IF NOT EXISTS "SemanticSupersededAtUtc" timestamp with time zone NULL;

                    DROP INDEX IF EXISTS "IX_ProductVariants_TenantId_ProductId_AttributeCombinationKey";
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProductVariants_TenantId_ProductId_AttributeCombinationKey"
                        ON "ProductVariants" ("TenantId", "ProductId", "AttributeCombinationKey")
                        WHERE "IsDeleted" = false AND "SemanticLifecycleState" = 0 AND "AttributeCombinationKey" IS NOT NULL;

                    CREATE INDEX IF NOT EXISTS "IX_ProductVariants_TenantId_SemanticLifecycleState"
                        ON "ProductVariants" ("TenantId", "SemanticLifecycleState");

                    DROP INDEX IF EXISTS "IX_ProductVariants_SKU";
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProductVariants_SKU"
                        ON "ProductVariants" ("TenantId", "SKU")
                        WHERE "IsDeleted" = false AND "SemanticLifecycleState" = 0 AND "SKU" IS NOT NULL;

                    DROP INDEX IF EXISTS "IX_ProductVariants_Barcode";
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProductVariants_Barcode"
                        ON "ProductVariants" ("TenantId", "Barcode")
                        WHERE "IsDeleted" = false AND "SemanticLifecycleState" = 0 AND "Barcode" IS NOT NULL;

                    DROP INDEX IF EXISTS "IX_ProductVariants_ProductId_IsDefault";
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProductVariants_ProductId_IsDefault"
                        ON "ProductVariants" ("TenantId", "ProductId", "IsDefault")
                        WHERE "IsDeleted" = false AND "SemanticLifecycleState" = 0 AND "IsDefault" = true;
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = current_schema()
                      AND table_name = 'StockSyncOutboxMessages') THEN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'StockSyncOutboxMessages'
                          AND column_name = 'WarehouseId') THEN
                        ALTER TABLE "StockSyncOutboxMessages"
                        ADD COLUMN "WarehouseId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'StockSyncOutboxMessages'
                          AND column_name = 'EventVersion') THEN
                        ALTER TABLE "StockSyncOutboxMessages"
                        ADD COLUMN "EventVersion" integer NOT NULL DEFAULT 1;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'StockSyncOutboxMessages'
                          AND column_name = 'ClaimedUntilUtc') THEN
                        ALTER TABLE "StockSyncOutboxMessages"
                        ADD COLUMN "ClaimedUntilUtc" timestamp with time zone NULL;
                    END IF;
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = current_schema()
                      AND table_name = 'MarketplaceEventOutboxMessages') THEN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'MarketplaceEventOutboxMessages'
                          AND column_name = 'AggregateRootId') THEN
                        ALTER TABLE "MarketplaceEventOutboxMessages"
                        ADD COLUMN "AggregateRootId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
                    END IF;
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = current_schema()
                      AND table_name = 'Promotions') THEN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'Promotions'
                          AND column_name = 'ProjectionMutationVersion') THEN
                        ALTER TABLE "Promotions"
                        ADD COLUMN "ProjectionMutationVersion" bigint NOT NULL DEFAULT 0;
                    END IF;
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = current_schema()
                      AND table_name = 'SalesInvoiceItems') THEN
                    ALTER TABLE "SalesInvoiceItems"
                        ADD COLUMN IF NOT EXISTS "CostPriceSnapshot" numeric(18,6) NULL;
                    ALTER TABLE "SalesInvoiceItems"
                        ADD COLUMN IF NOT EXISTS "CostAmountSnapshot" numeric(18,2) NULL;
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = current_schema()
                      AND table_name = 'ReturnItems') THEN
                    ALTER TABLE "ReturnItems"
                        ADD COLUMN IF NOT EXISTS "CostPriceSnapshot" numeric(18,6) NULL;
                    ALTER TABLE "ReturnItems"
                        ADD COLUMN IF NOT EXISTS "CostAmountSnapshot" numeric(18,2) NULL;
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = current_schema()
                      AND table_name = 'CommerceHubListings') THEN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'CommerceHubListings'
                          AND column_name = 'AttributeSchemaSnapshotJson') THEN
                        ALTER TABLE "CommerceHubListings"
                        ADD COLUMN "AttributeSchemaSnapshotJson" text NULL;
                    END IF;
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = current_schema()
                      AND table_name = 'CommerceHubCustomers') THEN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'CommerceHubCustomers'
                          AND column_name = 'TokensValidAfterUtc') THEN
                        ALTER TABLE "CommerceHubCustomers"
                        ADD COLUMN "TokensValidAfterUtc" timestamp with time zone NULL;
                    END IF;
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = current_schema()
                      AND table_name = 'CommerceHubMarketplaceOrders') THEN
                    ALTER TABLE "CommerceHubMarketplaceOrders"
                        ADD COLUMN IF NOT EXISTS "LastProjectionAttemptAt" timestamp with time zone NULL;
                    ALTER TABLE "CommerceHubMarketplaceOrders"
                        ADD COLUMN IF NOT EXISTS "ProjectionAttemptCount" integer NOT NULL DEFAULT 0;
                    ALTER TABLE "CommerceHubMarketplaceOrders"
                        ADD COLUMN IF NOT EXISTS "IsProjectionTerminalFailure" boolean NOT NULL DEFAULT false;
                    ALTER TABLE "CommerceHubMarketplaceOrders"
                        ADD COLUMN IF NOT EXISTS "ProjectionMutationVersion" bigint NOT NULL DEFAULT 1;
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = current_schema()
                      AND table_name = 'CommerceHubSubOrders') THEN
                    ALTER TABLE "CommerceHubSubOrders"
                        ADD COLUMN IF NOT EXISTS "ProjectionMutationVersion" bigint NOT NULL DEFAULT 1;
                END IF;

                IF NOT EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = current_schema()
                      AND table_name = 'MarketplaceProjectionOutboxMessages') THEN
                    CREATE TABLE "MarketplaceProjectionOutboxMessages"
                    (
                        "Id" uuid NOT NULL,
                        "EventId" uuid NOT NULL,
                        "ConsumerName" character varying(200) NOT NULL,
                        "EventType" character varying(200) NOT NULL,
                        "ClrType" character varying(1000) NOT NULL,
                        "EntityId" uuid NOT NULL,
                        "TenantId" uuid NULL,
                        "CorrelationId" character varying(200) NOT NULL,
                        "EventVersion" integer NOT NULL,
                        "OccurredAtUtc" timestamp with time zone NOT NULL,
                        "PayloadJson" text NOT NULL,
                        "ProcessedAtUtc" timestamp with time zone NULL,
                        "ClaimedUntilUtc" timestamp with time zone NULL,
                        "AttemptCount" integer NOT NULL DEFAULT 0,
                        "LastError" character varying(2000) NULL,
                        "NextRetryAtUtc" timestamp with time zone NULL,
                        "IsFailed" boolean NOT NULL DEFAULT false,
                        "CreatedAt" timestamp with time zone NOT NULL,
                        "UpdatedAt" timestamp with time zone NULL,
                        "CreatedBy" uuid NULL,
                        "UpdatedBy" uuid NULL,
                        "IsDeleted" boolean NOT NULL DEFAULT false,
                        CONSTRAINT "PK_MarketplaceProjectionOutboxMessages" PRIMARY KEY ("Id")
                    );
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = current_schema()
                      AND table_name = 'MarketplaceProjectionOutboxMessages') THEN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'MarketplaceProjectionOutboxMessages'
                          AND column_name = 'ClrType') THEN
                        ALTER TABLE "MarketplaceProjectionOutboxMessages"
                        ADD COLUMN "ClrType" character varying(1000) NOT NULL DEFAULT '';
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'MarketplaceProjectionOutboxMessages'
                          AND column_name = 'PayloadJson') THEN
                        ALTER TABLE "MarketplaceProjectionOutboxMessages"
                        ADD COLUMN "PayloadJson" text NOT NULL DEFAULT '{}';
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'MarketplaceProjectionOutboxMessages'
                          AND column_name = 'EventVersion') THEN
                        ALTER TABLE "MarketplaceProjectionOutboxMessages"
                        ADD COLUMN "EventVersion" integer NOT NULL DEFAULT 1;
                    END IF;

                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = current_schema()
                          AND table_name = 'MarketplaceProjectionOutboxMessages'
                          AND column_name = 'ClaimedUntilUtc') THEN
                        ALTER TABLE "MarketplaceProjectionOutboxMessages"
                        ADD COLUMN "ClaimedUntilUtc" timestamp with time zone NULL;
                    END IF;
                END IF;

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_MarketplaceProjectionOutbox_Idempotency"
                    ON "MarketplaceProjectionOutboxMessages" ("EventId", "ConsumerName", "EventType")
                    WHERE "IsDeleted" = false;

                CREATE INDEX IF NOT EXISTS "IX_MarketplaceProjectionOutbox_ProcessedAtUtc"
                    ON "MarketplaceProjectionOutboxMessages" ("ProcessedAtUtc");

                CREATE INDEX IF NOT EXISTS "IX_MarketplaceProjectionOutbox_TenantId_EntityId"
                    ON "MarketplaceProjectionOutboxMessages" ("TenantId", "EntityId");

                IF NOT EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = current_schema()
                      AND table_name = 'ProductRequestIdempotency') THEN
                    CREATE TABLE "ProductRequestIdempotency"
                    (
                        "Id" uuid NOT NULL,
                        "TenantId" uuid NOT NULL,
                        "Key" character varying(200) NOT NULL,
                        "Operation" character varying(32) NOT NULL,
                        "ProductId" uuid NOT NULL,
                        "ProductVariantId" uuid NULL,
                        "RequestFingerprint" character varying(64) NOT NULL,
                        "ResponseStatusCode" integer NULL,
                        "ResponseJson" text NULL,
                        "CompletedAtUtc" timestamp with time zone NULL,
                        "CreatedAt" timestamp with time zone NOT NULL,
                        "UpdatedAt" timestamp with time zone NULL,
                        "CreatedBy" uuid NULL,
                        "UpdatedBy" uuid NULL,
                        "IsDeleted" boolean NOT NULL DEFAULT false,
                        CONSTRAINT "PK_ProductRequestIdempotency" PRIMARY KEY ("Id")
                    );
                END IF;

                CREATE UNIQUE INDEX IF NOT EXISTS "IX_ProductRequestIdempotency_TenantId_Key_Operation"
                    ON "ProductRequestIdempotency" ("TenantId", "Key", "Operation");

                CREATE INDEX IF NOT EXISTS "IX_ProductRequestIdempotency_TenantId_ProductId"
                    ON "ProductRequestIdempotency" ("TenantId", "ProductId");

                CREATE INDEX IF NOT EXISTS "IX_ProductRequestIdempotency_TenantId_ProductVariantId"
                    ON "ProductRequestIdempotency" ("TenantId", "ProductVariantId");
            END
            $$;
            """;

        await command.ExecuteNonQueryAsync();
    }

    private static async Task MigrateEfChainAsync(ApplicationDbContext context, string? targetMigration = null)
    {
        var applied = new HashSet<string>(
            await context.Database.GetAppliedMigrationsAsync(),
            StringComparer.OrdinalIgnoreCase);

        var allMigrations = context.GetService<IMigrationsAssembly>()
            .Migrations
            .Keys
            .ToList();
        var pending = allMigrations
            .Where(migration => !applied.Contains(migration))
            .ToList();

        if (pending.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(targetMigration))
        {
            if (applied.Contains(targetMigration))
            {
                return;
            }

            var targetIndex = pending.FindIndex(migration => string.Equals(migration, targetMigration, StringComparison.OrdinalIgnoreCase));
            if (targetIndex < 0)
            {
                throw new InvalidOperationException(
                    $"Target migration '{targetMigration}' was not found in pending EF migrations. " +
                    $"First pending migration: '{pending[0]}'. Last pending migration: '{pending[^1]}'.");
            }

            pending = pending.Take(targetIndex + 1).ToList();
        }

        var migrator = context.GetService<IMigrator>();
        foreach (var migration in pending)
        {
            try
            {
                await migrator.MigrateAsync(migration);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed while applying EF migration '{migration}'.",
                    ex);
            }
        }
    }

    private async Task ApplySupplementalSqlMigrationsAsync(IReadOnlyCollection<string> migrationFileNames)
    {
        if (migrationFileNames.Count == 0)
        {
            return;
        }

        var migrationsRoot = Path.Combine(
            TestConfiguration.GetSolutionRoot(),
            "PointOfSale.Infrastructure",
            "Migrations");
        var candidateDirectories = new[]
        {
            migrationsRoot,
            Path.Combine(migrationsRoot, "Sql")
        };

        var selectedMigrations = new HashSet<string>(migrationFileNames, StringComparer.OrdinalIgnoreCase);
        var scripts = candidateDirectories
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.sql", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => ShouldApplySupplementalSqlMigration(Path.GetFileName(path), selectedMigrations))
            .OrderBy(path =>
            {
                var fileName = Path.GetFileName(path);
                if (SupplementalSqlMigrationOrder.TryGetValue(fileName, out var order))
                {
                    return order;
                }
                // Defensive fallback: if a file passes the filter but isn't in the order dictionary,
                // place it at the end to avoid KeyNotFoundException
                throw new InvalidOperationException(
                    $"SQL migration file '{fileName}' passed the filter but is not in SupplementalSqlMigrationOrder. " +
                    "This indicates a logic error in ShouldApplySupplementalSqlMigration.");
            })
            .ToArray();

        if (scripts.Length == 0)
        {
            return;
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        foreach (var scriptPath in scripts)
        {
            var sql = await File.ReadAllTextAsync(scriptPath);
            if (string.IsNullOrWhiteSpace(sql))
            {
                continue;
            }

            try
            {
                await using var command = new NpgsqlCommand(sql, connection)
                {
                    CommandTimeout = 120
                };

                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to apply supplemental SQL migration '{Path.GetFileName(scriptPath)}'.",
                    ex);
            }
        }
    }

    private async Task CreateDatabaseIfMissingAsync()
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();

        await using var existsCommand = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @databaseName;",
            connection);
        existsCommand.Parameters.AddWithValue("databaseName", _databaseName);

        var exists = await existsCommand.ExecuteScalarAsync();
        if (exists is not null)
        {
            return;
        }

        var quotedDatabaseName = QuoteIdentifier(_databaseName);
        await using var createCommand = new NpgsqlCommand($"CREATE DATABASE {quotedDatabaseName};", connection);
        await createCommand.ExecuteNonQueryAsync();
    }

    private async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var listTablesCommand = new NpgsqlCommand(
            """
            SELECT string_agg(format('%I.%I', schemaname, tablename), ',')
            FROM pg_tables
            WHERE schemaname = 'public'
              AND tablename <> '__EFMigrationsHistory'
              AND schemaname IS NOT NULL AND schemaname <> ''
              AND tablename IS NOT NULL AND tablename <> '';
            """,
            connection);

        var tables = await listTablesCommand.ExecuteScalarAsync() as string;
        if (string.IsNullOrWhiteSpace(tables))
        {
            return;
        }

        // Validate that no empty identifiers were produced
        if (tables.Contains("\"\""))
        {
            throw new InvalidOperationException(
                $"ResetAsync detected empty identifier in table list. This indicates invalid schema or table names. " +
                $"Tables list: {tables}");
        }

        await using var truncateCommand = new NpgsqlCommand(
            $"TRUNCATE TABLE {tables} RESTART IDENTITY CASCADE;",
            connection);
        await truncateCommand.ExecuteNonQueryAsync();

        await SeedReferenceDataAsync(connection);
    }

    private static async Task SeedReferenceDataAsync(NpgsqlConnection connection)
    {
        // Currency data synchronized with production migration: 20260426130000_S40_AddCurrencyReferenceTable.sql
        // This ensures tests mirror runtime data exactly
        await using var currencyCommand = new NpgsqlCommand(
            """
            INSERT INTO "Currencies" ("Code", "Name", "Symbol", "DecimalPlaces") VALUES
            -- Major World Currencies
            ('USD', 'US Dollar', '$', 2),
            ('EUR', 'Euro', '€', 2),
            ('GBP', 'British Pound', '£', 2),
            ('JPY', 'Japanese Yen', '¥', 0),
            ('CHF', 'Swiss Franc', 'CHF', 2),
            ('CAD', 'Canadian Dollar', 'C$', 2),
            ('AUD', 'Australian Dollar', 'A$', 2),
            ('NZD', 'New Zealand Dollar', 'NZ$', 2),
            
            -- Middle East & North Africa
            ('EGP', 'Egyptian Pound', 'E£', 2),
            ('SAR', 'Saudi Riyal', 'SR', 2),
            ('AED', 'UAE Dirham', 'د.إ', 2),
            ('KWD', 'Kuwaiti Dinar', 'KD', 3),
            ('QAR', 'Qatari Riyal', 'QR', 2),
            ('OMR', 'Omani Rial', 'OMR', 3),
            ('BHD', 'Bahraini Dinar', 'BD', 3),
            ('JOD', 'Jordanian Dinar', 'JD', 3),
            ('LBP', 'Lebanese Pound', 'LL', 2),
            ('IQD', 'Iraqi Dinar', 'IQD', 3),
            ('SYP', 'Syrian Pound', 'SYP', 2),
            ('YER', 'Yemeni Rial', 'YER', 2),
            ('MAD', 'Moroccan Dirham', 'MAD', 2),
            ('TND', 'Tunisian Dinar', 'TND', 3),
            ('DZD', 'Algerian Dinar', 'DZD', 2),
            ('LYD', 'Libyan Dinar', 'LYD', 3),
            ('SDG', 'Sudanese Pound', 'SDG', 2),
            
            -- Asia Pacific
            ('CNY', 'Chinese Yuan', '¥', 2),
            ('INR', 'Indian Rupee', '₹', 2),
            ('KRW', 'South Korean Won', '₩', 0),
            ('SGD', 'Singapore Dollar', 'S$', 2),
            ('HKD', 'Hong Kong Dollar', 'HK$', 2),
            ('TWD', 'Taiwan Dollar', 'NT$', 2),
            ('THB', 'Thai Baht', '฿', 2),
            ('MYR', 'Malaysian Ringgit', 'RM', 2),
            ('IDR', 'Indonesian Rupiah', 'Rp', 2),
            ('PHP', 'Philippine Peso', '₱', 2),
            ('VND', 'Vietnamese Dong', '₫', 0),
            ('PKR', 'Pakistani Rupee', 'Rs', 2),
            ('BDT', 'Bangladeshi Taka', '৳', 2),
            ('LKR', 'Sri Lankan Rupee', 'Rs', 2),
            
            -- Europe
            ('SEK', 'Swedish Krona', 'kr', 2),
            ('NOK', 'Norwegian Krone', 'kr', 2),
            ('DKK', 'Danish Krone', 'kr', 2),
            ('PLN', 'Polish Zloty', 'zł', 2),
            ('CZK', 'Czech Koruna', 'Kč', 2),
            ('HUF', 'Hungarian Forint', 'Ft', 2),
            ('RON', 'Romanian Leu', 'lei', 2),
            ('BGN', 'Bulgarian Lev', 'лв', 2),
            ('HRK', 'Croatian Kuna', 'kn', 2),
            ('RUB', 'Russian Ruble', '₽', 2),
            ('UAH', 'Ukrainian Hryvnia', '₴', 2),
            ('TRY', 'Turkish Lira', '₺', 2),
            
            -- Americas
            ('MXN', 'Mexican Peso', 'Mex$', 2),
            ('BRL', 'Brazilian Real', 'R$', 2),
            ('ARS', 'Argentine Peso', 'AR$', 2),
            ('CLP', 'Chilean Peso', 'CLP', 0),
            ('COP', 'Colombian Peso', 'COL$', 2),
            ('PEN', 'Peruvian Sol', 'S/', 2),
            ('VES', 'Venezuelan Bolívar', 'Bs.', 2),
            ('UYU', 'Uruguayan Peso', '$U', 2),
            
            -- Africa
            ('ZAR', 'South African Rand', 'R', 2),
            ('NGN', 'Nigerian Naira', '₦', 2),
            ('KES', 'Kenyan Shilling', 'KSh', 2),
            ('GHS', 'Ghanaian Cedi', 'GH₵', 2),
            ('TZS', 'Tanzanian Shilling', 'TSh', 2),
            ('UGX', 'Ugandan Shilling', 'USh', 0),
            ('ETB', 'Ethiopian Birr', 'Br', 2),
            ('XOF', 'West African CFA Franc', 'CFA', 0),
            ('XAF', 'Central African CFA Franc', 'FCFA', 0)
            ON CONFLICT ("Code") DO UPDATE
            SET
                "Name" = EXCLUDED."Name",
                "Symbol" = EXCLUDED."Symbol",
                "DecimalPlaces" = EXCLUDED."DecimalPlaces";
            """,
            connection);
        await currencyCommand.ExecuteNonQueryAsync();
    }

    private static string BuildDatabaseName(string key)
    {
        const int maxDatabaseNameLength = 63;
        const int maxPrefixLength = 20;
        const int minimumKeyStemLength = 8;

        var prefix = NormalizeDatabaseComponent(TestConfiguration.GetDatabasePrefix(), "pointofsale_tests");
        if (prefix.Length > maxPrefixLength)
        {
            prefix = prefix[..maxPrefixLength].TrimEnd('_');
        }

        var keyComponent = NormalizeDatabaseComponent(key, "test");
        var keyHash = TestConfiguration.BuildDeterministicSuffix(keyComponent);
        var runHash = TestConfiguration.BuildDeterministicSuffix(RunId);
        var reservedLength = keyHash.Length + runHash.Length + 3; // separators

        var availableKeyStemLength = maxDatabaseNameLength - prefix.Length - reservedLength;
        if (availableKeyStemLength < minimumKeyStemLength)
        {
            var maxSafePrefixLength = Math.Max(4, maxDatabaseNameLength - reservedLength - minimumKeyStemLength);
            prefix = prefix[..Math.Min(prefix.Length, maxSafePrefixLength)].TrimEnd('_');
            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = "tests";
            }

            availableKeyStemLength = maxDatabaseNameLength - prefix.Length - reservedLength;
        }

        var keyStem = keyComponent.Length <= availableKeyStemLength
            ? keyComponent
            : keyComponent[..availableKeyStemLength].TrimEnd('_');

        if (string.IsNullOrWhiteSpace(keyStem))
        {
            keyStem = "test";
        }

        return $"{prefix}_{keyStem}_{keyHash}_{runHash}";
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException(
                "Cannot quote an empty or whitespace-only identifier. " +
                "Database identifiers must be non-empty strings.",
                nameof(identifier));
        }

        return $"\"{identifier.Replace("\"", "\"\"")}\"";
    }

    private static string NormalizeDatabaseComponent(string value, string fallback)
    {
        var normalized = InvalidCharacters.Replace(value.ToLowerInvariant(), "_").Trim('_');
        return string.IsNullOrWhiteSpace(normalized)
            ? fallback
            : normalized;
    }

    private static bool ShouldApplySupplementalSqlMigration(string? fileName, IReadOnlySet<string> selectedMigrations)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (fileName.Contains("_Generated", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("PgAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return selectedMigrations.Contains(fileName);
    }
}
