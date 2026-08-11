using System.Text.Json;
using Npgsql;

static string? ReadSecretValue(JsonElement root, string key)
{
    if (root.TryGetProperty(key, out var flat) && flat.ValueKind == JsonValueKind.String)
    {
        return flat.GetString();
    }

    if (root.TryGetProperty("ConnectionStrings", out var connectionStrings)
        && connectionStrings.ValueKind == JsonValueKind.Object
        && connectionStrings.TryGetProperty("DefaultConnection", out var nested)
        && nested.ValueKind == JsonValueKind.String)
    {
        return nested.GetString();
    }

    return null;
}

static string? ResolveConnectionString()
{
    var fromEnvironment = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
    if (!string.IsNullOrWhiteSpace(fromEnvironment))
    {
        return fromEnvironment.Trim();
    }

    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    var secretsRoot = Path.Combine(appData, "Microsoft", "UserSecrets");
    if (!Directory.Exists(secretsRoot))
    {
        return null;
    }

    foreach (var secretsFile in Directory.EnumerateFiles(secretsRoot, "secrets.json", SearchOption.AllDirectories))
    {
        using var stream = File.OpenRead(secretsFile);
        using var document = JsonDocument.Parse(stream);
        var value = ReadSecretValue(document.RootElement, "ConnectionStrings:DefaultConnection");
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }
    }

    return null;
}

static void PrintRows(NpgsqlConnection connection, string label, string sql)
{
    Console.WriteLine($"[{label}]");
    using var command = new NpgsqlCommand(sql, connection);
    using var reader = command.ExecuteReader();
    var rowCount = 0;

    while (reader.Read())
    {
        rowCount++;
        var values = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            values[i] = reader.IsDBNull(i) ? "<NULL>" : Convert.ToString(reader.GetValue(i)) ?? "";
        }

        Console.WriteLine(string.Join(" | ", values));
    }

    if (rowCount == 0)
    {
        Console.WriteLine("<none>");
    }
}

var connectionString = ResolveConnectionString();
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("DefaultConnection was not found in environment variables or user secrets.");
}

using var connection = new NpgsqlConnection(connectionString);
connection.Open();

using (var databaseCommand = new NpgsqlCommand("select current_database();", connection))
{
    Console.WriteLine($"[Database] {databaseCommand.ExecuteScalar()}");
}

PrintRows(
    connection,
    "Migration",
    """
    select "MigrationId"
    from "__EFMigrationsHistory"
    where "MigrationId" = '20260809164343_DOMAIN_5_2_CertificatePersistenceFreeze';
    """);

PrintRows(
    connection,
    "SslCertificateColumns",
    """
    select column_name, data_type, is_nullable
    from information_schema.columns
    where table_schema = current_schema()
      and table_name = 'SslCertificates'
      and column_name in (
        'TenantId', 'DomainId', 'NormalizedHostName', 'Status', 'OperationPhase',
        'Generation', 'OperationId', 'IdempotencyKey', 'ProviderRequestReference',
        'OrderReference', 'AuthorizationReference', 'ChallengeReference',
        'EdgeCertificateReference', 'CaddyStorageReference', 'SubjectAlternativeNamesJson',
        'ActivatedAtUtc', 'ReplacementCertificateId', 'FencingToken',
        'LockOwnerReference', 'LeaseExpiresAtUtc')
    order by column_name;
    """);

PrintRows(
    connection,
    "OperationColumns",
    """
    select column_name, data_type, is_nullable
    from information_schema.columns
    where table_schema = current_schema()
      and table_name = 'SslCertificateOperations'
      and column_name in (
        'TenantId', 'DomainId', 'CertificateId', 'NormalizedHostName',
        'OperationType', 'Status', 'Phase', 'IdempotencyKey',
        'FencingToken', 'LeaseExpiresAtUtc', 'RetryAfterUtc')
    order by column_name;
    """);

PrintRows(
    connection,
    "Constraints",
    """
    select t.relname, c.conname, c.contype, pg_get_constraintdef(c.oid)
    from pg_constraint c
    join pg_class t on t.oid = c.conrelid
    where t.relname in ('SslCertificates', 'SslCertificateOperations', 'Domains')
      and c.conname in (
        'AK_Domains_TenantId_Id',
        'FK_SslCertificates_DomainId',
        'FK_SslCertificateOperations_DomainId',
        'CK_SslCertificates_Status_Canonical',
        'CK_SslCertificates_Generation_Positive',
        'CK_SslCertificates_Revoked_Metadata',
        'CK_SslCertificates_Expired_Metadata',
        'CK_SslCertificates_Failed_Metadata',
        'CK_SslCertificateOperations_IdempotencyKey_NotBlank',
        'CK_SslCertificateOperations_Fencing_NonNegative')
    order by t.relname, c.conname;
    """);

PrintRows(
    connection,
    "Indexes",
    """
    select tablename, indexname, indexdef
    from pg_indexes
    where schemaname = current_schema()
      and tablename in ('SslCertificates', 'SslCertificateOperations')
      and indexname in (
        'IX_SslCertificates_TenantId_DomainId_IsDeleted',
        'IX_SslCertificates_TenantId_NormalizedHostName_Status',
        'IX_SslCertificates_NextRenewalUtc_Status',
        'IX_SslCertificates_NotAfterUtc_Status',
        'IX_SslCertificates_RetryAfterUtc_Status',
        'IX_SslCertificates_Status_OperationPhase',
        'UQ_SslCertificates_TenantId_DomainId_Generation',
        'UQ_SslCertificates_TenantId_DomainId_ActiveActivatingGeneration',
        'UQ_SslCertificates_TenantId_DomainId_Active',
        'UQ_SslCertificates_TenantId_DomainId_IdempotencyKey',
        'UQ_SslCertificateOperations_Tenant_Domain_Type_IdempotencyKey',
        'UQ_SslCertificateOperations_Tenant_Domain_Type_InProgress',
        'IX_SslCertificateOperations_Tenant_Domain_FencingToken',
        'IX_SslCertificateOperations_LeaseExpiresAtUtc_Status',
        'IX_SslCertificateOperations_RetryAfterUtc_Status')
    order by tablename, indexname;
    """);

PrintRows(
    connection,
    "SecretColumns",
    """
    select table_name, column_name
    from information_schema.columns
    where table_schema = current_schema()
      and table_name in ('SslCertificates', 'SslCertificateOperations')
      and (
        column_name ilike '%private%key%'
        or column_name ilike '%certificatepem%'
        or column_name ilike '%pfx%'
        or column_name ilike '%pkcs%'
        or column_name ilike '%acme%secret%'
        or column_name ilike '%challenge%credential%'
        or column_name ilike '%dns%credential%'
        or column_name ilike '%certificateblob%'
        or column_name ilike '%serialized%certificate%');
    """);
