using System.Text.Json;
using Npgsql;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var settingsPath = Path.Combine(root, "PointOfSale.API", "appsettings.Development.local.json");
using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
var connectionString = settings.RootElement
    .GetProperty("ConnectionStrings")
    .GetProperty("DefaultConnection")
    .GetString() ?? throw new InvalidOperationException("Development database is not configured.");

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
{
    await readOnly.ExecuteNonQueryAsync();
}

Console.WriteLine("DATABASE");
await PrintAsync("SELECT current_database() AS database, inet_server_addr()::text AS host, inet_server_port() AS port, pg_is_in_recovery() AS is_replica");

Console.WriteLine("COUNTS");
await PrintAsync("""
    SELECT
      COUNT(*)::bigint AS cms_page_versions,
      COUNT(*) FILTER (WHERE "IsDraft" AND NOT "IsDeleted")::bigint AS active_drafts,
      COUNT(*) FILTER (WHERE "IsPublished" AND NOT "IsDeleted")::bigint AS active_published,
      COUNT(*) FILTER (WHERE "IsDeleted")::bigint AS deleted_versions
    FROM "CmsPages"
    """);

Console.WriteLine("DUPLICATES");
await PrintAsync("""
    SELECT 'draft_group' AS kind, COUNT(*)::bigint AS duplicate_sets
    FROM (SELECT "PageGroupId" FROM "CmsPages" WHERE "IsDraft" AND NOT "IsDeleted" GROUP BY "PageGroupId" HAVING COUNT(*) > 1) d
    UNION ALL
    SELECT 'published_group', COUNT(*)::bigint
    FROM (SELECT "PageGroupId" FROM "CmsPages" WHERE "IsPublished" AND NOT "IsDeleted" GROUP BY "PageGroupId" HAVING COUNT(*) > 1) p
    UNION ALL
    SELECT 'draft_slug', COUNT(*)::bigint
    FROM (SELECT "TenantId", "Slug" FROM "CmsPages" WHERE "IsDraft" AND NOT "IsDeleted" GROUP BY "TenantId", "Slug" HAVING COUNT(*) > 1) ds
    UNION ALL
    SELECT 'published_slug', COUNT(*)::bigint
    FROM (SELECT "TenantId", "Slug" FROM "CmsPages" WHERE "IsPublished" AND NOT "IsDeleted" GROUP BY "TenantId", "Slug" HAVING COUNT(*) > 1) ps
    ORDER BY kind
    """);

Console.WriteLine("INDEXES");
await PrintAsync("""
    SELECT indexname, indexdef
    FROM pg_indexes
    WHERE schemaname = 'public' AND tablename = 'CmsPages'
    ORDER BY indexname
    """);

Console.WriteLine("CONSTRAINTS");
await PrintAsync("""
    SELECT c.conname, c.contype, pg_get_constraintdef(c.oid) AS definition
    FROM pg_constraint c
    JOIN pg_class t ON t.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = t.relnamespace
    WHERE n.nspname = 'public' AND t.relname = 'CmsPages'
    ORDER BY c.conname
    """);

Console.WriteLine("MIGRATIONS");
await PrintAsync("""
    SELECT COUNT(*)::bigint AS applied_count, MAX("MigrationId") AS latest_migration
    FROM "__EFMigrationsHistory"
    """);

Console.WriteLine("TRANSLATION_TABLE");
await PrintAsync("SELECT to_regclass('public.\"CmsPageTranslations\"')::text AS relation");

Console.WriteLine("TRANSLATION_BACKFILL");
await PrintAsync("""
    SELECT
      COUNT(*)::bigint AS translation_rows,
      COUNT(*) FILTER (WHERE "Locale" = 'en')::bigint AS english_rows,
      COUNT(*) FILTER (WHERE "Locale" = 'ar')::bigint AS arabic_rows,
      COUNT(DISTINCT "CmsPageVersionId")::bigint AS covered_versions
    FROM "CmsPageTranslations"
    """);

Console.WriteLine("TRANSLATION_EQUIVALENCE");
await PrintAsync("""
    SELECT COUNT(*)::bigint AS mismatches
    FROM "CmsPages" AS page
    LEFT JOIN "CmsPageTranslations" AS translation
      ON translation."CmsPageVersionId" = page."Id"
     AND translation."Locale" = 'en'
    WHERE translation."CmsPageVersionId" IS NULL
       OR translation."TenantId" IS DISTINCT FROM page."TenantId"
       OR translation."Title" IS DISTINCT FROM page."Title"
       OR translation."Content" IS DISTINCT FROM page."Content"
       OR translation."ContentType" IS DISTINCT FROM page."ContentType"
       OR translation."SchemaVersion" IS DISTINCT FROM page."SchemaVersion"
       OR translation."MetaTitle" IS DISTINCT FROM page."MetaTitle"
       OR translation."MetaDescription" IS DISTINCT FROM page."MetaDescription"
       OR translation."MetaKeywords" IS DISTINCT FROM page."MetaKeywords"
       OR translation."OpenGraphImageUrl" IS DISTINCT FROM page."OpenGraphImageUrl"
    """);

await transaction.RollbackAsync();

async Task PrintAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, connection, transaction);
    await using var reader = await command.ExecuteReaderAsync();
    var headers = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
    Console.WriteLine(string.Join(" | ", headers));
    while (await reader.ReadAsync())
    {
        Console.WriteLine(string.Join(" | ", Enumerable.Range(0, reader.FieldCount)
            .Select(index => reader.IsDBNull(index) ? "<null>" : reader.GetValue(index).ToString())));
    }
}
