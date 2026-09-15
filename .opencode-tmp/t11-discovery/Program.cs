using System.Data;
using System.Text.Json;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? ReadLocalConnectionString();
var outputPath = Environment.GetEnvironmentVariable("T11_EVIDENCE_PATH")
    ?? throw new InvalidOperationException("T11_EVIDENCE_PATH is required.");

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
{
    await readOnly.ExecuteNonQueryAsync();
}

var evidence = new Dictionary<string, JsonElement>();
await CaptureAsync("database", """
    SELECT jsonb_build_object(
        'database', current_database(),
        'serverTime', clock_timestamp(),
        'transactionReadOnly', current_setting('transaction_read_only'),
        'isolation', current_setting('transaction_isolation'))
    """);
await CaptureAsync("tenants", """
    SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q."Name"), '[]'::jsonb)
    FROM (
        SELECT "Id", "Name", "Currency", "IsActive", "Status", "DeletedAtUtc", "CreatedAtUtc"
        FROM "Tenants"
        WHERE "IsActive" AND "DeletedAtUtc" IS NULL
    ) q
    """);
await CaptureAsync("catalogSummary", """
    WITH selected_tenants AS (
        SELECT "Id", "Name"
        FROM "Tenants"
        WHERE "IsActive" AND "DeletedAtUtc" IS NULL
    )
    SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q."Tenant"), '[]'::jsonb)
    FROM (
        SELECT t."Name" AS "Tenant", t."Id" AS "TenantId",
               COUNT(a."Id") FILTER (WHERE a."IsActive" AND NOT a."IsDeleted") AS "ActiveAccounts",
               COUNT(ta."Id") FILTER (WHERE ta."IsActive" AND NOT ta."IsDeleted") AS "ActiveTreasuryMappings",
               COUNT(ta."Id") FILTER (WHERE ta."IsActive" AND NOT ta."IsDeleted" AND ta."Type" = 1) AS "CashboxMappings",
               COUNT(ta."Id") FILTER (WHERE ta."IsActive" AND NOT ta."IsDeleted" AND ta."Type" = 2) AS "BankMappings",
               COUNT(ta."Id") FILTER (WHERE ta."IsActive" AND NOT ta."IsDeleted" AND ta."Type" = 3) AS "ClearingMappings",
               COUNT(a."Id") FILTER (WHERE a."IsActive" AND NOT a."IsDeleted" AND a."IsSupplierCardFunding") AS "SupplierCardAssets",
               COALESCE(SUM(a."CurrentBalance") FILTER (
                   WHERE a."IsActive" AND NOT a."IsDeleted"
                     AND ta."IsActive" AND NOT ta."IsDeleted" AND ta."Type" IN (1, 2)), 0) AS "CashEquivalentBalance",
               COALESCE(SUM(a."CurrentBalance") FILTER (
                   WHERE a."IsActive" AND NOT a."IsDeleted"
                     AND ta."IsActive" AND NOT ta."IsDeleted" AND ta."Type" = 3), 0) AS "ClearingBalance",
               COALESCE(SUM(a."CurrentBalance") FILTER (
                   WHERE a."IsActive" AND NOT a."IsDeleted" AND a."IsSupplierCardFunding"), 0) AS "SupplierCardBalance"
        FROM selected_tenants t
        LEFT JOIN "LedgerAccounts" a ON a."TenantId" = t."Id"
        LEFT JOIN "TreasuryAccounts" ta
          ON ta."TenantId" = a."TenantId" AND ta."AccountId" = a."Id"
        GROUP BY t."Name", t."Id"
    ) q
    """);
await CaptureAsync("treasuryCatalog", """
    WITH selected_tenants AS (
        SELECT "Id", "Name"
        FROM "Tenants"
        WHERE "IsActive" AND "DeletedAtUtc" IS NULL
    )
    SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q."Tenant", q."AccountCode"), '[]'::jsonb)
    FROM (
        SELECT t."Name" AS "Tenant", a."AccountCode", a."Name", a."NameAr", a."Description",
               a."CurrentBalance", a."IsClearing", a."IsSupplierCardFunding",
               p."AccountCode" AS "ParentCode", ta."Type" AS "TreasuryType",
               ta."Name" AS "TreasuryName", ta."NameAr" AS "TreasuryNameAr",
               ta."IsActive" AS "TreasuryIsActive", ta."IsDeleted" AS "TreasuryIsDeleted"
        FROM selected_tenants t
        JOIN "LedgerAccounts" a ON a."TenantId" = t."Id"
        LEFT JOIN "LedgerAccounts" p
          ON p."TenantId" = a."TenantId" AND p."Id" = a."ParentAccountId"
        LEFT JOIN "TreasuryAccounts" ta
          ON ta."TenantId" = a."TenantId" AND ta."AccountId" = a."Id"
        WHERE a."IsActive" AND NOT a."IsDeleted" AND a."AccountType" = 1
          AND (ta."Id" IS NOT NULL OR a."IsSupplierCardFunding" OR a."AccountCode" IN ('1000', '1020', '1040', '1100'))
    ) q
    """);
