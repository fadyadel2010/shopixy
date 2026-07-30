using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Infrastructure.Idempotency;

public sealed class ProductMutationIdempotencyService : IProductMutationIdempotencyService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationDbContext _context;

    public ProductMutationIdempotencyService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ProductMutationIdempotencyClaim> TryClaimAsync(
        ProductMutationIdempotencyRequest request,
        CancellationToken cancellationToken = default)
    {
        var claimId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;
        int rowsInserted;

        if (IsPostgresProvider())
        {
            rowsInserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""ProductRequestIdempotency""
                (""Id"", ""TenantId"", ""Key"", ""Operation"", ""ProductId"", ""ProductVariantId"", ""RequestFingerprint"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                VALUES
                ({claimId}, {request.TenantId}, {request.Key}, {request.Operation}, {request.ProductId}, {request.ProductVariantId}, {request.RequestFingerprint}, {nowUtc}, {nowUtc}, {false})
                ON CONFLICT (""TenantId"", ""Key"", ""Operation"") DO NOTHING",
                cancellationToken);
        }
        else if (IsSqlServerProvider())
        {
            rowsInserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO [ProductRequestIdempotency]
                    ([Id], [TenantId], [Key], [Operation], [ProductId], [ProductVariantId], [RequestFingerprint], [CreatedAt], [UpdatedAt], [IsDeleted])
                SELECT
                    {claimId}, {request.TenantId}, {request.Key}, {request.Operation}, {request.ProductId}, {request.ProductVariantId}, {request.RequestFingerprint}, {nowUtc}, {nowUtc}, {false}
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [ProductRequestIdempotency] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [TenantId] = {request.TenantId}
                      AND [Key] = {request.Key}
                      AND [Operation] = {request.Operation})",
                cancellationToken);
        }
        else if (IsSqliteProvider())
        {
            rowsInserted = await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT OR IGNORE INTO ""ProductRequestIdempotency""
                (""Id"", ""TenantId"", ""Key"", ""Operation"", ""ProductId"", ""ProductVariantId"", ""RequestFingerprint"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                VALUES
                ({claimId}, {request.TenantId}, {request.Key}, {request.Operation}, {request.ProductId}, {request.ProductVariantId}, {request.RequestFingerprint}, {nowUtc}, {nowUtc}, {false})",
                cancellationToken);
        }
        else
        {
            throw new NotSupportedException(
                $"Database provider '{_context.Database.ProviderName}' is not supported for product mutation idempotency.");
        }

        if (rowsInserted > 0)
        {
            var claimedRecord = await _context.ProductRequestIdempotencyRecords
                .FirstAsync(record =>
                    record.TenantId == request.TenantId &&
                    record.Key == request.Key &&
                    record.Operation == request.Operation,
                    cancellationToken);

            return new ProductMutationIdempotencyClaim(claimedRecord, null);
        }

        var existingRecord = await TryResolveAsync(
            request.TenantId,
            request.Key,
            request.Operation,
            cancellationToken);

        if (existingRecord is null)
        {
            return new ProductMutationIdempotencyClaim(
                null,
                new ProductMutationIdempotencyReplay(ProductMutationIdempotencyReplayKind.InProgress, null));
        }

        if (!string.Equals(existingRecord.RequestFingerprint, request.RequestFingerprint, StringComparison.Ordinal))
        {
            return new ProductMutationIdempotencyClaim(
                null,
                new ProductMutationIdempotencyReplay(ProductMutationIdempotencyReplayKind.Conflict, existingRecord));
        }

        if (!existingRecord.ResponseStatusCode.HasValue)
        {
            return new ProductMutationIdempotencyClaim(
                null,
                new ProductMutationIdempotencyReplay(ProductMutationIdempotencyReplayKind.InProgress, existingRecord));
        }

        return new ProductMutationIdempotencyClaim(
            null,
            new ProductMutationIdempotencyReplay(ProductMutationIdempotencyReplayKind.Completed, existingRecord));
    }

    public Task<ProductRequestIdempotency?> TryResolveAsync(
        Guid tenantId,
        string key,
        string operation,
        CancellationToken cancellationToken = default) =>
        _context.ProductRequestIdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(record =>
                record.TenantId == tenantId &&
                record.Key == key &&
                record.Operation == operation,
                cancellationToken);

    public void Complete(
        ProductRequestIdempotency? record,
        int statusCode,
        object? responsePayload,
        Guid? productId = null,
        Guid? productVariantId = null)
    {
        if (record is null)
        {
            return;
        }

        record.ResponseStatusCode = statusCode;
        record.ResponseJson = responsePayload is null
            ? null
            : JsonSerializer.Serialize(responsePayload, JsonOptions);
        record.CompletedAtUtc = DateTime.UtcNow;
        record.UpdatedAt = DateTime.UtcNow;

        if (productId.HasValue)
        {
            record.ProductId = productId.Value;
        }

        if (productVariantId.HasValue)
        {
            record.ProductVariantId = productVariantId.Value;
        }
    }

    public string ScopeKey(string scope, string idempotencyKey)
    {
        var scopedKey = $"{scope}:{idempotencyKey.Trim()}";
        return scopedKey.Length <= 200
            ? scopedKey
            : $"{scope}:sha256:{HashToHex(scopedKey)}";
    }

    private bool IsPostgresProvider() =>
        _context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqlServerProvider() =>
        _context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    private bool IsSqliteProvider() =>
        _context.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

    private static string HashToHex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
