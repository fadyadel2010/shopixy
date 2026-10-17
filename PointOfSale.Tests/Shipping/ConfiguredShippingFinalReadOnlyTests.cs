using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using Xunit.Abstractions;

namespace PointOfSale.Tests.Shipping;

public sealed class ConfiguredShippingFinalReadOnlyTests(ITestOutputHelper output)
{
    [ConfiguredFinalReadOnlyFact]
    public async Task RemainingFinal_OriginalProtectedRowsAndExplainedOperationalAuditAdditions_ReadOnly()
    {
        var evidence = Environment.GetEnvironmentVariable("SHIPPING_FINAL_EVIDENCE_DIRECTORY")!;
        var previousPhase = Environment.GetEnvironmentVariable("SHIPPING_FINAL_OBSERVER_PHASE");
        try
        {
            // Keep the original strict 43-table comparison and its failed result.
            // This separately named observation explains additions; it cannot
            // replace the original baseline or delete immutable audit history.
            Environment.SetEnvironmentVariable("SHIPPING_FINAL_OBSERVER_PHASE", "remaining-final");
            await ExistingConfiguredDatabase_FinalSchemaCatalogAndProtectedFinancialSnapshot_ReadOnly();
        }
        finally { Environment.SetEnvironmentVariable("SHIPPING_FINAL_OBSERVER_PHASE", previousPhase); }
        await HistoricalOperationalAuditDrift_ExplainExactAdditionsWithoutRebaseline_ReadOnly();
        using var before = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(evidence, "configured-before.json")));
        using var current = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(evidence, "configured-remaining-final.json")));
        using var drift = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(evidence, "operational-ledger-drift-explanation.json")));
        var original = before.RootElement.GetProperty("Financial"); var after = current.RootElement.GetProperty("Financial");
        Assert.Equal(43, original.EnumerateObject().Count()); Assert.Equal(43, after.EnumerateObject().Count());
        foreach (var entry in original.EnumerateObject().Where(p => p.Name != "CommerceHubRecoveryOperationLedgerEntries"))
            Assert.Equal(entry.Value.GetRawText().Replace(" ", "").Replace("\r", "").Replace("\n", ""), after.GetProperty(entry.Name).GetRawText().Replace(" ", "").Replace("\r", "").Replace("\n", ""));
        var additions = drift.RootElement.GetProperty("Additions").EnumerateArray().ToArray();
        Assert.All(additions, item => {
            Assert.Equal("AutoHealing", item.GetProperty("OperationType").GetString());
            Assert.Equal("system:CommerceHubAutoHealingService", item.GetProperty("Actor").GetString());
            Assert.Equal("listing", item.GetProperty("Scope").GetString());
            Assert.Equal("Succeeded", item.GetProperty("Outcome").GetString());
        });
        Assert.Equal(long.Parse(original.GetProperty("CommerceHubRecoveryOperationLedgerEntries")[0].GetString()!) + additions.Length,
            long.Parse(after.GetProperty("CommerceHubRecoveryOperationLedgerEntries")[0].GetString()!));
        await File.WriteAllTextAsync(Path.Combine(evidence, "protected-preservation-final.json"), JsonSerializer.Serialize(new {
            UnchangedWholeTables=42, OriginalOperationalAuditRows=83233, OriginalOperationalRowsFingerprintMatched=true,
            ExplainedOperationalAuditAdditions=additions.Length, OriginalStrictWholeTableComparisonPassed=false,
            BaselineReplaced=false, ImmutableRowsDeleted=false, ObservedUtc=DateTime.UtcNow }));
    }

    [ConfiguredFinalReadOnlyFact]
    public async Task HistoricalOperationalAuditDrift_ExplainExactAdditionsWithoutRebaseline_ReadOnly()
    {
        var evidence = Environment.GetEnvironmentVariable("SHIPPING_FINAL_EVIDENCE_DIRECTORY")!;
        using var before = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(evidence, "configured-before.json")));
        var observed = before.RootElement.GetProperty("ObservedUtc").GetDateTime().ToUniversalTime();
        var expected = before.RootElement.GetProperty("Financial").GetProperty("CommerceHubRecoveryOperationLedgerEntries");
        await using var db = ResolveDevelopment(); await using var c = new NpgsqlConnection(db.Database.GetConnectionString()); await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await using (var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", c, tx)) await ro.ExecuteNonQueryAsync();
        var rows = new List<object>();
        await using (var q = new NpgsqlCommand("SELECT \"Id\",\"OperationType\",\"ActorIdentity\",\"Scope\",\"Outcome\",\"StartedAtUtc\",\"EndedAtUtc\",xmin::text FROM \"CommerceHubRecoveryOperationLedgerEntries\" WHERE \"EndedAtUtc\">@snapshot ORDER BY \"EndedAtUtc\",\"Id\"", c, tx))
        {
            q.Parameters.AddWithValue("snapshot", observed); await using var r = await q.ExecuteReaderAsync();
            while (await r.ReadAsync()) rows.Add(new { Id=r.GetGuid(0), OperationType=r.GetString(1), Actor=r.GetString(2), Scope=r.GetString(3), Outcome=r.GetString(4), StartedUtc=r.GetDateTime(5), EndedUtc=r.GetDateTime(6), Xmin=r.GetString(7) });
        }
        await using var original = new NpgsqlCommand("SELECT count(*)::text,md5(coalesce(string_agg(h,'' ORDER BY h),'')) FROM (SELECT md5(xmin::text || ':' || to_jsonb(t)::text) h FROM \"CommerceHubRecoveryOperationLedgerEntries\" t WHERE \"EndedAtUtc\"<=@snapshot) s", c, tx);
        original.Parameters.AddWithValue("snapshot", observed); await using var read = await original.ExecuteReaderAsync(); Assert.True(await read.ReadAsync());
        var count = read.GetString(0); var hash = read.GetString(1);
        await File.WriteAllTextAsync(Path.Combine(evidence, "operational-ledger-drift-explanation.json"), JsonSerializer.Serialize(new {
            OriginalSnapshotUtc=observed, OriginalRetainedCount=count, OriginalRetainedHash=hash, Additions=rows, Rebaseline=false, Deletion=false, ReadOnly=true, ObservedUtc=DateTime.UtcNow }));
        Assert.Equal(expected[0].GetString(), count); Assert.Equal(expected[1].GetString(), hash);
        await read.DisposeAsync(); await tx.RollbackAsync();
    }

    [ConfiguredFinalReadOnlyFact]
    public async Task RemainingGates_AccountCatalogOutboxAndPermanentFinancialGuards_ReadOnly()
    {
        await using var db = ResolveDevelopment();
        await using var c = new NpgsqlConnection(db.Database.GetConnectionString()); await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await using (var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", c, tx)) await ro.ExecuteNonQueryAsync();
        async Task<string[][]> Query(string sql)
        {
            await using var q = new NpgsqlCommand(sql, c, tx); await using var r = await q.ExecuteReaderAsync();
            var rows = new List<string[]>();
            while (await r.ReadAsync()) rows.Add(Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "" : r.GetValue(i).ToString()!).ToArray());
            return rows.ToArray();
        }
        var accounts = await Query("SELECT \"TenantId\"::text,\"Id\"::text,\"CarrierCode\",\"State\"::text,\"OperationalState\"::text FROM \"CarrierAccounts\" ORDER BY \"TenantId\",\"Id\"");
        var prerequisites = await Query("SELECT (SELECT count(*) FROM \"CarrierCredentialReferences\")::text,(SELECT count(*) FROM \"CarrierServiceLevelEnablements\")::text,(SELECT count(*) FROM \"GeographicCatalogReleases\")::text,(SELECT count(*) FROM \"GeographicCatalogPublications\")::text");
        var credentialStates = await Query("SELECT \"TenantId\"::text,\"CarrierAccountId\"::text,\"State\"::text,coalesce(\"ExpiresAtUtc\"::text,''),(length(\"SecretReference\")>0)::text FROM \"CarrierCredentialReferences\" ORDER BY \"TenantId\",\"CarrierAccountId\"");
        var connectionTests = await Query("SELECT \"TenantId\"::text,\"CarrierAccountId\"::text,\"Status\"::text,\"TestedAtUtc\"::text FROM \"CarrierConnectionTestRecords\" ORDER BY \"TenantId\",\"CarrierAccountId\"");
        var outbox = await Query("SELECT \"EventType\",count(*)::text,count(*) FILTER(WHERE \"ProcessedAtUtc\" IS NULL)::text,count(*) FILTER(WHERE \"RetryCount\">=5 AND \"ProcessedAtUtc\" IS NULL)::text FROM \"OutboxMessages\" GROUP BY \"EventType\" ORDER BY \"EventType\"");
        var guards = await Query("SELECT t.tgname,t.tgenabled::text,p.proname,pg_get_functiondef(p.oid) FROM pg_trigger t JOIN pg_proc p ON p.oid=t.tgfoid WHERE NOT t.tgisinternal AND p.proname IN ('carrier_remittance_immutable','carrier_cod_evidence_immutable') ORDER BY t.tgname");
        Assert.NotEmpty(guards); Assert.All(guards, g => Assert.Equal("O", g[1]));
        var evidence = Environment.GetEnvironmentVariable("SHIPPING_FINAL_EVIDENCE_DIRECTORY")!;
        await File.WriteAllTextAsync(Path.Combine(evidence, "remaining-prerequisites-readonly.json"), JsonSerializer.Serialize(new {
            AccountsWithoutCredentials = accounts, CredentialServiceReleasePublicationCounts = prerequisites, CredentialStateWithoutSecrets=credentialStates, ConnectionTestMetadata=connectionTests,
            OutboxTypeTotalPendingExhausted = outbox, PermanentFinancialGuards = guards, ReadOnly = true, ObservedUtc = DateTime.UtcNow }));
        await tx.RollbackAsync();
    }

    internal static ApplicationDbContext ResolveDevelopment()
    {
        var old = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var directory = Directory.GetCurrentDirectory();
        try
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            Directory.SetCurrentDirectory(Path.Combine(TestConfiguration.GetSolutionRoot(), "PointOfSale.Infrastructure"));
            return new ApplicationDbContextFactory().CreateDbContext([]);
        }
        finally { Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", old); Directory.SetCurrentDirectory(directory); }
    }

    [ConfiguredFinalReadOnlyFact]
    public async Task ExistingConfiguredDatabase_FinalSchemaCatalogAndProtectedFinancialSnapshot_ReadOnly()
    {
        await using var db = ResolveDevelopment();
        var cs = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        Assert.Equal("192.168.1.5", cs.Host); Assert.Equal(5432, cs.Port); Assert.Equal("PosEcommerceDb_Baseline", cs.Database);
        await using var c = new NpgsqlConnection(cs.ConnectionString); await c.OpenAsync();
        await using var tx = await c.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await using (var ro = new NpgsqlCommand("SET TRANSACTION READ ONLY", c, tx)) await ro.ExecuteNonQueryAsync();
        async Task<string[][]> Query(string sql)
        {
            await using var cmd = new NpgsqlCommand(sql, c, tx);
            await using var r = await cmd.ExecuteReaderAsync(); var rows = new List<string[]>();
            while (await r.ReadAsync()) rows.Add(Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "" : r.GetValue(i).ToString()!).ToArray());
            return rows.ToArray();
        }
        Assert.Equal("on", (await Query("SELECT current_setting('transaction_read_only')"))[0][0]);
        var history = await Query("SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"");
        Assert.Equal(db.Database.GetMigrations(), history.Select(r => r[0])); Assert.Equal(53, history.Length);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var column = await Query("SELECT data_type,is_nullable FROM information_schema.columns WHERE table_schema='public' AND table_name='AddressAdmissionEvidence' AND column_name='CanonicalAddressEvidenceJson'");
        Assert.Equal(new[] { "text", "YES" }, Assert.Single(column));
        var triggers = await Query("SELECT t.tgname,t.tgenabled::text,p.proname FROM pg_trigger t JOIN pg_proc p ON p.oid=t.tgfoid WHERE NOT t.tgisinternal AND p.proname IN ('shopixy_guard_canonical_admission','shopixy_guard_canonical_order','shopixy_guard_canonical_shipment') ORDER BY t.tgname");
        Assert.Equal(3, triggers.Length); Assert.All(triggers, t => Assert.Equal("O", t[1]));
        var catalog = await Query("SELECT (SELECT count(*) FROM \"GeographicCatalogReleases\")::text,(SELECT count(*) FROM \"GeographicCatalogPublications\")::text,(SELECT count(*) FROM \"AddressAdmissionEvidence\" WHERE \"CanonicalAddressEvidenceJson\" IS NOT NULL)::text");
        var dependencies = await Query("SELECT conrelid::regclass::text,confrelid::regclass::text,pg_get_constraintdef(oid) FROM pg_constraint WHERE contype='f' AND confrelid IN ('\"Orders\"'::regclass,'\"Shipments\"'::regclass) ORDER BY conrelid::regclass::text,conname");
        var financial = new Dictionary<string, string[]>();
        var tables = (await Query("SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename")).Select(r => r[0]).ToArray();
        foreach (var table in tables.Where(t => t.Contains("Ledger", StringComparison.Ordinal) || t.Contains("Settlement", StringComparison.Ordinal) ||
            t.Contains("Remittance", StringComparison.Ordinal) || t.Contains("Collection", StringComparison.Ordinal) || t.Contains("Cashbox", StringComparison.Ordinal) ||
            t.Contains("Treasury", StringComparison.Ordinal) || t.Contains("Payment", StringComparison.Ordinal) || t is "AccountingOutboxMessages" or "OrderRefunds"))
        {
            var q = "\"" + table.Replace("\"", "\"\"") + "\"";
            financial[table] = (await Query($"SELECT count(*)::text,md5(coalesce(string_agg(h,'' ORDER BY h),'')) FROM (SELECT md5(xmin::text || ':' || to_jsonb(t)::text) h FROM public.{q} t) s"))[0];
        }
        var e = Environment.GetEnvironmentVariable("SHIPPING_FINAL_EVIDENCE_DIRECTORY")!;
        var phase = Environment.GetEnvironmentVariable("SHIPPING_FINAL_OBSERVER_PHASE") ?? "before";
        var cleanupTenants = new List<Guid>();
        var tenantTables = (await Query("SELECT table_name FROM information_schema.columns WHERE table_schema='public' AND column_name='TenantId' ORDER BY table_name")).Select(r => r[0]).ToArray();
        foreach (var manifest in Directory.GetFiles(e, "rollback-*.json").Concat(Directory.GetFiles(e, "concurrency-*.json")))
        {
            using var m = JsonDocument.Parse(await File.ReadAllTextAsync(manifest));
            var tenant = m.RootElement.TryGetProperty("Tenant", out var upper) ? upper.GetGuid() : m.RootElement.GetProperty("tenant").GetGuid();
            cleanupTenants.Add(tenant);
        }
        var ids = string.Join(',', cleanupTenants.Distinct().Select(t => $"'{t}'::uuid"));
        var leaks = ids.Length == 0 ? [] : await Query(string.Join(" UNION ALL ", tenantTables.Select(table =>
            $"SELECT '{table}',\"TenantId\"::text,count(*)::text FROM public.\"{table.Replace("\"", "\"\"")}\" WHERE \"TenantId\" IN ({ids}) GROUP BY \"TenantId\"")));
        var tenantLeaks = ids.Length == 0 ? [] : await Query($"SELECT 'Tenants',\"Id\"::text,'1' FROM \"Tenants\" WHERE \"Id\" IN ({ids})");
        var globalIdentityLeaks = ids.Length == 0 ? [] : await Query($"SELECT 'CanonicalIdentityLinks-global',\"SubjectId\"::text,'1' FROM \"CanonicalIdentityLinks\" WHERE \"SubjectId\" IN ({ids})");
        await File.WriteAllTextAsync(Path.Combine(e, "synthetic-cleanup-observation.json"), JsonSerializer.Serialize(new { ManifestCount = cleanupTenants.Count, Leaks = leaks.Concat(tenantLeaks).Concat(globalIdentityLeaks).ToArray() }));
        var evidence = new { Target = new { cs.Host, cs.Port, cs.Database }, History = history, Column = column, Triggers = triggers, CatalogCounts = catalog,
            OrderShipmentForeignKeys = dependencies, Financial = financial, IndependentlyCleanedSyntheticTenants = cleanupTenants, ReadOnly = true, ObservedUtc = DateTime.UtcNow };
        await File.WriteAllTextAsync(Path.Combine(e, $"configured-{phase}.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
        Assert.Empty(leaks); Assert.Empty(tenantLeaks); Assert.Empty(globalIdentityLeaks);
        if (phase == "after")
        {
            var before = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(e, "configured-before.json")));
            Assert.Equal(before.RootElement.GetProperty("Financial").GetRawText().Replace("\r", "").Replace("\n", "").Replace(" ", ""),
                JsonSerializer.Serialize(financial));
        }
        await tx.RollbackAsync();
        output.WriteLine($"Configured PostgreSQL: 53/53, pending=0, nullable text column, 3 enabled triggers; catalog releases/publications/canonical payloads={string.Join('/', catalog[0])}; {financial.Count} protected financial tables fingerprinted; phase={phase}; READ ONLY rollback.");
    }
}

public sealed class ConfiguredFinalReadOnlyFactAttribute : FactAttribute
{
    public ConfiguredFinalReadOnlyFactAttribute()
    { if (Environment.GetEnvironmentVariable("SHIPPING_FINAL_READONLY") != "1") Skip = "Explicit configured read-only observation required."; }
}
