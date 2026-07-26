using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Infrastructure.MongoDB.Services;

/// <summary>
/// MongoDB repository implementation for payment method read operations.
/// 
/// ARCHITECTURE (CQRS COMPLIANCE):
/// - MongoDB is the read-only projection source for Storefront/Checkout
/// - PostgreSQL is write-only for payment methods
/// - All queries MUST filter by TenantId
/// - NO SECRETS - ConfigEncrypted is NEVER exposed
/// - Pure data access layer - no business logic
/// </summary>
public class PaymentMethodReadRepository : IPaymentMethodReadRepository
{
    private const string DataProtectionPurpose = "PaymentMethodSecrets";
    private readonly IMongoCollection<PaymentMethodDocument> _collection;
    private readonly ApplicationDbContext _dbContext;
    private readonly IDataProtector _dataProtector;
    private readonly ILogger<PaymentMethodReadRepository> _logger;

    private const string CollectionName = "payment_methods";

    public PaymentMethodReadRepository(
        IMongoDatabase database,
        ApplicationDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<PaymentMethodReadRepository> logger)
    {
        _collection = database.GetCollection<PaymentMethodDocument>(CollectionName);
        _dbContext = dbContext;
        _dataProtector = dataProtectionProvider.CreateProtector(DataProtectionPurpose);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<PaymentMethodDocument>> GetEnabledPaymentMethodsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        List<PaymentMethodDocument>? result = null;

        try
        {
            var filter = Builders<PaymentMethodDocument>.Filter.And(
                Builders<PaymentMethodDocument>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<PaymentMethodDocument>.Filter.Eq(x => x.IsEnabled, true),
                Builders<PaymentMethodDocument>.Filter.Ne(x => x.IsDeleted, true)
            );

            var sort = Builders<PaymentMethodDocument>.Sort
                .Ascending(x => x.SortOrder)
                .Ascending(x => x.DisplayName);

            result = await _collection
                .Find(filter)
                .Sort(sort)
                .ToListAsync(cancellationToken);

            if (result.Count > 0 && result.All(document => document.IsConfigurationValid.HasValue))
            {
                _logger.LogDebug(
                    "GetEnabledPaymentMethods | TenantId: {TenantId} | ResultCount: {Count}",
                    tenantId, result.Count);

                return result;
            }

            _logger.LogWarning(
                "Payment method projection empty or stale; falling back to SQL | TenantId: {TenantId}",
                tenantId);
        }
        catch (Exception ex) when (IsMongoReadFailure(ex))
        {
            _logger.LogError(
                ex,
                "Payment method read model unavailable; falling back to SQL | TenantId: {TenantId}",
                tenantId);
        }

        var methods = await _dbContext.TenantPaymentMethods
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsEnabled && !x.IsDeleted)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

