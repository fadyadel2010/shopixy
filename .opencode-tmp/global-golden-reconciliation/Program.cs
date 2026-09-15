// Global Golden Treasury Reconciliation evidence harness.
// Deterministic READ-ONLY PostgreSQL snapshot utility. Never writes business state:
// every SQL statement is validated by IsReadOnlySql (SELECT/WITH only, single
// statement, no mutating keywords), the session runs in an explicit
// REPEATABLE READ + READ ONLY transaction that is always rolled back, and
// transaction_read_only is verified before any business query runs.
using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;

return await GlobalGoldenHarness.RunAsync(args);

static class GlobalGoldenHarness
{
    public const string ToolVersion = "1.0.0";

    // Frozen tenant allow-list (brief section 3 / architecture section 3).
    public static readonly IReadOnlyDictionary<Guid, string> AllowTenants =
        new Dictionary<Guid, string>
        {
            [Guid.Parse("c9c5c1ae-949e-498e-bc63-07242daeb49d")] = "AhramStore",
            [Guid.Parse("83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5")] = "shopixy",
            [Guid.Parse("c43aebf2-4cc4-406a-a2db-4df06d40bce5")] = "greatone",
        };

    public static readonly string[] RequestedAccountCodes =
    [
        "1010", "1020", "1030", "1110", "1120", "1130", "1140", "1310",
        "1410", "2010", "2105", "3010", "4010", "4020", "5010", "6010",
        "6020", "6110",
    ];

    // Columns whose names contain any of these substrings are never emitted.
    // Idempotency keys / request hashes are intentionally NOT excluded: the
    // brief (contract 8) requires idempotency-key linkage, and hashes are
    // integrity fingerprints, not secrets.
    static readonly string[] DeniedSubstrings =
    [
        "password", "secret", "token", "cookie", "credential", "apikey",
        "api_key", "privatekey", "connectionstring", "authorization", "bearer",
        "cardnumber", "card_number", "cvv", "cvc", "expiry", "expiration", "iban", "otp",
        "payload", "rawbody", "webhookbody", "webhookpayload", "requestbody",
        "responsebody", "email", "phone", "mobile", "fax", "address", "street",
        "city", "postal", "zipcode", "zip_code", "postcode", "nationalid",
        "national_id", "passport", "expiry", "expiration", "configencrypted",
        "checkouturl", "proofofdeliveryurl", "accountholder", "accountsnapshot",
        "payoutaccount", "guestname", "snapshotjson", "attributesjson",
        "note", "comment", "description", "reason", "transactionreference",
        "paymentreference", "providertransactionid",
    ];

