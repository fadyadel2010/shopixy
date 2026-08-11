using System.Text.Json;
using Npgsql;

var configPath = Path.GetFullPath(Path.Combine(
    Directory.GetCurrentDirectory(),
    "PointOfSale.API",
    "appsettings.Development.local.json"));

using var configJson = JsonDocument.Parse(File.ReadAllText(configPath));
var connectionString = configJson.RootElement
    .GetProperty("ConnectionStrings")
    .GetProperty("DefaultConnection")
    .GetString();

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("DefaultConnection is not configured.");
}

var checks = new (string Name, string Sql)[]
{
    ("MigrationHistory", "select count(*) from \"__EFMigrationsHistory\";"),
    ("DomainRows", "select count(*) from \"Domains\";"),
    ("ActiveDomainRows", "select count(*) from \"Domains\" where \"IsDeleted\" = false;"),
    ("PendingVerificationDomains", "select count(*) from \"Domains\" where \"IsDeleted\" = false and \"LifecycleState\" = 1;"),
    ("VerifiedServableDomains", "select count(*) from \"Domains\" where \"IsDeleted\" = false and \"LifecycleState\" in (2,4,5);"),
    ("DomainVerificationRows", "select count(*) from \"DomainVerifications\";"),
    ("DomainAuditRows", "select count(*) from \"DomainAuditEntries\";"),
    ("LatestDomainMigrationApplied", "select \"MigrationId\" from \"__EFMigrationsHistory\" where \"MigrationId\" like '%DOMAIN%' order by \"MigrationId\" desc limit 1;"),
    ("HardeningMigrationApplied", "select count(*) from \"__EFMigrationsHistory\" where \"MigrationId\" = '20260810181420_DOMAIN_Final_ForensicHardening';"),
    ("DuplicateVerificationAttempts", "select count(*) from (select \"DomainId\", \"Attempt\" from \"DomainVerifications\" where \"IsDeleted\" = false group by \"DomainId\", \"Attempt\" having count(*) > 1) q;"),
    ("PrimaryTypeStateMismatches", "select count(*) from \"Domains\" where (\"DomainType\" = 0) <> (\"LifecycleState\" = 5);"),
    ("RedirectInvariantViolations", "select count(*) from \"Domains\" where not ((\"DomainType\" = 2 and \"RedirectTargetDomainId\" is not null and \"RedirectTargetDomainId\" <> \"Id\" and \"RedirectType\" <> 0) or (\"DomainType\" <> 2 and \"RedirectTargetDomainId\" is null and \"RedirectType\" = 0));"),
    ("CrossTenantRedirects", "select count(*) from \"Domains\" d join \"Domains\" t on t.\"Id\" = d.\"RedirectTargetDomainId\" where d.\"TenantId\" <> t.\"TenantId\";"),
    ("CrossTenantCertificateOperations", "select count(*) from \"SslCertificateOperations\" o join \"SslCertificates\" c on c.\"Id\" = o.\"CertificateId\" where o.\"CertificateId\" is not null and (o.\"TenantId\", o.\"DomainId\") <> (c.\"TenantId\", c.\"DomainId\");"),
    ("CrossTenantCertificateReplacements", "select count(*) from \"SslCertificates\" c join \"SslCertificates\" r on r.\"Id\" = c.\"ReplacementCertificateId\" where c.\"ReplacementCertificateId\" is not null and (c.\"TenantId\", c.\"DomainId\") <> (r.\"TenantId\", r.\"DomainId\");"),
    ("DuplicateCertificateGenerations", "select count(*) from (select \"TenantId\", \"DomainId\", \"Generation\" from \"SslCertificates\" where \"IsDeleted\" = false group by \"TenantId\", \"DomainId\", \"Generation\" having count(*) > 1) q;"),
    ("DuplicateOperationIdempotencyKeys", "select count(*) from (select \"TenantId\", \"DomainId\", \"OperationType\", \"IdempotencyKey\" from \"SslCertificateOperations\" where \"IsDeleted\" = false group by \"TenantId\", \"DomainId\", \"OperationType\", \"IdempotencyKey\" having count(*) > 1) q;"),
    ("MultipleInProgressOperationsPerType", "select count(*) from (select \"TenantId\", \"DomainId\", \"OperationType\" from \"SslCertificateOperations\" where \"IsDeleted\" = false and \"Status\" in (0,1) group by \"TenantId\", \"DomainId\", \"OperationType\" having count(*) > 1) q;"),
};

var builder = new NpgsqlConnectionStringBuilder(connectionString)
{
    SslMode = SslMode.Disable,
};
await using var connection = new NpgsqlConnection(builder.ConnectionString);
await connection.OpenAsync();
await using (var readOnly = new NpgsqlCommand("set default_transaction_read_only = on;", connection))
{
    await readOnly.ExecuteNonQueryAsync();
}

foreach (var check in checks)
{
    await using var command = new NpgsqlCommand(check.Sql, connection);
    var value = await command.ExecuteScalarAsync();
    Console.WriteLine($"{check.Name}: {value}");
}