        return methods.Select(MapToDocument).ToList();
    }

    /// <inheritdoc />
    public async Task<PaymentMethodDocument?> GetByIdAsync(
        Guid tenantId,
        Guid paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filter = Builders<PaymentMethodDocument>.Filter.And(
                Builders<PaymentMethodDocument>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<PaymentMethodDocument>.Filter.Eq(x => x.PaymentMethodId, paymentMethodId),
                Builders<PaymentMethodDocument>.Filter.Ne(x => x.IsDeleted, true)
            );

            var document = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
            if (document != null && document.IsConfigurationValid.HasValue)
            {
                return document;
            }

            _logger.LogWarning(
                "Payment method projection miss or stale; falling back to SQL | TenantId: {TenantId} | PaymentMethodId: {PaymentMethodId}",
                tenantId,
                paymentMethodId);
        }
        catch (Exception ex) when (IsMongoReadFailure(ex))
        {
            _logger.LogError(
                ex,
                "Payment method read model unavailable; falling back to SQL | TenantId: {TenantId} | PaymentMethodId: {PaymentMethodId}",
                tenantId,
                paymentMethodId);
        }

        var method = await _dbContext.TenantPaymentMethods
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Id == paymentMethodId && !x.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        return method == null ? null : MapToDocument(method);
    }

    /// <inheritdoc />
    public async Task<PaymentMethodDocument?> GetByCodeAsync(
        Guid tenantId,
        string code,
        CancellationToken cancellationToken = default)
    {
        // Normalize code for case-insensitive matching
        var normalizedCode = code?.Trim().ToUpperInvariant() ?? string.Empty;

        try
        {
            var filter = Builders<PaymentMethodDocument>.Filter.And(
                Builders<PaymentMethodDocument>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<PaymentMethodDocument>.Filter.Eq(x => x.Code, normalizedCode),
                Builders<PaymentMethodDocument>.Filter.Ne(x => x.IsDeleted, true)
            );

            var result = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);

            _logger.LogDebug(
                "GetByCode | TenantId: {TenantId} | Code: {Code} | Found: {Found}",
                tenantId, normalizedCode, result != null);

            if (result != null && result.IsConfigurationValid.HasValue)
            {
                return result;
            }

            _logger.LogWarning(
                "Payment method projection miss or stale; falling back to SQL | TenantId: {TenantId} | Code: {Code}",
                tenantId,
                normalizedCode);
        }
        catch (Exception ex) when (IsMongoReadFailure(ex))
        {
            _logger.LogError(
                ex,
                "Payment method read model unavailable; falling back to SQL | TenantId: {TenantId} | Code: {Code}",
                tenantId,
                normalizedCode);
        }

        var method = await _dbContext.TenantPaymentMethods
            .AsNoTracking()
            .Where(x =>
                x.TenantId == tenantId &&
                x.Code.ToUpper() == normalizedCode &&
                !x.IsDeleted)
            .FirstOrDefaultAsync(cancellationToken);

        return method == null ? null : MapToDocument(method);
    }

    /// <inheritdoc />
    public async Task<bool> UpsertAsync(
        PaymentMethodDocument document,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<PaymentMethodDocument>.Filter.And(
            Builders<PaymentMethodDocument>.Filter.Eq(x => x.TenantId, document.TenantId),
            Builders<PaymentMethodDocument>.Filter.Eq(x => x.PaymentMethodId, document.PaymentMethodId)
        );

        var options = new ReplaceOptions { IsUpsert = true };

        var result = await _collection.ReplaceOneAsync(filter, document, options, cancellationToken);

        var success = result.IsAcknowledged && (result.ModifiedCount > 0 || result.UpsertedId != null);

        _logger.LogDebug(
            "Upsert PaymentMethod | TenantId: {TenantId} | PaymentMethodId: {PaymentMethodId} | Code: {Code} | Success: {Success}",
            document.TenantId, document.PaymentMethodId, document.Code, success);

        return success;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        Guid tenantId,
        Guid paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<PaymentMethodDocument>.Filter.And(
            Builders<PaymentMethodDocument>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<PaymentMethodDocument>.Filter.Eq(x => x.PaymentMethodId, paymentMethodId)
        );

        var result = await _collection.DeleteOneAsync(filter, cancellationToken);

        var success = result.IsAcknowledged && result.DeletedCount > 0;

        _logger.LogInformation(
            "Delete PaymentMethod | TenantId: {TenantId} | PaymentMethodId: {PaymentMethodId} | Deleted: {Deleted}",
            tenantId, paymentMethodId, success);

        return success;
    }

    private PaymentMethodDocument MapToDocument(TenantPaymentMethod method)
    {
        var configurationValidation = EvaluateConfiguration(method);

        return new PaymentMethodDocument
        {
            TenantId = method.TenantId,
            PaymentMethodId = method.Id,
            Code = method.Code?.Trim().ToUpperInvariant() ?? string.Empty,
            DisplayName = method.DisplayName,
            DisplayNameAr = method.DisplayNameAr,
            Description = method.Description,
            DescriptionAr = method.DescriptionAr,
            IsEnabled = method.IsEnabled,
            SortOrder = method.SortOrder,
            SupportsOnline = method.SupportsOnline,
            SupportsOffline = method.SupportsOffline,
            IsSandbox = method.IsSandbox,
            Icon = method.Icon,
            MinOrderAmount = method.MinOrderAmount,
            MaxOrderAmount = method.MaxOrderAmount,
            Fee = method.Fee,
            FeeType = method.FeeType,
            IsConfigurationValid = configurationValidation.IsConfigurationValid,
            HasConfiguration = !string.IsNullOrWhiteSpace(method.ConfigEncrypted),
            PublicConfiguration = new Dictionary<string, string>(configurationValidation.PublicConfiguration, StringComparer.OrdinalIgnoreCase),
            IsDeleted = false,
            SyncedAtUtc = method.UpdatedAtUtc ?? method.UpdatedAt ?? method.CreatedAt
        };
    }

    private PaymentMethodConfigurationValidationResult EvaluateConfiguration(TenantPaymentMethod method)
    {
        if (string.IsNullOrWhiteSpace(method.ConfigEncrypted))
        {
            return PaymentMethodConfigurationValidator.Validate(method.Code, null);
        }

        try
        {
            var decrypted = _dataProtector.Unprotect(method.ConfigEncrypted);
            var config = JsonSerializer.Deserialize<Dictionary<string, string>>(decrypted);
            return PaymentMethodConfigurationValidator.Validate(method.Code, config);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to decrypt payment-method configuration during SQL fallback | TenantId: {TenantId} | PaymentMethodId: {PaymentMethodId} | Code: {Code}",
                method.TenantId,
                method.Id,
                method.Code);

            return new PaymentMethodConfigurationValidationResult(
                method.Code?.Trim().ToUpperInvariant() ?? string.Empty,
                PaymentMethodConfigurationValidator.RequiresConfiguration(method.Code),
                false,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new[] { "Stored payment configuration could not be decrypted." },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private static bool IsMongoReadFailure(Exception ex) =>
        ex is MongoException || ex is TimeoutException;
}
