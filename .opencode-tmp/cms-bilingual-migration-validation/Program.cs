using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PointOfSale.Infrastructure.Data;

const string previousMigration = "20260926215531_StorefrontFooterCmsPageIdentity";
const string targetMigration = "20260927210427_BilingualCmsPageTranslations";

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var settingsPath = Path.Combine(root, "PointOfSale.API", "appsettings.Development.local.json");
using var settings = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
var sourceConnectionString = settings.RootElement
    .GetProperty("ConnectionStrings")
    .GetProperty("DefaultConnection")
    .GetString() ?? throw new InvalidOperationException("Development database is not configured.");

var sourceBuilder = new NpgsqlConnectionStringBuilder(sourceConnectionString);
var scratchDatabase = $"pos_cms_bilingual_validation_{Guid.NewGuid():N}";
var adminBuilder = new NpgsqlConnectionStringBuilder(sourceConnectionString) { Database = "postgres" };
var scratchBuilder = new NpgsqlConnectionStringBuilder(sourceConnectionString) { Database = scratchDatabase };

await using var adminConnection = new NpgsqlConnection(adminBuilder.ConnectionString);
await adminConnection.OpenAsync();

try
{
    await ExecuteAdminAsync($"CREATE DATABASE \"{scratchDatabase}\"");

    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql(scratchBuilder.ConnectionString)
        .Options;
    await using (var dbContext = new ApplicationDbContext(options))
    {
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(previousMigration);
    }

    await using (var scratchConnection = new NpgsqlConnection(scratchBuilder.ConnectionString))
    {
        await scratchConnection.OpenAsync();
        await using var seed = new NpgsqlCommand(
            """
            INSERT INTO "CmsPages" (
                "Id", "PageGroupId", "TenantId", "Slug", "Title", "Content", "ContentType",
                "Version", "SchemaVersion", "IsDeleted", "IsDraft", "IsPublished", "PublishedAt",
                "MetaTitle", "MetaDescription", "MetaKeywords", "OpenGraphImageUrl", "CreatedAt", "UpdatedAt")
            VALUES
                (@publishedId, @groupOne, @tenantId, 'about', 'About', '<p>English published</p>', 'RichText',
                 1, 1, FALSE, FALSE, TRUE, NOW(), 'About SEO', 'Published description', 'about,cms', '/media/about.png', NOW(), NOW()),
                (@draftId, @groupOne, @tenantId, 'about', 'About draft', '[{"type":"text","data":{"text":"Draft"}}]', 'Blocks',
                 2, 1, FALSE, TRUE, FALSE, NULL, NULL, NULL, NULL, NULL, NOW(), NOW()),
                (@deletedId, @groupTwo, @tenantId, 'retired', 'Retired', '<p>Retired</p>', 'RichText',
                 1, 1, TRUE, FALSE, FALSE, NULL, NULL, NULL, NULL, NULL, NOW(), NOW());
            """,
            scratchConnection);
        seed.Parameters.AddWithValue("publishedId", Guid.NewGuid());
        seed.Parameters.AddWithValue("draftId", Guid.NewGuid());
        seed.Parameters.AddWithValue("deletedId", Guid.NewGuid());
        seed.Parameters.AddWithValue("groupOne", Guid.NewGuid());
        seed.Parameters.AddWithValue("groupTwo", Guid.NewGuid());
        seed.Parameters.AddWithValue("tenantId", Guid.NewGuid());
        await seed.ExecuteNonQueryAsync();
    }

    await using (var dbContext = new ApplicationDbContext(options))
    {
        var migrator = dbContext.GetService<IMigrator>();
        await migrator.MigrateAsync(targetMigration);
    }

    await using (var scratchConnection = new NpgsqlConnection(scratchBuilder.ConnectionString))
    {
        await scratchConnection.OpenAsync();
        await AssertScalarAsync(scratchConnection,
            "SELECT COUNT(*) FROM \"CmsPageTranslations\" WHERE \"Locale\" = 'en'",
            3L,
            "Every retained version receives one English translation");
        await AssertScalarAsync(scratchConnection,
            "SELECT COUNT(*) FROM \"CmsPageTranslations\" WHERE \"Locale\" = 'ar'",
            0L,
            "Backfill does not fabricate Arabic translations");
        await AssertScalarAsync(scratchConnection,
            """
            SELECT COUNT(*)
            FROM "CmsPages" page
            JOIN "CmsPageTranslations" translation
              ON translation."CmsPageVersionId" = page."Id" AND translation."Locale" = 'en'
            WHERE translation."TenantId" IS DISTINCT FROM page."TenantId"
               OR translation."Title" IS DISTINCT FROM page."Title"
               OR translation."Content" IS DISTINCT FROM page."Content"
               OR translation."ContentType" IS DISTINCT FROM page."ContentType"
               OR translation."SchemaVersion" IS DISTINCT FROM page."SchemaVersion"
               OR translation."MetaTitle" IS DISTINCT FROM page."MetaTitle"
               OR translation."MetaDescription" IS DISTINCT FROM page."MetaDescription"
               OR translation."MetaKeywords" IS DISTINCT FROM page."MetaKeywords"
               OR translation."OpenGraphImageUrl" IS DISTINCT FROM page."OpenGraphImageUrl"
            """,
            0L,
            "English backfill is lossless");
        await AssertScalarAsync(scratchConnection,
            "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = 'public' AND tablename = 'CmsPages' AND indexname IN ('IX_CmsPages_PageGroupId', 'UQ_CmsPages_PageGroupId_Draft', 'UQ_CmsPages_PageGroupId_Published', 'UQ_CmsPages_Tenant_Draft_Slug', 'UQ_CmsPages_Tenant_Published_Slug')",
            5L,
            "All draft, published, and lookup indexes exist");
        await AssertScalarAsync(scratchConnection,
            "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = '20260927210427_BilingualCmsPageTranslations'",
            1L,
            "Target migration is recorded");

        Console.WriteLine("PASS: disposable PostgreSQL migration validation completed.");
    }
}
finally
{
    NpgsqlConnection.ClearAllPools();
    await using var terminate = new NpgsqlCommand(
        "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @database AND pid <> pg_backend_pid()",
        adminConnection);
    terminate.Parameters.AddWithValue("database", scratchDatabase);
    await terminate.ExecuteNonQueryAsync();
    await ExecuteAdminAsync($"DROP DATABASE IF EXISTS \"{scratchDatabase}\"");
}

async Task ExecuteAdminAsync(string sql)
{
    await using var command = new NpgsqlCommand(sql, adminConnection);
    await command.ExecuteNonQueryAsync();
}

static async Task AssertScalarAsync(NpgsqlConnection connection, string sql, long expected, string invariant)
{
    await using var command = new NpgsqlCommand(sql, connection);
    var actual = Convert.ToInt64(await command.ExecuteScalarAsync());
    if (actual != expected)
    {
        throw new InvalidOperationException($"Migration invariant failed: {invariant}. Expected {expected}, found {actual}.");
    }

    Console.WriteLine($"PASS: {invariant}.");
}
