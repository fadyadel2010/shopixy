using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("POS_DOMAIN512_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("POS_DOMAIN512_CONNECTION_STRING is not configured.");
}

var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString)
{
    SslMode = SslMode.Disable,
    Timeout = 5,
    CommandTimeout = 15
};

var checks = new (string Name, string Sql)[]
{
    ("MigrationHistoryCount", "select count(*) from \"__EFMigrationsHistory\";"),
    ("LatestMigration", "select \"MigrationId\" from \"__EFMigrationsHistory\" order by \"MigrationId\" desc limit 1;"),
    ("DomainRows", "select count(*) from \"Domains\";"),
    ("ActiveDomainRows", "select count(*) from \"Domains\" where \"IsDeleted\" = false;"),
    ("SslCertificateRows", "select count(*) from \"SslCertificates\";"),
    ("SslCertificateOperationRows", "select count(*) from \"SslCertificateOperations\";"),
    ("RevocationOperationRows", "select count(*) from \"SslCertificateOperations\" where \"OperationType\" = 3;"),
    ("DomainAuditRows", "select count(*) from \"DomainAuditEntries\";"),
    ("DomainEventOutboxRows", "select count(*) from \"OutboxMessages\" where \"Type\" ilike '%Domain%' or \"Type\" ilike '%Certificate%';"),
    ("HangfireSchemaRows", "select count(*) from information_schema.schemata where schema_name = 'hangfire';"),
    ("HangfireServerRows", "select case when exists (select 1 from information_schema.tables where table_schema = 'hangfire' and table_name = 'server') then (select count(*) from hangfire.server) else null end;"),
    ("HangfireRecurringRows", "select case when exists (select 1 from information_schema.tables where table_schema = 'hangfire' and table_name = 'hash') then (select count(*) from hangfire.hash where key like 'recurring-job:%') else null end;"),
};

await using var connection = new NpgsqlConnection(connectionStringBuilder.ConnectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync();
await using (var readOnly = new NpgsqlCommand("set transaction read only;", connection, transaction))
{
    await readOnly.ExecuteNonQueryAsync();
}

foreach (var check in checks)
{
    try
    {
        await using var command = new NpgsqlCommand(check.Sql, connection, transaction);
        var value = await command.ExecuteScalarAsync();
        Console.WriteLine($"{check.Name}: {value ?? "NULL"}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{check.Name}: ERROR:{ex.GetType().Name}");
    }
}

await transaction.RollbackAsync();