await CaptureAsync("accounts", """
    WITH selected_tenants AS (
        SELECT "Id", "Name"
        FROM "Tenants"
        WHERE "IsActive" AND "DeletedAtUtc" IS NULL
    ), ledger AS (
        SELECT le."TenantId", le."AccountId",
               COUNT(*) AS "TransactionCount",
               SUM(le."DebitAmount") AS "TotalDebits",
               SUM(le."CreditAmount") AS "TotalCredits",
               MAX(je."JournalDate") AS "LastJournalDate"
        FROM "LedgerLines" le
        JOIN "LedgerJournalEntries" je
          ON je."TenantId" = le."TenantId" AND je."Id" = le."JournalEntryId"
        WHERE je."Status" IN (1, 2)
        GROUP BY le."TenantId", le."AccountId"
    )
    SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q."Tenant", q."AccountCode"), '[]'::jsonb)
    FROM (
        SELECT t."Name" AS "Tenant", a."TenantId", a."Id" AS "AccountId",
               a."AccountCode", a."Name", a."NameAr", a."Description", a."AccountType",
               p."AccountCode" AS "ParentCode", p."Name" AS "ParentName",
               CASE WHEN a."AccountType" IN (1, 5, 6) THEN 'Debit' ELSE 'Credit' END AS "NormalBalance",
               a."CurrentBalance" AS "CachedBalance",
               CASE WHEN a."AccountType" IN (1, 5, 6)
                    THEN COALESCE(l."TotalDebits", 0) - COALESCE(l."TotalCredits", 0)
                    ELSE COALESCE(l."TotalCredits", 0) - COALESCE(l."TotalDebits", 0)
               END AS "LedgerBalance",
               a."IsSystem", a."IsActive", a."IsDeleted", a."PreventNegative",
               a."IsClearing", a."IsSupplierCardFunding", a."ClearingSourceType",
               a."ClearingSourceCode", a."ExpectedSettlementDays",
               ta."Id" AS "TreasuryMappingId", ta."Type" AS "TreasuryType",
               ta."Name" AS "TreasuryName", ta."NameAr" AS "TreasuryNameAr",
               ta."IsActive" AS "TreasuryIsActive", ta."IsDeleted" AS "TreasuryIsDeleted",
               ta."IsDefault" AS "TreasuryIsDefault", ta."ReferenceCode", ta."Location",
               COALESCE(l."TransactionCount", 0) AS "TransactionCount",
               COALESCE(l."TotalDebits", 0) AS "TotalDebits",
               COALESCE(l."TotalCredits", 0) AS "TotalCredits",
               l."LastJournalDate",
               COALESCE(a."CurrentBalance" - (
                   CASE WHEN a."AccountType" IN (1, 5, 6)
                        THEN COALESCE(l."TotalDebits", 0) - COALESCE(l."TotalCredits", 0)
                        ELSE COALESCE(l."TotalCredits", 0) - COALESCE(l."TotalDebits", 0)
                   END), 0) AS "CacheVariance"
        FROM selected_tenants t
        JOIN "LedgerAccounts" a ON a."TenantId" = t."Id"
        LEFT JOIN "LedgerAccounts" p
          ON p."TenantId" = a."TenantId" AND p."Id" = a."ParentAccountId"
        LEFT JOIN "TreasuryAccounts" ta
          ON ta."TenantId" = a."TenantId" AND ta."AccountId" = a."Id"
        LEFT JOIN ledger l
          ON l."TenantId" = a."TenantId" AND l."AccountId" = a."Id"
    ) q
    """);
await CaptureAsync("recentUsage", """
    WITH selected_tenants AS (
        SELECT "Id", "Name"
        FROM "Tenants"
        WHERE "IsActive" AND "DeletedAtUtc" IS NULL
    ), ranked AS (
        SELECT t."Name" AS "Tenant", a."AccountCode", a."Name" AS "AccountName",
               je."Id" AS "JournalId", je."JournalNumber", je."JournalDate", je."Status",
               je."SourceType", je."SourceReference", je."Description" AS "JournalDescription",
               le."DebitAmount", le."CreditAmount", le."BalanceAfter", le."LineDescription",
               ROW_NUMBER() OVER (
                   PARTITION BY le."TenantId", le."AccountId"
                   ORDER BY je."JournalDate" DESC, je."PostedAtUtc" DESC NULLS LAST, le."LineNumber" DESC) AS rn
        FROM selected_tenants t
        JOIN "LedgerAccounts" a ON a."TenantId" = t."Id"
        JOIN "LedgerLines" le ON le."TenantId" = a."TenantId" AND le."AccountId" = a."Id"
        JOIN "LedgerJournalEntries" je ON je."TenantId" = le."TenantId" AND je."Id" = le."JournalEntryId"
        WHERE je."Status" IN (1, 2)
    )
    SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q."Tenant", q."AccountCode", q.rn), '[]'::jsonb)
    FROM (SELECT * FROM ranked WHERE rn <= 5) q
    """);