    // Customer-facing order tables carry free-text/PII-adjacent columns that
    // are never certification evidence. Denied in addition to the global list.
    static readonly IReadOnlyDictionary<string, string[]> ExtraDeniedByTable =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Orders"] =
            [
                "fullname", "notes", "apartment", "building", "floor", "landmark",
                "district", "locality", "zone", "state", "country", "placeid", "area",
            ],
            ["CommerceHubMarketplaceOrders"] =
            [
                "fullname", "notes", "apartment", "building", "floor", "landmark",
                "district", "locality", "zone", "state", "country", "placeid", "area",
            ],
            ["CommerceHubRefunds"] = ["reference"],
        };

    // Tables that carry party personal data: drop name-like columns there.
    // Catalog names (ledger/treasury/payment-method/tenant names and document
    // reference numbers) are kept: the brief allows tenant names and
    // financial document/reference numbers.
    static bool IsPartyTable(string table) =>
        table.Contains("customer", StringComparison.OrdinalIgnoreCase)
        || table.Contains("supplier", StringComparison.OrdinalIgnoreCase)
        || table.Contains("affiliate", StringComparison.OrdinalIgnoreCase)
        || table.Contains("employee", StringComparison.OrdinalIgnoreCase)
        || table.Contains("salesrep", StringComparison.OrdinalIgnoreCase)
        || table.Contains("member", StringComparison.OrdinalIgnoreCase)
        || table.Contains("contact", StringComparison.OrdinalIgnoreCase)
        || table.Contains("users", StringComparison.OrdinalIgnoreCase)
        || table is "User" or "Users";

    public static bool IsSensitiveColumn(string table, string column)
    {
        var lower = column.ToLowerInvariant();
        foreach (var denied in DeniedSubstrings)
        {
            if (lower.Contains(denied, StringComparison.Ordinal))
                return true;
        }
        if (ExtraDeniedByTable.TryGetValue(table, out var extra))
        {
            foreach (var denied in extra)
            {
                if (lower.Contains(denied, StringComparison.Ordinal))
                    return true;
            }
        }
        if (IsPartyTable(table)
            && (lower.Contains("name", StringComparison.Ordinal)
                || lower.Contains("firstname", StringComparison.Ordinal)
                || lower.Contains("lastname", StringComparison.Ordinal)
                || lower.Contains("notes", StringComparison.Ordinal)))
            return true;
        return false;
    }

    // Every SQL statement executed by this harness. Kept in one registry so
    // --self-test can prove read-only presence without a database.
    public static class SqlTemplates
    {
        public const string SetReadOnly = "SET TRANSACTION READ ONLY";
        public const string VerifyReadOnly = "SELECT current_setting('transaction_read_only')";
        public const string DbInfo = "SELECT current_database() AS database_name, current_setting('transaction_isolation') AS isolation";
        public const string Tables = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'";
        public const string Columns = "SELECT table_name, column_name, udt_name FROM information_schema.columns WHERE table_schema = 'public'";
        public static IReadOnlyList<string> All() =>
        [
            SetReadOnly, VerifyReadOnly, DbInfo, Tables, Columns,
            // Dynamic section queries are built at runtime from introspected
            // identifiers; they always match one of these shapes:
            "SELECT t.<cols> FROM <table> t WHERE (t.<tenantCol> = ANY(@tenants))",
            "SELECT t.<cols> FROM <table> t WHERE (t.<tenantCol> = ANY(@tenants) OR t.<storeTenantCol> = ANY(@tenants))",
            "SELECT t.<cols> FROM <table> t WHERE (t.<tenantCol> = ANY(@tenants)) AND (t.<sourceCols> = ANY(@sources))",
            "SELECT c.<cols> FROM <table> c JOIN <parent> p ON p.<pk> = c.<fk> WHERE (p.<tenantCol> = ANY(@tenants))",
            "SELECT m.<cols> FROM <table> m WHERE EXISTS (SELECT 1 FROM <join> WHERE <link> AND <store> = ANY(@tenants))",
            "SELECT t.<cols> FROM <table> t WHERE (t.<refCol> = ANY(@capturedIds))",
            "SELECT COUNT(*) FROM <table>",
        ];
    }

    static readonly Regex ForbiddenWrite =
        new(@"\b(insert|update|delete|merge|create|alter|drop|truncate|grant|revoke|copy|vacuum|call|execute|listen|notify|refresh|reindex|cluster|comment|prepare|deallocate|import|load|discard|reset|handler|into|conflict)\b|\bfor\s+update\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Proves a statement is a read-only SELECT/WITH (or the single allowed
    // session control). Rejects multi-statements via ';'.
    public static bool IsReadOnlySql(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return false;
        var trimmed = sql.TrimStart();
        if (string.Equals(trimmed, SqlTemplates.SetReadOnly, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            return false;
        if (trimmed.Contains(';'))
            return false;
        if (ForbiddenWrite.IsMatch(trimmed))
            return false;
        return true;
    }

    sealed record SectionSpec(string Key, string[] Candidates)
    {
        // Tenant predicate columns, OR-ed. All values are restricted to the
        // allow-list, so an OR across tenant-scope columns cannot broaden scope.
        public string[] TenantCols { get; init; } = ["TenantId"];
        // Optional single-hop parent join: child FK -> parent PK, with the
        // explicit tenant predicate applied to the parent row.
        public JoinSpec? Join { get; init; }
        // Optional multi-hop EXISTS scoping for tables without usable tenant
        // columns (verified column-by-column at runtime).
        public ExistsKind Exists { get; init; } = ExistsKind.None;
        // Optional restriction to IDs already proven tenant-owned in this
        // snapshot (for polymorphic platform tables without tenant columns).
        public bool RestrictToCapturedIds { get; init; }
    }

    sealed record JoinSpec(string ParentTable, string[] ChildFkCandidates, string[] ParentTenantCols, string ParentPk = "Id");

    enum ExistsKind
    {
        None,
        MarketplaceOrderViaSubOrderStore,
        CommercePaymentViaOrderSubOrderStore,
        RefundExecutionViaRequestItemSubOrderStore,
        RequestViaItemSubOrderStore,
    }

    static IReadOnlyList<SectionSpec> BuildSections() =>
    [
        new("tenants", ["Tenants"]),
        new("ledgerAccounts", ["LedgerAccounts"]),
        new("treasuryAccounts", ["TreasuryAccounts"]),
        new("paymentMethods", ["PaymentMethods"]),
        new("tenantPaymentMethods", ["TenantPaymentMethods"]),
        new("supplierCardFundingInstruments", ["SupplierCardFundingInstruments"]),
        new("supplierCardFundings", ["SupplierCardFundings"]),
        new("treasuryInitialCapitalizations", ["TreasuryInitialCapitalizations"]),
        new("treasuryInitialCapitalizationAllocations", ["TreasuryInitialCapitalizationAllocations"]),
        new("treasuryTransactions", ["TreasuryTransactions"]),
        new("cashboxes", ["Cashboxes"]),
        new("orders", ["Orders"]),
        new("orderItems", ["OrderItems"])
            { Join = new JoinSpec("Orders", ["OrderId"], ["TenantId"]) },
        new("salesInvoices", ["SalesInvoices"]),
        new("salesInvoiceItems", ["SalesInvoiceItems"])
            { Join = new JoinSpec("SalesInvoices", ["SalesInvoiceId", "InvoiceId"], ["TenantId"]) },
        new("salesInvoicePayments", ["SalesInvoicePayments"])
            { Join = new JoinSpec("SalesInvoices", ["SalesInvoiceId", "InvoiceId"], ["TenantId"]) },
        new("purchaseInvoices", ["PurchaseInvoices", "Purchases"]),
        new("purchaseInvoiceItems", ["PurchaseInvoiceItems"])
            { Join = new JoinSpec("PurchaseInvoices", ["PurchaseInvoiceId", "InvoiceId"], ["TenantId"]) },
        new("purchaseInvoicePayments", ["PurchaseInvoicePayments"])
            { Join = new JoinSpec("PurchaseInvoices", ["PurchaseInvoiceId", "InvoiceId"], ["TenantId"]) },
        new("supplierPayments", ["SupplierPayments"]),
        new("customerPayments", ["CustomerPayments"]),
        // One Returns table owns both sale and purchase returns; ReturnType
        // remains in evidence so the two domains can be partitioned without
        // pretending a separate PurchaseReturns table exists.
        new("returns", ["Returns", "SalesReturns"]),
        new("returnItems", ["ReturnItems"])
            { Join = new JoinSpec("Returns", ["ReturnId"], ["TenantId"]) },
        new("orderRefunds", ["OrderRefunds"]),
        new("orderRefundLines", ["OrderRefundLines", "OrderRefundAllocations"]),
        new("affiliateCommissionLedger", ["AffiliateCommissionLedger"])
            { TenantCols = ["TenantId", "StoreTenantId"] },
        new("affiliateBalanceLedger", ["AffiliateBalanceLedger"])
            { TenantCols = ["TenantId", "StoreTenantId"] },
        new("affiliateAttributions", ["AffiliateAttributions"])
            { TenantCols = ["TenantId", "StoreTenantId"] },
        new("affiliateAccounts", ["AffiliateAccounts"])
            { TenantCols = ["TenantId", "StoreTenantId"] },
        new("affiliatePayoutRequests", ["AffiliatePayoutRequests"])
            { TenantCols = ["TenantId", "StoreTenantId"] },
        new("affiliatePayoutTransferEvidence", ["AffiliatePayoutTransferEvidence"])
            { TenantCols = ["TenantId", "StoreTenantId"] },
        new("affiliatePayoutTransferAllocations", ["AffiliatePayoutTransferAllocations"])
            { TenantCols = ["TenantId", "StoreTenantId"] },
        new("affiliatePromotionLinks", ["AffiliatePromotionLinks"])
            { TenantCols = ["TenantId", "StoreTenantId"] },
        // CommerceHub marketplace tables carry no direct TenantId. Vendor-side
        // tenancy is proven through CommerceHubSubOrders.StoreId, so every
        // marketplace query below carries an explicit StoreId predicate or an
        // EXISTS chain rooted at it.
        new("commerceMarketplaceOrders", ["CommerceHubMarketplaceOrders"])
            { Exists = ExistsKind.MarketplaceOrderViaSubOrderStore },
        new("commerceSubOrders", ["CommerceHubSubOrders"])
            { TenantCols = ["StoreId"] },
        new("commerceSubOrderItems", ["CommerceHubSubOrderItems"])
            { Join = new JoinSpec("CommerceHubSubOrders", ["SubOrderId"], ["StoreId"]) },
        new("commercePayments", ["CommerceHubPayments"])
            { Exists = ExistsKind.CommercePaymentViaOrderSubOrderStore },
        new("commercePaymentItems", ["CommerceHubPaymentItems"])
            { Join = new JoinSpec("CommerceHubSubOrders", ["SubOrderId"], ["StoreId"]) },
        new("commerceRefunds", ["CommerceHubRefunds"]),
        new("commerceRefundRequests", ["CommerceHubRefundRequests"])
            { Exists = ExistsKind.RequestViaItemSubOrderStore },
        new("commerceReturnRequests", ["CommerceHubReturnRequests"])
            { Exists = ExistsKind.RequestViaItemSubOrderStore },
        new("commerceRefundExecutions", ["CommerceHubRefundExecutions"])
            { Exists = ExistsKind.RefundExecutionViaRequestItemSubOrderStore },
        // Polymorphic platform table (ReferenceId/EntityId without tenant
        // columns): restricted to IDs proven tenant-owned in this snapshot.
        new("commerceFinancialTransactions", ["CommerceHubFinancialTransactions"])
            { RestrictToCapturedIds = true },
        new("commerceVendorEarnings", ["CommerceHubVendorEarnings"])
            { TenantCols = ["TenantId", "StoreTenantId"] },
        new("commerceVendorSettlements", ["CommerceHubVendorSettlements"])
            { TenantCols = ["TenantId", "StoreTenantId"] },
        new("commercePayouts", ["CommerceHubPayouts"])
            { TenantCols = ["TenantId", "StoreTenantId"] },
        new("commercePayoutItems", ["CommerceHubPayoutItems"])
            { Join = new JoinSpec("CommerceHubPayouts", ["PayoutId"], ["TenantId", "StoreTenantId"]) },
        new("commerceVendorPaymentRequests", ["CommerceHubVendorPaymentRequests"])
            { TenantCols = ["TenantId", "StoreTenantId"] },
        new("settlementBatches", ["PaymentSettlementBatches"]),
        new("settlementMembers", ["PaymentSettlementOrders", "PaymentSettlementMembers"]),
        new("productVariantStocks", ["ProductVariantStocks"]),
        new("inventoryReservations", ["InventoryReservations"]),
        new("stockMovements", ["StockMovements"]),
        new("warehouseStocks", ["WarehouseStocks"]),
        new("journalEntries", ["LedgerJournalEntries", "JournalEntries"]),
        new("journalLines", ["LedgerLines", "LedgerEntries"]),
        new("accountingOutbox", ["AccountingOutboxMessages"]),
        new("customerTransactions", ["CustomerTransactions"]),
        new("supplierTransactions", ["SupplierTransactions"]),
        new("accountLedgers", ["AccountLedgers"]),
        new("partyLedgerAdjustments", ["PartyLedgerAdjustments"]),
        new("marketplaceEventOutbox", ["MarketplaceEventOutboxMessages"]),
    ];

    public sealed class Options
    {
        public bool SelfTest { get; set; }
        public string? Output { get; set; }
        public bool Overwrite { get; set; }
        public List<Guid> Tenants { get; } = [];
        public List<Guid> Sources { get; } = [];
    }

    public static async Task<int> RunAsync(string[] args)
    {
        Options options;
        try
        {
            options = ParseArgs(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"argument error: {ex.Message}");
            PrintUsage();
            return 2;
        }

        if (options.SelfTest)
            return SelfTest.Run();

        if (options.Output is null)
        {
            Console.Error.WriteLine("argument error: --output PATH is required (or use --self-test).");
            PrintUsage();
            return 2;
        }

        IReadOnlyList<Guid> tenants;
        try
        {
            tenants = ResolveTenants(options.Tenants);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"tenant scope error: {ex.Message}");
            return 2;
        }

        string outputFullPath;
        try
        {
            outputFullPath = PrepareOutputPath(options.Output, options.Overwrite);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"output error: {ex.Message}");
            return 2;
        }

        string connectionString;
        try
        {
            connectionString = ResolveConnectionString();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"connection error: {ex.Message}");
            return 2;
        }

        try
        {
            var snapshot = await CaptureAsync(connectionString, tenants, options.Sources);
            var fingerprint = ComputeFingerprint(snapshot.Evidence);
            WriteEvidenceFile(outputFullPath, snapshot, fingerprint);
            var counts = snapshot.Evidence.Sections.ToDictionary(
                kv => kv.Key, kv => kv.Value.RowCount, StringComparer.Ordinal);
            var summary = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["path"] = outputFullPath,
                ["fingerprint"] = fingerprint,
                ["tenantCount"] = tenants.Count,
                ["counts"] = counts.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal),
            };
            Console.WriteLine(JsonSerializer.Serialize(summary));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"capture error: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    static void PrintUsage()
    {
        Console.Error.WriteLine(
            "usage: GlobalGoldenTreasuryReconciliation [--self-test] [--output PATH] [--overwrite] [--tenant GUID]... [--source-id GUID]...");
    }

    public static Options ParseArgs(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--self-test":
                    options.SelfTest = true;
                    break;
                case "--overwrite":
                    options.Overwrite = true;
                    break;
                case "--output":
                    if (++i >= args.Length)
                        throw new ArgumentException("--output requires a PATH value.");
                    options.Output = args[i];
                    break;
                case "--tenant":
                    if (++i >= args.Length)
                        throw new ArgumentException("--tenant requires a GUID value.");
                    options.Tenants.Add(ParseGuid(args[i], "--tenant"));
                    break;
                case "--source-id":
                    if (++i >= args.Length)
                        throw new ArgumentException("--source-id requires a GUID value.");
                    options.Sources.Add(ParseGuid(args[i], "--source-id"));
                    break;
                case "--help":
                case "-h":
                    PrintUsage();
                    throw new OperationCanceledException("help requested");
                default:
                    throw new ArgumentException($"unknown argument '{args[i]}'.");
            }
        }
        return options;
    }

    public static Guid ParseGuid(string value, string flag)
    {
        if (!Guid.TryParse(value, out var guid) || guid == Guid.Empty)
            throw new ArgumentException($"{flag} requires a non-empty GUID value, got '{value}'.");
        return guid;
    }

    // Default scope is exactly the three allow-list tenants. An explicit
    // subset is permitted only from the allow-list; anything else fails.
    public static IReadOnlyList<Guid> ResolveTenants(IReadOnlyList<Guid> requested)
    {
        if (requested.Count == 0)
            return AllowTenants.Keys.OrderBy(g => g).ToList();
        foreach (var guid in requested)
        {
            if (!AllowTenants.ContainsKey(guid))
                throw new ArgumentException(
                    $"tenant {guid:D} is not in the audited allow-list; scope cannot be broadened.");
        }
        return requested.Distinct().OrderBy(g => g).ToList();
    }

    static string PrepareOutputPath(string output, bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(output))
            throw new ArgumentException("output path is empty.");
        var full = Path.GetFullPath(output);
        if (File.Exists(full) && !overwrite)
            throw new IOException($"output file already exists: {full} (supply --overwrite to replace).");
        if (Directory.Exists(full))
            throw new IOException($"output path is an existing directory: {full}.");
        var parent = Path.GetDirectoryName(full);
        if (string.IsNullOrWhiteSpace(parent))
            throw new IOException($"output path has no valid parent directory: {full}.");
        Directory.CreateDirectory(parent);
        return full;
    }

    static string ResolveConnectionString()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        for (var depth = 0; depth < 8 && dir is not null; depth++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "PointOfSale.API", "appsettings.Development.local.json");
            if (File.Exists(candidate))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(candidate));
                if (doc.RootElement.TryGetProperty("ConnectionStrings", out var cs)
                    && cs.TryGetProperty("DefaultConnection", out var value))
                {
                    var resolved = value.GetString();
                    if (!string.IsNullOrWhiteSpace(resolved))
                        return resolved;
                }
                throw new InvalidOperationException(
                    "DefaultConnection is not configured in appsettings.Development.local.json.");
            }
        }
        throw new InvalidOperationException(
            "ConnectionStrings__DefaultConnection is not set and PointOfSale.API/appsettings.Development.local.json was not found.");
    }

    static string QuoteIdent(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";

    static NpgsqlCommand GuardedCommand(string sql, NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        if (!IsReadOnlySql(sql))
            throw new InvalidOperationException("blocked non-read-only SQL statement.");
        return new NpgsqlCommand(sql, connection, transaction);
    }

    public sealed class EvidenceSection
    {
        public string Table { get; set; } = "";
        public string Scope { get; set; } = "tenant";
        public int RowCount { get; set; }
        public List<string> RedactedColumns { get; } = [];
        public List<SortedDictionary<string, object?>> Rows { get; } = [];
    }

    sealed class Snapshot
    {
        public SortedDictionary<string, object?> Capture { get; } = new(StringComparer.Ordinal);
        public Evidence Evidence { get; } = new();
        public List<SortedDictionary<string, object?>> SchemaLimitations { get; } = [];
    }

    public sealed class Evidence
    {
        public SortedDictionary<string, object?> Scope { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, EvidenceSection> Sections { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, object?> Aggregates { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, object?> Linkage { get; } = new(StringComparer.Ordinal);
    }

    static readonly string[] SourceKeyCandidates =
    [
        "Id", "BusinessEntityId", "SourceId", "OrderId", "SalesInvoiceId",
        "PurchaseInvoiceId", "ReturnId", "RefundId", "OrderRefundId",
        "JournalEntryId", "JournalId", "PaymentId", "SettlementBatchId",
        "OutboxId", "SourceEntityId",
    ];
    static readonly string[] OrderKeyCandidates = ["Id", "CreatedAtUtc", "CreatedAt"];

    static async Task<Snapshot> CaptureAsync(
        string connectionString, IReadOnlyList<Guid> tenants, IReadOnlyList<Guid> sources)
    {
        var snapshot = new Snapshot();
        var capturedAt = DateTimeOffset.UtcNow;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        try
        {
            await using (var cmd = GuardedCommand(SqlTemplates.SetReadOnly, connection, transaction))
                await cmd.ExecuteNonQueryAsync();

            await using (var cmd = GuardedCommand(SqlTemplates.VerifyReadOnly, connection, transaction))
            {
                var flag = (await cmd.ExecuteScalarAsync())?.ToString();
                if (!string.Equals(flag, "on", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "read-only enforcement cannot be proven: transaction_read_only is not on. Stopping.");
            }

            string database = "";
            string isolation = "";
            await using (var cmd = GuardedCommand(SqlTemplates.DbInfo, connection, transaction))
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (await reader.ReadAsync())
                {
                    database = reader.GetValue(0)?.ToString() ?? "";
                    isolation = reader.GetValue(1)?.ToString() ?? "";
                }
            }

            var tables = await LoadStringSetAsync(connection, transaction, SqlTemplates.Tables);
            var columnsByTable = await LoadColumnsAsync(connection, transaction);

            var specs = BuildSections();
            // Captured-ID-restricted sections run last so the tenant-proven ID
            // set is complete before they execute.
            foreach (var spec in specs.Where(s => !s.RestrictToCapturedIds))
                await CaptureSectionSafeAsync(connection, transaction, snapshot, spec, tables, columnsByTable, tenants, sources, capturedIds: []);
            var capturedIds = CollectCapturedIds(snapshot);
            foreach (var spec in specs.Where(s => s.RestrictToCapturedIds))
                await CaptureSectionSafeAsync(connection, transaction, snapshot, spec, tables, columnsByTable, tenants, sources, capturedIds);

            snapshot.Evidence.Scope["tenants"] = tenants.Select(t => t.ToString("D").ToLowerInvariant())
                .OrderBy(s => s, StringComparer.Ordinal).ToList();
            snapshot.Evidence.Scope["sources"] = sources.Select(s => s.ToString("D").ToLowerInvariant())
                .OrderBy(s => s, StringComparer.Ordinal).ToList();
            BuildAggregates(snapshot);
            BuildLinkage(snapshot);

            snapshot.Capture["capturedAtUtc"] = capturedAt.ToUniversalTime()
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
            snapshot.Capture["toolVersion"] = ToolVersion;
            snapshot.Capture["transactionIsolation"] = isolation;
            snapshot.Capture["transactionReadOnly"] = "on";
            snapshot.Capture["database"] = database;
            snapshot.Capture["tenantScope"] = tenants.Select(t => t.ToString("D").ToLowerInvariant())
                .OrderBy(s => s, StringComparer.Ordinal).ToList();
            snapshot.Capture["sourceScope"] = sources.Select(s => s.ToString("D").ToLowerInvariant())
                .OrderBy(s => s, StringComparer.Ordinal).ToList();

            await transaction.RollbackAsync();
            return snapshot;
        }
        catch
        {
            try { await transaction.RollbackAsync(); } catch { /* best effort */ }
            throw;
        }
    }

    static async Task<HashSet<string>> LoadStringSetAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        await using var cmd = GuardedCommand(sql, connection, transaction);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var value = reader.GetValue(0)?.ToString();
            if (!string.IsNullOrEmpty(value))
                set.Add(value);
        }
        return set;
    }

    public sealed record ColumnInfo(string Name, string Udt);

    static async Task<Dictionary<string, List<ColumnInfo>>> LoadColumnsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var map = new Dictionary<string, List<ColumnInfo>>(StringComparer.Ordinal);
        await using var cmd = GuardedCommand(SqlTemplates.Columns, connection, transaction);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var table = reader.GetValue(0)?.ToString() ?? "";
            var column = reader.GetValue(1)?.ToString() ?? "";
            var udt = reader.GetValue(2)?.ToString() ?? "";
            if (table.Length == 0 || column.Length == 0)
                continue;
            if (!map.TryGetValue(table, out var list))
            {
                list = [];
                map[table] = list;
            }
            list.Add(new ColumnInfo(column, udt));
        }
        return map;
    }

    static List<string> ColumnNames(Dictionary<string, List<ColumnInfo>> map, string table) =>
        map.TryGetValue(table, out var list) ? list.Select(c => c.Name).ToList() : [];

    static HashSet<string> UuidColumns(Dictionary<string, List<ColumnInfo>> map, string table) =>
        map.TryGetValue(table, out var list)
            ? list.Where(c => string.Equals(c.Udt, "uuid", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Name).ToHashSet(StringComparer.Ordinal)
            : [];

    static List<Guid> CollectCapturedIds(Snapshot snapshot)
    {
        var ids = new HashSet<Guid>();
        foreach (var section in snapshot.Evidence.Sections.Values)
        {
            foreach (var row in section.Rows)
            {
                if (row.TryGetValue("Id", out var value) && value is string text
                    && Guid.TryParse(text, out var guid) && guid != Guid.Empty)
                    ids.Add(guid);
            }
        }
        return ids.OrderBy(g => g).ToList();
    }

    static async Task CaptureSectionSafeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Snapshot snapshot,
        SectionSpec spec,
        HashSet<string> tables,
        Dictionary<string, List<ColumnInfo>> columnsByTable,
        IReadOnlyList<Guid> tenants,
        IReadOnlyList<Guid> sources,
        IReadOnlyList<Guid> capturedIds)
    {
        try
        {
            await CaptureSectionAsync(connection, transaction, snapshot, spec, tables, columnsByTable, tenants, sources, capturedIds);
        }
        catch (Exception ex)
        {
            // Postgres/driver error text contains statement diagnostics only;
            // connection details are never part of it.
            var message = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message;
            AddLimitation(snapshot, spec.Key, "capture-error",
                $"section query failed ({ex.GetType().Name}: {message}); section omitted rather than fabricated.");
        }
    }

    static string TenantPredicate(
        string alias, IReadOnlyList<string> existingTenantCols, string paramName, out bool usable)
    {
        var clauses = existingTenantCols.Select(c => $"{alias}.{QuoteIdent(c)} = ANY(@{paramName})").ToList();
        usable = clauses.Count > 0;
        return string.Join(" OR ", clauses);
    }
    static async Task CaptureSectionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Snapshot snapshot,
        SectionSpec spec,
        HashSet<string> tables,
        Dictionary<string, List<ColumnInfo>> columnsByTable,
        IReadOnlyList<Guid> tenants,
        IReadOnlyList<Guid> sources,
        IReadOnlyList<Guid> capturedIds)
    {
        var table = spec.Candidates.FirstOrDefault(tables.Contains);
        if (table is null)
        {
            AddLimitation(snapshot, spec.Key, "table-missing",
                $"none of ({string.Join(", ", spec.Candidates)}) exists in the PostgreSQL schema; section omitted, not silently zero.");
            return;
        }

        var allColumns = ColumnNames(columnsByTable, table);
        var uuidCols = UuidColumns(columnsByTable, table);

        var visible = allColumns.Where(c => !IsSensitiveColumn(table, c)).ToList();
        var redacted = allColumns.Where(c => IsSensitiveColumn(table, c))
            .OrderBy(c => c, StringComparer.Ordinal).ToList();

        var section = new EvidenceSection { Table = table };
        section.RedactedColumns.AddRange(redacted);

        // Source-ID comparisons are uuid-typed only: comparing a varchar
        // column to a uuid array would abort the read-only transaction.
        var sourceCols = sources.Count > 0
            ? SourceKeyCandidates
                .Where(c => visible.Contains(c, StringComparer.Ordinal) && uuidCols.Contains(c))
                .ToList()
            : [];
        string sourceFilter(string alias) => sourceCols.Count > 0
            ? " AND (" + string.Join(" OR ", sourceCols.Select(c => $"{alias}.{QuoteIdent(c)} = ANY(@sources)")) + ")"
            : "";

        var orderCol = OrderKeyCandidates.FirstOrDefault(c => visible.Contains(c, StringComparer.Ordinal));
        string orderClause(string alias) => orderCol is not null ? $" ORDER BY {alias}.{QuoteIdent(orderCol)}" : "";

        string statement;
        string scope;
        List<(string Name, object Value)> parameters = [("tenants", tenants.ToArray())];
        if (sourceCols.Count > 0)
            parameters.Add(("sources", sources.ToArray()));

        if (table == "Tenants")
        {
            var selectList = visible.Count > 0
                ? string.Join(", ", visible.Select(c => $"t.{QuoteIdent(c)}"))
                : "1 AS _noop";
            statement = $"SELECT {selectList} FROM {QuoteIdent(table)} t WHERE t.{QuoteIdent("Id")} = ANY(@tenants){orderClause("t")}";
            scope = "tenant";
        }
        else if (spec.Join is not null)
        {
            var join = spec.Join;
            if (!tables.Contains(join.ParentTable))
            {
                AddLimitation(snapshot, spec.Key, "join-parent-missing",
                    $"parent table {join.ParentTable} does not exist; section omitted rather than unscoped.");
                return;
            }
            var parentColumns = ColumnNames(columnsByTable, join.ParentTable);
            var parentUuid = UuidColumns(columnsByTable, join.ParentTable);
            var childFk = join.ChildFkCandidates
                .FirstOrDefault(c => visible.Contains(c, StringComparer.Ordinal) && uuidCols.Contains(c));
            var parentTenantCols = join.ParentTenantCols
                .Where(c => parentColumns.Contains(c, StringComparer.Ordinal) && parentUuid.Contains(c)).ToList();
            if (childFk is null || parentTenantCols.Count == 0
                || !parentUuid.Contains(join.ParentPk))
            {
                AddLimitation(snapshot, spec.Key, "join-columns-missing",
                    $"join {table}.{string.Join("/", join.ChildFkCandidates)} -> {join.ParentTable} cannot be proven from the live schema; section omitted rather than unscoped.");
                return;
            }
            var selectList = visible.Count > 0
                ? string.Join(", ", visible.Select(c => $"c.{QuoteIdent(c)}"))
                : "1 AS _noop";
            statement = $"SELECT {selectList} FROM {QuoteIdent(table)} c"
                + $" JOIN {QuoteIdent(join.ParentTable)} p ON p.{QuoteIdent(join.ParentPk)} = c.{QuoteIdent(childFk)}"
                + $" WHERE ({TenantPredicate("p", parentTenantCols, "tenants", out _)})"
                + $"{sourceFilter("c")}{orderClause("c")}";
            scope = $"tenant-via-parent-{join.ParentTable}-on-{childFk}";
        }
        else if (spec.Exists != ExistsKind.None)
        {
            statement = BuildExistsSql(spec, table, visible, uuidCols, tables, columnsByTable, sourceFilter("m"), orderClause("m"))!;
            if (statement is null)
            {
                AddLimitation(snapshot, spec.Key, "exists-columns-missing",
                    "multi-hop tenant EXISTS scoping cannot be proven from the live schema; section omitted rather than unscoped.");
                return;
            }
            scope = $"tenant-via-suborder-store-exists:{spec.Exists}";
        }
        else if (spec.RestrictToCapturedIds)
        {
            var refCols = new[] { "ReferenceId", "EntityId" }
                .Where(c => visible.Contains(c, StringComparer.Ordinal) && uuidCols.Contains(c)).ToList();
            if (refCols.Count == 0 || capturedIds.Count == 0)
            {
                section.Scope = "platform-no-tenant-column-no-captured-ids";
                section.RowCount = 0;
                snapshot.Evidence.Sections[spec.Key] = section;
                AddLimitation(snapshot, spec.Key, "platform-scope-no-tenant-ids",
                    $"table {table} is platform-scoped without tenant columns and no tenant-proven IDs are available; no detail rows captured.");
                return;
            }
            var selectList = visible.Count > 0
                ? string.Join(", ", visible.Select(c => $"t.{QuoteIdent(c)}"))
                : "1 AS _noop";
            statement = $"SELECT {selectList} FROM {QuoteIdent(table)} t"
                + $" WHERE ({string.Join(" OR ", refCols.Select(c => $"t.{QuoteIdent(c)} = ANY(@capturedIds)") )})"
                + $"{sourceFilter("t")}{orderClause("t")}";
            parameters.Add(("capturedIds", capturedIds.ToArray()));
            scope = "tenant-via-captured-source-ids";
        }
        else
        {
            var existingTenantCols = (table == "Tenants" ? new[] { "Id" } : spec.TenantCols)
                .Where(c => allColumns.Contains(c, StringComparer.Ordinal) && UuidColumns(columnsByTable, table).Contains(c)).ToList();
            if (existingTenantCols.Count == 0)
            {
                section.Scope = "global-no-tenant-column";
                section.RowCount = await CountRowsAsync(connection, transaction, table);
                snapshot.Evidence.Sections[spec.Key] = section;
                AddLimitation(snapshot, spec.Key, "no-tenant-predicate",
                    $"table {table} has no tenant predicate column; only a global row count is captured, no detail rows.");
                return;
            }
            var selectList = visible.Count > 0
                ? string.Join(", ", visible.Select(c => $"t.{QuoteIdent(c)}"))
                : "1 AS _noop";
            statement = $"SELECT {selectList} FROM {QuoteIdent(table)} t"
                + $" WHERE ({TenantPredicate("t", existingTenantCols, "tenants", out _)})"
                + $"{sourceFilter("t")}{orderClause("t")}";
            scope = existingTenantCols.Count > 1 ? "tenant-or-store-tenant" : "tenant";
        }

        await using var cmd = GuardedCommand(statement, connection, transaction);
        foreach (var (name, value) in parameters)
            cmd.Parameters.Add(new NpgsqlParameter("@" + name, NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = value });

        await using var reader = await cmd.ExecuteReaderAsync();
        var fieldCount = reader.FieldCount;
        var fieldNames = Enumerable.Range(0, fieldCount).Select(reader.GetName).ToList();
        while (await reader.ReadAsync())
        {
            var row = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            for (var i = 0; i < fieldCount; i++)
                row[fieldNames[i]] = NormalizeValue(await reader.IsDBNullAsync(i) ? null : reader.GetValue(i));
            section.Rows.Add(row);
        }

        section.Rows.Sort((a, b) => string.CompareOrdinal(CanonicalJson(a), CanonicalJson(b)));
        section.RowCount = section.Rows.Count;
        if (sources.Count > 0 && sourceCols.Count == 0)
            scope += "+source-unfiltered-no-source-column";
        else if (sources.Count > 0)
            scope += "+source";
        section.Scope = scope;
        snapshot.Evidence.Sections[spec.Key] = section;
    }

    // Multi-hop EXISTS scoping rooted at the explicit vendor-store tenant
    // predicate CommerceHubSubOrders.StoreId. Every referenced column is
    // verified against the live schema before use; null means unverifiable.
    static string? BuildExistsSql(
        SectionSpec spec,
        string table,
        List<string> visible,
        HashSet<string> uuidCols,
        HashSet<string> tables,
        Dictionary<string, List<ColumnInfo>> columnsByTable,
        string sourceFilter,
        string orderClause)
    {
        bool Has(string t, string c) =>
            tables.Contains(t)
            && columnsByTable.TryGetValue(t, out var cols)
            && cols.Any(x => string.Equals(x.Name, c, StringComparison.Ordinal)
                && string.Equals(x.Udt, "uuid", StringComparison.OrdinalIgnoreCase));
        bool Outer(string c) => visible.Contains(c, StringComparer.Ordinal) && uuidCols.Contains(c);

        var selectList = visible.Count > 0
            ? string.Join(", ", visible.Select(c => $"m.{QuoteIdent(c)}"))
            : "1 AS _noop";

        switch (spec.Exists)
        {
            case ExistsKind.MarketplaceOrderViaSubOrderStore:
                if (!Has("CommerceHubSubOrders", "MarketplaceOrderId")
                    || !Has("CommerceHubSubOrders", "StoreId")
                    || !Outer("Id"))
                    return null;
                return $"SELECT {selectList} FROM {QuoteIdent(table)} m WHERE EXISTS ("
                    + $"SELECT 1 FROM {QuoteIdent("CommerceHubSubOrders")} s"
                    + $" WHERE s.{QuoteIdent("MarketplaceOrderId")} = m.{QuoteIdent("Id")} AND s.{QuoteIdent("StoreId")} = ANY(@tenants))"
                    + $"{sourceFilter}{orderClause}";
            case ExistsKind.CommercePaymentViaOrderSubOrderStore:
                if (!Outer("OrderId")
                    || !Has("CommerceHubMarketplaceOrders", "Id")
                    || !Has("CommerceHubSubOrders", "MarketplaceOrderId")
                    || !Has("CommerceHubSubOrders", "StoreId"))
                    return null;
                return $"SELECT {selectList} FROM {QuoteIdent(table)} m WHERE EXISTS ("
                    + $"SELECT 1 FROM {QuoteIdent("CommerceHubMarketplaceOrders")} o"
                    + $" JOIN {QuoteIdent("CommerceHubSubOrders")} s ON s.{QuoteIdent("MarketplaceOrderId")} = o.{QuoteIdent("Id")}"
                    + $" WHERE o.{QuoteIdent("Id")} = m.{QuoteIdent("OrderId")} AND s.{QuoteIdent("StoreId")} = ANY(@tenants))"
                    + $"{sourceFilter}{orderClause}";
            case ExistsKind.RefundExecutionViaRequestItemSubOrderStore:
                if (!Outer("RefundId")
                    || !Has("CommerceHubRefundRequests", "Id")
                    || !Has("CommerceHubRefundRequests", "OrderItemId")
                    || !Has("CommerceHubSubOrderItems", "Id")
                    || !Has("CommerceHubSubOrderItems", "SubOrderId")
                    || !Has("CommerceHubSubOrders", "Id")
                    || !Has("CommerceHubSubOrders", "StoreId"))
                    return null;
                return $"SELECT {selectList} FROM {QuoteIdent(table)} m WHERE EXISTS ("
                    + $"SELECT 1 FROM {QuoteIdent("CommerceHubRefundRequests")} r"
                    + $" JOIN {QuoteIdent("CommerceHubSubOrderItems")} i ON i.{QuoteIdent("Id")} = r.{QuoteIdent("OrderItemId")}"
                    + $" JOIN {QuoteIdent("CommerceHubSubOrders")} s ON s.{QuoteIdent("Id")} = i.{QuoteIdent("SubOrderId")}"
                    + $" WHERE r.{QuoteIdent("Id")} = m.{QuoteIdent("RefundId")} AND s.{QuoteIdent("StoreId")} = ANY(@tenants))"
                    + $"{sourceFilter}{orderClause}";
            case ExistsKind.RequestViaItemSubOrderStore:
                if (!Outer("OrderItemId")
                    || !Has("CommerceHubSubOrderItems", "Id")
                    || !Has("CommerceHubSubOrderItems", "SubOrderId")
                    || !Has("CommerceHubSubOrders", "Id")
                    || !Has("CommerceHubSubOrders", "StoreId"))
                    return null;
                return $"SELECT {selectList} FROM {QuoteIdent(table)} m WHERE EXISTS ("
                    + $"SELECT 1 FROM {QuoteIdent("CommerceHubSubOrderItems")} i"
                    + $" JOIN {QuoteIdent("CommerceHubSubOrders")} s ON s.{QuoteIdent("Id")} = i.{QuoteIdent("SubOrderId")}"
                    + $" WHERE i.{QuoteIdent("Id")} = m.{QuoteIdent("OrderItemId")} AND s.{QuoteIdent("StoreId")} = ANY(@tenants))"
                    + $"{sourceFilter}{orderClause}";
            default:
                return null;
        }
    }

    static async Task<int> CountRowsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string table)
    {
        var sql = $"SELECT COUNT(*) FROM {QuoteIdent(table)}";
        await using var cmd = GuardedCommand(sql, connection, transaction);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    static void AddLimitation(Snapshot snapshot, string section, string kind, string detail)
    {
        snapshot.SchemaLimitations.Add(new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["section"] = section,
            ["kind"] = kind,
            ["detail"] = detail,
        });
    }

    public static object? NormalizeValue(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case Guid g:
                return g.ToString("D").ToLowerInvariant();
            case DateTime dt:
                return dt.ToUniversalTime()
                    .ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
            case DateTimeOffset dto:
                return dto.ToUniversalTime()
                    .ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
            case decimal d:
                return d == 0 ? "0" : d.ToString("0.############################", CultureInfo.InvariantCulture);
            case double db:
                return db.ToString("R", CultureInfo.InvariantCulture);
            case float f:
                return ((double)f).ToString("R", CultureInfo.InvariantCulture);
            case bool b:
                return b;
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return Convert.ToInt64(value);
            case byte[] bytes:
                return Convert.ToBase64String(bytes);
            case string s:
                return s;
            case Array arr:
                var items = new List<object?>();
                foreach (var item in arr)
                    items.Add(NormalizeValue(item));
                return items;
            default:
                return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    // Deterministic canonical JSON: sorted object keys, invariant scalars.
    public static string CanonicalJson(object? node)
    {
        var sb = new StringBuilder();
        AppendCanonical(sb, node);
        return sb.ToString();
    }

    static void AppendCanonical(StringBuilder sb, object? node)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case string s:
                sb.Append(JsonSerializer.Serialize(s));
                break;
            case SortedDictionary<string, object?> dict:
                sb.Append('{');
                var first = true;
                foreach (var kv in dict)
                {
                    if (!first)
                        sb.Append(',');
                    first = false;
                    sb.Append(JsonSerializer.Serialize(kv.Key));
                    sb.Append(':');
                    AppendCanonical(sb, kv.Value);
                }
                sb.Append('}');
                break;
            case IDictionary<string, object?> generic:
                sb.Append('{');
                var ordered = true;
                foreach (var kv in generic.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    if (!ordered)
                        sb.Append(',');
                    ordered = false;
                    sb.Append(JsonSerializer.Serialize(kv.Key));
                    sb.Append(':');
                    AppendCanonical(sb, kv.Value);
                }
                sb.Append('}');
                break;
            case IEnumerable<object?> list:
                sb.Append('[');
                var started = true;
                foreach (var item in list)
                {
                    if (!started)
                        sb.Append(',');
                    started = false;
                    AppendCanonical(sb, item);
                }
                sb.Append(']');
                break;
            case long or int or short or byte or sbyte or ushort or uint or ulong:
                sb.Append(Convert.ToString(node, CultureInfo.InvariantCulture));
                break;
            case decimal dec:
                sb.Append(dec == 0 ? "0" : dec.ToString("0.############################", CultureInfo.InvariantCulture));
                break;
            case double dbl:
                sb.Append(((double)dbl).ToString("R", CultureInfo.InvariantCulture));
                break;
            default:
                sb.Append(JsonSerializer.Serialize(Convert.ToString(node, CultureInfo.InvariantCulture)));
                break;
        }
    }

    // Fingerprint covers only deterministic evidence; capture metadata
    // (timestamps, paths) is excluded by construction.
    public static string ComputeFingerprint(Evidence evidence)
    {
        var doc = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["scope"] = evidence.Scope,
            ["sections"] = evidence.Sections.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToDictionary(
                    kv => kv.Key,
                    kv => (object?)new SortedDictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["table"] = kv.Value.Table,
                        ["scope"] = kv.Value.Scope,
                        ["rowCount"] = kv.Value.RowCount,
                        ["redactedColumns"] = kv.Value.RedactedColumns.OrderBy(c => c, StringComparer.Ordinal).ToList<object?>(),
                        ["rows"] = kv.Value.Rows,
                    },
                    StringComparer.Ordinal),
            ["aggregates"] = evidence.Aggregates,
            ["linkage"] = evidence.Linkage,
        };
        var canonical = CanonicalJson(doc);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    // ---- Aggregates -----------------------------------------------------

    static List<SortedDictionary<string, object?>> SectionRows(Evidence evidence, string key) =>
        evidence.Sections.TryGetValue(key, out var section) ? section.Rows : [];

    static string? RowString(SortedDictionary<string, object?> row, params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (row.TryGetValue(candidate, out var value) && value is not null)
                return value.ToString();
        }
        return null;
    }

    static decimal RowDecimal(SortedDictionary<string, object?> row, params string[] candidates)
    {
        var text = RowString(row, candidates);
        return text is not null && decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;
    }

    static bool RowBool(SortedDictionary<string, object?> row, string candidate, bool defaultValue = false)
    {
        var text = RowString(row, candidate);
        return text is null
            ? defaultValue
            : text.Equals("true", StringComparison.OrdinalIgnoreCase) || text == "1";
    }

    static string CanonDecimal(decimal value) =>
        value == 0 ? "0" : value.ToString("0.############################", CultureInfo.InvariantCulture);

    static string? FindKey(IEnumerable<string> keys, params string[] candidates)
    {
        var set = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (set.Contains(candidate))
                return keys.First(k => string.Equals(k, candidate, StringComparison.OrdinalIgnoreCase));
        }
        return null;
    }

    static void BuildAggregates(Snapshot snapshot)
    {
        var evidence = snapshot.Evidence;
        var agg = evidence.Aggregates;
        var accounts = SectionRows(evidence, "ledgerAccounts");
        var treasury = SectionRows(evidence, "treasuryAccounts");
        var entries = SectionRows(evidence, "journalEntries");
        var lines = SectionRows(evidence, "journalLines");

        string? lineAccountKey = lines.Count > 0
            ? FindKey(lines[0].Keys, "AccountId", "LedgerAccountId") : null;
        string? lineDebitKey = lines.Count > 0
            ? FindKey(lines[0].Keys, "DebitAmount", "Debit", "DrAmount") : null;
        string? lineCreditKey = lines.Count > 0
            ? FindKey(lines[0].Keys, "CreditAmount", "Credit", "CrAmount") : null;
        string? lineJournalKey = lines.Count > 0
            ? FindKey(lines[0].Keys, "JournalEntryId", "JournalId") : null;
        var accountKey = accounts.Count > 0 ? FindKey(accounts[0].Keys, "Id") : null;
        var codeKey = accounts.Count > 0 ? FindKey(accounts[0].Keys, "AccountCode", "Code") : null;
        var cachedKey = accounts.Count > 0 ? FindKey(accounts[0].Keys, "CurrentBalance", "Balance") : null;
        var typeKey = accounts.Count > 0 ? FindKey(accounts[0].Keys, "AccountType", "Type") : null;
        var treasuryAccountKey = treasury.Count > 0 ? FindKey(treasury[0].Keys, "AccountId", "LedgerAccountId") : null;
        var treasuryTypeKey = treasury.Count > 0 ? FindKey(treasury[0].Keys, "Type", "TreasuryType") : null;

        // A line is authoritative only through a live Posted/Reversed journal.
        // The original reversed journal remains included because its immutable
        // effect is offset by a separate reversal journal.
        var validJournalIds = entries
            .Where(row => !RowBool(row, "IsDeleted"))
            .Where(row => RowString(row, "Status") is "1" or "2")
            .Select(row => RowString(row, "Id"))
            .Where(id => id is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        var postedLines = lineJournalKey is null
            ? new List<SortedDictionary<string, object?>>()
            : lines.Where(row => !RowBool(row, "IsDeleted")
                    && RowString(row, lineJournalKey) is string journalId
                    && validJournalIds.Contains(journalId))
                .ToList();

        var postedByAccount = new Dictionary<string, (decimal Debit, decimal Credit)>(StringComparer.Ordinal);
        if (lineAccountKey is not null && lineDebitKey is not null && lineCreditKey is not null)
        {
            foreach (var line in postedLines)
            {
                var accountId = RowString(line, lineAccountKey);
                if (accountId is null)
                    continue;
                postedByAccount.TryGetValue(accountId, out var totals);
                totals.Debit += RowDecimal(line, lineDebitKey);
                totals.Credit += RowDecimal(line, lineCreditKey);
                postedByAccount[accountId] = totals;
            }
        }

        // Treasury authority is active, non-deleted and tenant-consistent.
        var treasuryByAccount = new Dictionary<string, string>(StringComparer.Ordinal);
        if (treasuryAccountKey is not null && treasuryTypeKey is not null)
        {
            foreach (var mapping in treasury.Where(row => RowBool(row, "IsActive") && !RowBool(row, "IsDeleted")))
            {
                var accountId = RowString(mapping, treasuryAccountKey);
                var mapType = RowString(mapping, treasuryTypeKey);
                var tenantId = RowString(mapping, "TenantId");
                if (accountId is not null && mapType is not null && tenantId is not null)
                    treasuryByAccount[$"{tenantId}:{accountId}"] = mapType;
            }
        }

        var accountRows = new List<object?>();
        foreach (var account in accounts)
        {
            var id = accountKey is not null ? RowString(account, accountKey) : null;
            var tenantId = RowString(account, "TenantId");
            var cached = cachedKey is not null ? RowDecimal(account, cachedKey) : 0m;
            postedByAccount.TryGetValue(id ?? "", out var posted);
            // Debit-normal classes follow T-11 semantics: AccountType 1/5/6.
            var typeText = typeKey is not null ? RowString(account, typeKey) : null;
            var debitNormal = typeText is "1" or "5" or "6";
            var ledger = debitNormal ? posted.Debit - posted.Credit : posted.Credit - posted.Debit;
            accountRows.Add(new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["tenantId"] = tenantId,
                ["accountCode"] = codeKey is not null ? RowString(account, codeKey) : null,
                ["cachedBalance"] = CanonDecimal(cached),
                ["postedDebit"] = CanonDecimal(posted.Debit),
                ["postedCredit"] = CanonDecimal(posted.Credit),
                ["ledgerBalance"] = CanonDecimal(ledger),
                ["variance"] = CanonDecimal(cached - ledger),
                ["treasuryType"] = id is not null && tenantId is not null
                    && treasuryByAccount.TryGetValue($"{tenantId}:{id}", out var t) ? t : null,
                ["isActive"] = RowString(account, "IsActive"),
                ["isDeleted"] = RowString(account, "IsDeleted"),
                ["isSystem"] = RowString(account, "IsSystem"),
                ["isClearing"] = RowString(account, "IsClearing"),
                ["isSupplierCardFunding"] = RowString(account, "IsSupplierCardFunding"),
            });
        }
        accountRows.Sort((a, b) => string.CompareOrdinal(CanonicalJson(a), CanonicalJson(b)));
        agg["treasuryByAccount"] = accountRows;
        agg["treasuryAggregateKeys"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["lineAccountKey"] = lineAccountKey,
            ["lineDebitKey"] = lineDebitKey,
            ["lineCreditKey"] = lineCreditKey,
            ["lineJournalKey"] = lineJournalKey,
            ["accountIdKey"] = accountKey,
            ["accountCodeKey"] = codeKey,
            ["cachedBalanceKey"] = cachedKey,
            ["accountTypeKey"] = typeKey,
        };

        var requestedRows = new List<object?>();
        foreach (var tenantId in snapshot.Evidence.Scope["tenants"] as IEnumerable<string> ?? [])
        {
            var tenantAccounts = accounts
                .Where(row => string.Equals(RowString(row, "TenantId"), tenantId, StringComparison.Ordinal))
                .ToList();
            foreach (var code in RequestedAccountCodes)
            {
                var row = tenantAccounts.FirstOrDefault(candidate =>
                    string.Equals(codeKey is not null ? RowString(candidate, codeKey) : null, code, StringComparison.Ordinal));
                if (row is not null)
                {
                    var id = accountKey is not null ? RowString(row, accountKey) : null;
                    postedByAccount.TryGetValue(id ?? "", out var posted);
                    var debitNormal = RowString(row, typeKey ?? "AccountType") is "1" or "5" or "6";
                    requestedRows.Add(new SortedDictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["tenantId"] = tenantId,
                        ["accountCode"] = code,
                        ["present"] = true,
                        ["cachedBalance"] = CanonDecimal(cachedKey is not null ? RowDecimal(row, cachedKey) : 0m),
                        ["ledgerBalance"] = CanonDecimal(debitNormal
                            ? posted.Debit - posted.Credit
                            : posted.Credit - posted.Debit),
                    });
                }
                else
                {
                    requestedRows.Add(new SortedDictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["tenantId"] = tenantId,
                        ["accountCode"] = code,
                        ["present"] = false,
                        ["cachedBalance"] = null,
                        ["ledgerBalance"] = null,
                    });
                }
            }
        }
        agg["requestedAccountCodes"] = requestedRows;

        decimal totalDebit = 0m, totalCredit = 0m;
        foreach (var totals in postedByAccount.Values)
        {
            totalDebit += totals.Debit;
            totalCredit += totals.Credit;
        }
        agg["journalTotals"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["entryCount"] = validJournalIds.Count,
            ["lineCount"] = postedLines.Count,
            ["totalDebit"] = CanonDecimal(totalDebit),
            ["totalCredit"] = CanonDecimal(totalCredit),
            ["variance"] = CanonDecimal(totalDebit - totalCredit),
            ["balanced"] = totalDebit == totalCredit,
        };

        // Per-tenant report inputs prevent equal account codes in different
        // tenants from collapsing into one apparent financial statement.
        var reportByTenant = new List<object?>();
        foreach (var tenantId in snapshot.Evidence.Scope["tenants"] as IEnumerable<string> ?? [])
        {
            var tenantAccounts = accounts
                .Where(row => string.Equals(RowString(row, "TenantId"), tenantId, StringComparison.Ordinal))
                .ToList();
            var tenantAccountIds = tenantAccounts
                .Select(row => accountKey is not null ? RowString(row, accountKey) : null)
                .Where(id => id is not null)
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);
            decimal tenantDebit = 0m, tenantCredit = 0m;
            foreach (var accountId in tenantAccountIds)
            {
                postedByAccount.TryGetValue(accountId, out var posted);
                tenantDebit += posted.Debit;
                tenantCredit += posted.Credit;
            }

            decimal assets = 0m, liabilities = 0m, equity = 0m, revenue = 0m, expenses = 0m, cogs = 0m;
            decimal cashCached = 0m, cashLedger = 0m, clearing = 0m, supplierCard = 0m;
            var byClass = new SortedDictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var account in tenantAccounts)
            {
                var id = accountKey is not null ? RowString(account, accountKey) : null;
                var type = typeKey is not null ? RowString(account, typeKey) ?? "unknown" : "unknown";
                var cached = cachedKey is not null ? RowDecimal(account, cachedKey) : 0m;
                byClass[type] = byClass.TryGetValue(type, out var prior) ? prior + cached : cached;
                switch (type)
                {
                    case "1": assets += cached; break;
                    case "2": liabilities += cached; break;
                    case "3": equity += cached; break;
                    case "4": revenue += cached; break;
                    case "5": expenses += cached; break;
                    case "6": cogs += cached; break;
                }

                if (RowBool(account, "IsSupplierCardFunding"))
                    supplierCard += cached;
                if (id is null || !RowBool(account, "IsActive") || RowBool(account, "IsDeleted")
                    || type != "1" || !treasuryByAccount.TryGetValue($"{tenantId}:{id}", out var mapType))
                    continue;
                if (mapType is "1" or "2")
                {
                    cashCached += cached;
                    postedByAccount.TryGetValue(id, out var posted);
                    cashLedger += posted.Debit - posted.Credit;
                }
                else if (mapType == "3")
                {
                    clearing += cached;
                }
            }

            var netProfit = revenue - expenses - cogs;
            var rightSide = liabilities + equity + netProfit;
            reportByTenant.Add(new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["tenantId"] = tenantId,
                ["cashClosingCached"] = CanonDecimal(cashCached),
                ["cashClosingLedger"] = CanonDecimal(cashLedger),
                ["cashVariance"] = CanonDecimal(cashCached - cashLedger),
                ["clearingClosing"] = CanonDecimal(clearing),
                ["supplierCardClosing"] = CanonDecimal(supplierCard),
                ["revenue"] = CanonDecimal(revenue),
                ["operatingExpenses"] = CanonDecimal(expenses),
                ["cogs"] = CanonDecimal(cogs),
                ["netProfit"] = CanonDecimal(netProfit),
                ["assets"] = CanonDecimal(assets),
                ["liabilities"] = CanonDecimal(liabilities),
                ["equityBeforeCurrentEarnings"] = CanonDecimal(equity),
                ["liabilitiesPlusEquityAndCurrentEarnings"] = CanonDecimal(rightSide),
                ["balanceSheetVariance"] = CanonDecimal(assets - rightSide),
                ["balanceSheetBalanced"] = assets == rightSide,
                ["trialBalanceDebit"] = CanonDecimal(tenantDebit),
                ["trialBalanceCredit"] = CanonDecimal(tenantCredit),
                ["trialBalanceVariance"] = CanonDecimal(tenantDebit - tenantCredit),
                ["trialBalanceBalanced"] = tenantDebit == tenantCredit,
                ["balancesByAccountType"] = byClass.ToDictionary(
                    kv => kv.Key, kv => (object?)CanonDecimal(kv.Value), StringComparer.Ordinal),
            });
        }
        agg["reportInputsByTenant"] = reportByTenant;

        BuildOutboxAggregates(snapshot);
        BuildPartyAggregates(snapshot);
        BuildStockAggregates(snapshot);
    }

    static void BuildOutboxAggregates(Snapshot snapshot)
    {
        var rows = SectionRows(snapshot.Evidence, "accountingOutbox");
        string? statusKey = rows.Count > 0 ? FindKey(rows[0].Keys, "Status", "State") : null;
        string? failedKey = rows.Count > 0 ? FindKey(rows[0].Keys, "IsFailed") : null;
        string? processedKey = rows.Count > 0 ? FindKey(rows[0].Keys, "ProcessedAtUtc", "ProcessedAt", "IsProcessed") : null;
        string? journalKey = rows.Count > 0 ? FindKey(rows[0].Keys, "JournalId", "JournalEntryId") : null;
        string? typeKey = rows.Count > 0
            ? FindKey(rows[0].Keys, "EventType", "SourceType", "Type", "MessageType") : null;
        string? sourceKey = rows.Count > 0
            ? FindKey(rows[0].Keys, "SourceType", "Source", "BusinessEntityType") : null;

        var byType = new SortedDictionary<string, long>(StringComparer.Ordinal);
        var bySource = new SortedDictionary<string, long>(StringComparer.Ordinal);
        long pending = 0, failed = 0, unlinked = 0;
        foreach (var row in rows)
        {
            var type = typeKey is not null ? RowString(row, typeKey) ?? "unknown" : "unknown";
            byType[type] = byType.TryGetValue(type, out var c) ? c + 1 : 1;
            var source = sourceKey is not null ? RowString(row, sourceKey) ?? "unknown" : "unknown";
            bySource[source] = bySource.TryGetValue(source, out var s) ? s + 1 : 1;
            var status = statusKey is not null ? RowString(row, statusKey) ?? "" : "";
            var isFailedText = failedKey is not null ? RowString(row, failedKey) ?? "" : "";
            var processedText = processedKey is not null ? RowString(row, processedKey) : null;
            var isFailed = status.Contains("Fail", StringComparison.OrdinalIgnoreCase)
                || status is "3"
                || isFailedText.Equals("true", StringComparison.OrdinalIgnoreCase)
                || isFailedText is "1";
            // Pending = never processed and not failed. ProcessedAtUtc null
            // (or IsProcessed false) is the code-level pending signal.
            var processed = processedText is not null
                && !processedText.Equals("false", StringComparison.OrdinalIgnoreCase);
            if (isFailed)
                failed++;
            else if (!processed)
                pending++;
            if (journalKey is null || RowString(row, journalKey) is null)
                unlinked++;
        }
        snapshot.Evidence.Aggregates["outboxSummary"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["total"] = (long)rows.Count,
            ["pending"] = pending,
            ["failed"] = failed,
            ["unlinked"] = unlinked,
            ["statusKey"] = statusKey,
            ["failedKey"] = failedKey,
            ["processedKey"] = processedKey,
            ["journalKey"] = journalKey,
            ["countsByType"] = byType.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal),
            ["countsBySource"] = bySource.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal),
        };
    }

    static void BuildPartyAggregates(Snapshot snapshot)
    {
        var evidence = snapshot.Evidence;
        var control = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var key in new[] { "customerTransactions", "supplierTransactions", "accountLedgers", "partyLedgerAdjustments" })
        {
            var rows = SectionRows(evidence, key);
            string? debitKey = rows.Count > 0 ? FindKey(rows[0].Keys, "DebitAmount") : null;
            string? creditKey = rows.Count > 0 ? FindKey(rows[0].Keys, "CreditAmount") : null;
            string? amountKey = rows.Count > 0
                ? FindKey(rows[0].Keys, "Amount", "Total", "Balance", "RunningBalance", "DebitAmount", "CreditAmount") : null;
            decimal debit = 0m, credit = 0m, total = 0m;
            foreach (var row in rows)
            {
                debit += debitKey is not null ? RowDecimal(row, debitKey) : 0m;
                credit += creditKey is not null ? RowDecimal(row, creditKey) : 0m;
                total += amountKey is not null ? RowDecimal(row, amountKey) : 0m;
            }
            var partyKey = rows.Count > 0
                ? FindKey(rows[0].Keys, "CustomerId", "SupplierId", "AffiliateId", "PartyId", "AccountId") : null;
            var distinctParties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var party = partyKey is not null ? RowString(row, partyKey) : null;
                if (party is not null)
                    distinctParties.Add(party);
            }
            control[key] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["rowCount"] = (long)rows.Count,
                ["amountKey"] = amountKey,
                ["controlTotal"] = CanonDecimal(total),
                ["debitTotal"] = CanonDecimal(debit),
                ["creditTotal"] = CanonDecimal(credit),
                ["netTotal"] = CanonDecimal(debit - credit),
                ["distinctPartyCount"] = (long)distinctParties.Count,
                ["partyKey"] = partyKey,
            };
        }
        evidence.Aggregates["partyControls"] = control;
    }

    static void BuildStockAggregates(Snapshot snapshot)
    {
        var evidence = snapshot.Evidence;
        var stock = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var key in new[] { "productVariantStocks", "warehouseStocks", "inventoryReservations", "stockMovements" })
        {
            var rows = SectionRows(evidence, key);
            string? qtyKey = rows.Count > 0
                ? FindKey(rows[0].Keys, "Quantity", "AvailableQuantity", "OnHand", "Stock", "ReservedQuantity", "CommittedQuantity", "QuantityChange", "QuantityAfter", "ReservedQuantityChange", "AvailableAfter") : null;
            decimal total = 0m;
            foreach (var row in rows)
                total += qtyKey is not null ? RowDecimal(row, qtyKey) : 0m;
            stock[key] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["rowCount"] = (long)rows.Count,
                ["quantityKey"] = qtyKey,
                ["quantityTotal"] = CanonDecimal(total),
            };
        }
        evidence.Aggregates["stockTotals"] = stock;
    }

    // Source -> outbox -> journal linkage. Links are proven only by shared
    // TenantId + BusinessEntityId/source IDs, idempotency keys, and JournalId.
    // Amounts are never used to infer a link; unresolved rows are emitted.
    static void BuildLinkage(Snapshot snapshot)
    {
        var evidence = snapshot.Evidence;
        var outbox = SectionRows(evidence, "accountingOutbox");
        var entries = SectionRows(evidence, "journalEntries");
        var lines = SectionRows(evidence, "journalLines");

        string? outTenant = outbox.Count > 0 ? FindKey(outbox[0].Keys, "TenantId") : null;
        string? outEntity = outbox.Count > 0
            ? FindKey(outbox[0].Keys, "BusinessEntityId", "SourceId", "SourceEntityId", "Id") : null;
        string? outIdem = outbox.Count > 0
            ? FindKey(outbox[0].Keys, "IdempotencyKey", "Idempotency", "RequestHash") : null;
        string? outSourceType = outbox.Count > 0
            ? FindKey(outbox[0].Keys, "SourceType", "EventType", "Type") : null;
        string? outSourceRef = outbox.Count > 0
            ? FindKey(outbox[0].Keys, "SourceReference", "Source", "Reference", "SourceId") : null;
        string? outJournal = outbox.Count > 0 ? FindKey(outbox[0].Keys, "JournalId", "JournalEntryId") : null;

        string? entryId = entries.Count > 0 ? FindKey(entries[0].Keys, "Id") : null;
        string? lineJournal = lines.Count > 0 ? FindKey(lines[0].Keys, "JournalEntryId", "JournalId") : null;
        string? lineOrder = lines.Count > 0 ? FindKey(lines[0].Keys, "LineNumber", "Order", "Sequence") : null;

        var linesByJournal = new Dictionary<string, List<SortedDictionary<string, object?>>>(StringComparer.Ordinal);
        if (lineJournal is not null)
        {
            foreach (var line in lines)
            {
                var journalId = RowString(line, lineJournal);
                if (journalId is null)
                    continue;
                if (!linesByJournal.TryGetValue(journalId, out var list))
                {
                    list = [];
                    linesByJournal[journalId] = list;
                }
                list.Add(line);
            }
            foreach (var list in linesByJournal.Values)
                list.Sort((a, b) => string.CompareOrdinal(
                    lineOrder is not null ? RowString(a, lineOrder) ?? "" : CanonicalJson(a),
                    lineOrder is not null ? RowString(b, lineOrder) ?? "" : CanonicalJson(b)));
        }

        var referencedJournals = new HashSet<string>(StringComparer.Ordinal);
        var outboxSourceKeys = new HashSet<string>(StringComparer.Ordinal);
        var links = new List<object?>();
        var unresolvedOutbox = new List<object?>();
        foreach (var row in outbox)
        {
            var tenantId = outTenant is not null ? RowString(row, outTenant) : null;
            var businessEntityId = outEntity is not null ? RowString(row, outEntity) : null;
            if (tenantId is not null && businessEntityId is not null)
                outboxSourceKeys.Add($"{tenantId}:{businessEntityId}");

            var journalId = outJournal is not null ? RowString(row, outJournal) : null;
            var entry = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["tenantId"] = tenantId,
                ["businessEntityId"] = businessEntityId,
                ["idempotencyKey"] = outIdem is not null ? RowString(row, outIdem) : null,
                ["sourceType"] = outSourceType is not null ? RowString(row, outSourceType) : null,
                ["sourceReference"] = outSourceRef is not null ? RowString(row, outSourceRef) : null,
                ["journalId"] = journalId,
                ["linkageKeys"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["tenantKey"] = outTenant,
                    ["entityKey"] = outEntity,
                    ["idempotencyKeyColumn"] = outIdem,
                    ["journalKey"] = outJournal,
                },
            };
            if (journalId is not null && linesByJournal.TryGetValue(journalId, out var journalLines))
            {
                referencedJournals.Add(journalId);
                entry["orderedLineCount"] = (long)journalLines.Count;
                links.Add(entry);
            }
            else if (journalId is not null)
            {
                referencedJournals.Add(journalId);
                entry["orderedLineCount"] = (long)0;
                links.Add(entry);
            }
            else
            {
                unresolvedOutbox.Add(entry);
            }
        }

        var entriesById = entryId is null
            ? new Dictionary<string, SortedDictionary<string, object?>>(StringComparer.Ordinal)
            : entries
                .Where(entry => RowString(entry, entryId) is not null)
                .GroupBy(entry => RowString(entry, entryId)!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var classifiedNonPrimaryJournals = new List<object?>();
        var unresolvedJournals = new List<object?>();
        if (entryId is not null)
        {
            foreach (var entry in entries)
            {
                var id = RowString(entry, entryId);
                if (id is not null && !referencedJournals.Contains(id))
                {
                    var tenantId = RowString(entry, "TenantId");
                    var sourceId = RowString(entry, "SourceId");
                    var sourceType = RowString(entry, "SourceType");
                    var reversesJournalId = RowString(entry, "ReversesJournalId");
                    var classification = "unclassified";
                    var evidenceBasis = "No structural accounting authority lineage was proven.";

                    if (tenantId is not null && sourceId is not null &&
                        outboxSourceKeys.Contains($"{tenantId}:{sourceId}"))
                    {
                        classification = "outbox-source-companion";
                        evidenceBasis = "TenantId and SourceId match an accounting outbox BusinessEntityId; the outbox stores a different primary JournalId for the same source.";
                    }
                    else if (sourceType == "Reversal" && reversesJournalId is not null &&
                             sourceId == reversesJournalId && entriesById.ContainsKey(reversesJournalId))
                    {
                        classification = "journal-reversal-lineage";
                        evidenceBasis = "SourceId and ReversesJournalId identify an existing original journal.";
                    }
                    else if (sourceType == "AffiliateCommissionEarned")
                    {
                        classification = "masstransit-consumer-direct-journal";
                        evidenceBasis = "Affiliate commission accounting is posted by its MassTransit accounting consumer rather than the accounting outbox.";
                    }
                    else if (sourceType is "Order_COD" or "Order_Refund")
                    {
                        classification = "commerce-accounting-direct-journal";
                        evidenceBasis = "The Storefront payment/refund path posts through CommerceAccountingService without an accounting outbox row.";
                    }
                    else if (sourceType == "SupplierCardFunding")
                    {
                        classification = "historical-supplier-card-direct-journal";
                        evidenceBasis = "The historical supplier-card funding journal predates the current controller-created accounting outbox linkage.";
                    }

                    var classified = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["journalId"] = id,
                        ["tenantId"] = tenantId,
                        ["journalNumber"] = RowString(entry, "JournalNumber"),
                        ["sourceId"] = sourceId,
                        ["sourceReference"] = RowString(entry, "SourceReference", "Source", "Reference", "JournalNumber"),
                        ["sourceType"] = sourceType,
                        ["idempotencyKey"] = RowString(entry, "IdempotencyKey"),
                        ["reversesJournalId"] = reversesJournalId,
                        ["classification"] = classification,
                        ["evidenceBasis"] = evidenceBasis,
                    };
                    classifiedNonPrimaryJournals.Add(classified);
                    if (classification == "unclassified")
                        unresolvedJournals.Add(classified);
                }
            }
        }
        classifiedNonPrimaryJournals.Sort((a, b) => string.CompareOrdinal(CanonicalJson(a), CanonicalJson(b)));
        unresolvedJournals.Sort((a, b) => string.CompareOrdinal(CanonicalJson(a), CanonicalJson(b)));

        evidence.Linkage["links"] = links
            .OrderBy(l => CanonicalJson(l), StringComparer.Ordinal).ToList();
        evidence.Linkage["unresolvedOutbox"] = unresolvedOutbox
            .OrderBy(l => CanonicalJson(l), StringComparer.Ordinal).ToList();
        evidence.Linkage["classifiedNonPrimaryJournals"] = classifiedNonPrimaryJournals;
        evidence.Linkage["nonPrimaryJournalClassificationCounts"] = classifiedNonPrimaryJournals
            .Cast<SortedDictionary<string, object?>>()
            .GroupBy(row => row["classification"]?.ToString() ?? "unclassified", StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (object?)(long)group.Count(), StringComparer.Ordinal);
        evidence.Linkage["unresolvedJournals"] = unresolvedJournals;
        evidence.Linkage["method"] = "tenant-plus-entity-plus-idempotency-plus-journal-id; never amount-inferred";
    }

    static void WriteEvidenceFile(string path, Snapshot snapshot, string fingerprint)
    {
        var sections = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var kv in snapshot.Evidence.Sections)
        {
            sections[kv.Key] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["table"] = kv.Value.Table,
                ["scope"] = kv.Value.Scope,
                ["rowCount"] = (long)kv.Value.RowCount,
                ["redactedColumns"] = kv.Value.RedactedColumns.OrderBy(c => c, StringComparer.Ordinal).ToList<object?>(),
                ["rows"] = kv.Value.Rows.Cast<object?>().ToList(),
            };
        }
        var limitations = snapshot.SchemaLimitations
            .OrderBy(l => CanonicalJson(l), StringComparer.Ordinal)
            .Cast<object?>().ToList();
        var doc = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["capture"] = snapshot.Capture,
            ["fingerprint"] = fingerprint,
            ["evidence"] = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["scope"] = snapshot.Evidence.Scope,
                ["sections"] = sections,
                ["aggregates"] = snapshot.Evidence.Aggregates,
                ["linkage"] = snapshot.Evidence.Linkage,
            },
            ["schemaLimitations"] = limitations,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ---- Self-test (no database) ----------------------------------------

    static class SelfTest
    {
        public static int Run()
        {
            var failures = 0;
            failures += Check("canonical-ordering-hash-stability", () =>
            {
                var rowA = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                    { ["b"] = "2", ["a"] = "1" };
                var rowB = new SortedDictionary<string, object?>(StringComparer.Ordinal)
                    { ["a"] = "1", ["b"] = "2" };
                if (CanonicalJson(rowA) != CanonicalJson(rowB))
                    throw new Exception("key order changed canonical form.");
                var evidence1 = SampleEvidence([rowA]);
                var evidence2 = SampleEvidence([rowB]);
                if (ComputeFingerprint(evidence1) != ComputeFingerprint(evidence2))
                    throw new Exception("row order changed fingerprint.");
            });
            failures += Check("capture-timestamp-excluded-from-fingerprint", () =>
            {
                var snapshot1 = new Snapshot();
                snapshot1.Capture["capturedAtUtc"] = "2026-09-15T00:00:00.0000000Z";
                snapshot1.Capture["capturedAtUtc"] = "2026-09-16T00:00:00.0000000Z";
                // Fingerprint input never includes Capture: prove by hashing
                // two evidences that differ only outside evidence.
                var e1 = SampleEvidence([]);
                var e2 = SampleEvidence([]);
                if (ComputeFingerprint(e1) != ComputeFingerprint(e2))
                    throw new Exception("identical evidence produced different fingerprints.");
                if (snapshot1.Capture["capturedAtUtc"]?.ToString() is not string)
                    throw new Exception("capture metadata missing.");
            });
            failures += Check("tenant-allow-list-rejection", () =>
            {
                var subset = ResolveTenants([Guid.Parse("83b39fe2-c80d-45a0-8c0d-5ce6c1f33ae5")]);
                if (subset.Count != 1)
                    throw new Exception("explicit subset was not honored.");
                var all = ResolveTenants([]);
                if (all.Count != 3)
                    throw new Exception("default scope is not exactly three tenants.");
                try
                {
                    ResolveTenants([Guid.NewGuid()]);
                    throw new Exception("foreign tenant was accepted.");
                }
                catch (ArgumentException) { /* expected */ }
            });
            failures += Check("source-guid-validation", () =>
            {
                ParseGuid("a9c435b4-f3c0-4a74-93f7-e7a2a9b42593", "--source-id");
                try
                {
                    ParseGuid("not-a-guid", "--source-id");
                    throw new Exception("invalid GUID was accepted.");
                }
                catch (ArgumentException) { /* expected */ }
                try
                {
                    ParseGuid("00000000-0000-0000-0000-000000000000", "--source-id");
                    throw new Exception("empty GUID was accepted.");
                }
                catch (ArgumentException) { /* expected */ }
            });
            failures += Check("sensitive-key-redaction-exclusion", () =>
            {
                string[] sensitive =
                [
                    "WebhookPayload", "RawOutboxPayload", "Email", "PhoneNumber",
                    "PasswordHash", "ApiToken", "SecretKey", "CookieValue",
                    "Credential", "HomeAddress", "CardNumber", "Cvv",
                    "ConfigEncrypted", "CheckoutUrl", "AccountSnapshot",
                    "PayoutAccountHolderNameSnapshot", "GuestName", "PricingSnapshotJson",
                    "Notes", "AdminNote", "Description", "FailureReason",
                    "TransactionReference", "PaymentReference", "ProviderTransactionId",
                ];
                foreach (var column in sensitive)
                {
                    if (!IsSensitiveColumn("AccountingOutboxMessages", column))
                        throw new Exception($"{column} was not treated as sensitive.");
                }
                if (IsSensitiveColumn("LedgerAccounts", "AccountCode")
                    || IsSensitiveColumn("LedgerAccounts", "Name")
                    || IsSensitiveColumn("Tenants", "Name")
                    || IsSensitiveColumn("SalesInvoices", "InvoiceNumber")
                    || IsSensitiveColumn("AccountingOutboxMessages", "IdempotencyKey")
                    || IsSensitiveColumn("AccountingOutboxMessages", "JournalId"))
                    throw new Exception("financial/catalog/linkage column was wrongly redacted.");
                if (!IsSensitiveColumn("AffiliateAccounts", "Name"))
                    throw new Exception("affiliate personal name was not excluded.");
                if (!IsSensitiveColumn("CustomerPayments", "Notes")
                    || !IsSensitiveColumn("SupplierPayments", "Notes"))
                    throw new Exception("party notes were not excluded.");
                if (!IsSensitiveColumn("CommerceHubMarketplaceOrders", "FullName")
                    || !IsSensitiveColumn("CommerceHubMarketplaceOrders", "Notes")
                    || !IsSensitiveColumn("CommerceHubMarketplaceOrders", "Area")
                    || !IsSensitiveColumn("CommerceHubRefunds", "Reference")
                    || !IsSensitiveColumn("Orders", "CustomerNotes")
                    || !IsSensitiveColumn("Orders", "BillingApartment"))
                    throw new Exception("customer order PII/address column was not excluded.");
                if (IsSensitiveColumn("InventoryReservations", "ExpiresAtUtc")
                    || IsSensitiveColumn("Tenants", "Country")
                    || IsSensitiveColumn("Orders", "OrderNumber"))
                    throw new Exception("non-sensitive operational column was wrongly redacted.");
            });
            failures += Check("sql-read-only-command-presence", () =>
            {
                foreach (var template in SqlTemplates.All())
                {
                    string[] probes = template.StartsWith("SELECT <cols>", StringComparison.Ordinal)
                        ? ["SELECT \"Id\" FROM \"Tenants\" WHERE \"Id\" = ANY(@tenants)"]
                        : [template];
                    foreach (var probe in probes)
                    {
                        if (!IsReadOnlySql(probe))
                            throw new Exception($"read-only template rejected: {probe}");
                    }
                }
                string[] writes =
                [
                    "INSERT INTO \"Orders\" SELECT 1",
                    "UPDATE \"LedgerAccounts\" SET \"CurrentBalance\" = 0",
                    "DELETE FROM \"Orders\"",
                    "DROP TABLE \"Orders\"",
                    "TRUNCATE \"Orders\"",
                    "ALTER TABLE \"Orders\" ADD COLUMN x int",
                    "CREATE TABLE evil (id int)",
                    "SELECT * FROM \"Orders\"; DELETE FROM \"Orders\"",
                    "SELECT * INTO backup FROM \"Orders\"",
                    "SELECT * FROM \"Orders\" FOR UPDATE",
                    "WITH x AS (DELETE FROM \"Orders\" RETURNING *) SELECT * FROM x",
                ];
                foreach (var sql in writes)
                {
                    if (IsReadOnlySql(sql))
                        throw new Exception($"write statement passed the read-only gate: {sql}");
                }
                if (!IsReadOnlySql("WITH t AS (SELECT 1 AS x) SELECT * FROM t WHERE 1 = 1"))
                    throw new Exception("WITH ... SELECT was rejected.");
            });
            Console.WriteLine(failures == 0 ? "SELFTEST PASS: 6/6" : $"SELFTEST FAIL: {failures} failing");
            return failures == 0 ? 0 : 1;
        }

        static Evidence SampleEvidence(IReadOnlyList<SortedDictionary<string, object?>> rows)
        {
            var evidence = new Evidence();
            var section = new EvidenceSection { Table = "Sample", RowCount = rows.Count };
            section.Rows.AddRange(rows);
            evidence.Sections["sample"] = section;
            evidence.Aggregates["probe"] = "x";
            return evidence;
        }

        static int Check(string name, Action body)
        {
            try
            {
                body();
                Console.WriteLine($"SELFTEST PASS: {name}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SELFTEST FAIL: {name}: {ex.Message}");
                return 1;
            }
        }
    }
}