await CaptureAsync("paymentMethods", """
    WITH selected_tenants AS (
        SELECT "Id", "Name" FROM "Tenants"
        WHERE "IsActive" AND "DeletedAtUtc" IS NULL
    )
    SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q."Tenant", q."Code"), '[]'::jsonb)
    FROM (
        SELECT t."Name" AS "Tenant", pm."Code", pm."Name",
               CASE WHEN pm."ClearingAccountId" IS NOT NULL THEN 'Deferred' ELSE 'Immediate' END AS "SettlementType",
               target."AccountCode" AS "TargetAccountCode",
               clearing."AccountCode" AS "ClearingAccountCode",
               fee."AccountCode" AS "FeeAccountCode",
               pm."IsActive", pm."IsDeleted", pm."IsSystem"
        FROM selected_tenants t
        JOIN "PaymentMethods" pm ON pm."TenantId" = t."Id"
        LEFT JOIN "LedgerAccounts" target ON target."TenantId" = pm."TenantId" AND target."Id" = pm."TargetAccountId"
        LEFT JOIN "LedgerAccounts" clearing ON clearing."TenantId" = pm."TenantId" AND clearing."Id" = pm."ClearingAccountId"
        LEFT JOIN "LedgerAccounts" fee ON fee."TenantId" = pm."TenantId" AND fee."Id" = pm."FeesAccountId"
    ) q
    """);
await CaptureAsync("supplierCardInstruments", """
    WITH selected_tenants AS (
        SELECT "Id", "Name" FROM "Tenants"
        WHERE "IsActive" AND "DeletedAtUtc" IS NULL
    )
    SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q."Tenant", q."CreatedAt"), '[]'::jsonb)
    FROM (
        SELECT t."Name" AS "Tenant", i."Id", i."DisplayName", i."InstrumentType", i."Last4",
               asset."Id" AS "LedgerAccountId", asset."AccountCode" AS "LedgerAccountCode",
               asset."Name" AS "LedgerAccountName", asset."CurrentBalance" AS "LedgerAccountCachedBalance",
               asset."IsSupplierCardFunding", asset."PreventNegative", asset."IsClearing",
               source_ta."Id" AS "FundingSourceTreasuryAccountId", source_ta."Type" AS "FundingSourceTreasuryType",
               source."AccountCode" AS "FundingSourceAccountCode", i."AuthorityVersion",
               i."IsActive", i."IsDeleted", i."CreatedAt"
        FROM selected_tenants t
        JOIN "SupplierCardFundingInstruments" i ON i."TenantId" = t."Id"
        JOIN "LedgerAccounts" asset ON asset."TenantId" = i."TenantId" AND asset."Id" = i."LedgerAccountId"
        JOIN "TreasuryAccounts" source_ta ON source_ta."TenantId" = i."TenantId" AND source_ta."Id" = i."FundingSourceTreasuryAccountId"
        JOIN "LedgerAccounts" source ON source."TenantId" = source_ta."TenantId" AND source."Id" = source_ta."AccountId"
    ) q
    """);
await CaptureAsync("legacyFinanceCounts", """
    WITH selected_tenants AS (
        SELECT "Id", "Name" FROM "Tenants"
        WHERE "IsActive" AND "DeletedAtUtc" IS NULL
    )
    SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q."Tenant"), '[]'::jsonb)
    FROM (
        SELECT t."Name" AS "Tenant",
               (SELECT COUNT(*) FROM "FinancialAccounts" f WHERE f."TenantId" = t."Id") AS "FinancialAccounts",
               (SELECT COUNT(*) FROM "FinancialTransactions" f WHERE f."TenantId" = t."Id") AS "FinancialTransactions",
               (SELECT COUNT(*) FROM "VisaTransactions" v WHERE v."TenantId" = t."Id") AS "VisaTransactions",
               (SELECT COUNT(*) FROM "TreasuryTransactions" x WHERE x."TenantId" = t."Id") AS "TreasuryTransactions"
        FROM selected_tenants t
    ) q
    """);
await CaptureAsync("legacyAccounts", """
    WITH selected_tenants AS (
        SELECT "Id", "Name" FROM "Tenants"
        WHERE "IsActive" AND "DeletedAtUtc" IS NULL
    )
    SELECT COALESCE(jsonb_agg(to_jsonb(q) ORDER BY q."Tenant", q."AccountCode"), '[]'::jsonb)
    FROM (
        SELECT t."Name" AS "Tenant", f.*
        FROM selected_tenants t
        JOIN "FinancialAccounts" f ON f."TenantId" = t."Id"
    ) q
    """);

await transaction.RollbackAsync();
await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(evidence, new JsonSerializerOptions
{
    WriteIndented = true
}));
Console.WriteLine(outputPath);

async Task CaptureAsync(string key, string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    var json = (string?)await command.ExecuteScalarAsync() ?? "null";
    using var document = JsonDocument.Parse(json);
    evidence[key] = document.RootElement.Clone();
}

static string ReadLocalConnectionString()
{
    var path = Path.Combine(
        Directory.GetCurrentDirectory(),
        "PointOfSale.API",
        "appsettings.Development.local.json");
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    return document.RootElement
        .GetProperty("ConnectionStrings")
        .GetProperty("DefaultConnection")
        .GetString()
        ?? throw new InvalidOperationException("DefaultConnection is not configured.");
}
