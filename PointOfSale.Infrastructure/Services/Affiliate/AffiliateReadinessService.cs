using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Core.Models.Affiliate;
using PointOfSale.Core.Payments;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Services.Affiliate;

/// <summary>
/// AF-23: Affiliate Readiness Gate Service Implementation.
/// 
/// ARCHITECTURE:
/// - Queries PostgreSQL authoritative StoreConfig, TenantPaymentMethods, and ShippingMethods
/// - Centralized logic (single source of truth)
/// - No caching (always fresh check)
/// - Tenant-safe (requires valid tenantId)
/// 
/// READINESS RULES:
/// 1. Storefront status must be "live"
/// 2. At least one TenantPaymentMethod with IsEnabled = true and valid configuration
/// 3. At least one ShippingMethod with IsActive = true
/// 
/// All three conditions must be satisfied for IsReady = true.
/// </summary>
public class AffiliateReadinessService : IAffiliateReadinessService
{
    private const string PaymentMethodSecretsProtectorPurpose = "PaymentMethodSecrets";

    private readonly ApplicationDbContext _dbContext;
    private readonly IDataProtector _paymentMethodProtector;
    private readonly ILogger<AffiliateReadinessService> _logger;

    public AffiliateReadinessService(
        ApplicationDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<AffiliateReadinessService> logger)
    {
        _dbContext = dbContext;
        _paymentMethodProtector = dataProtectionProvider.CreateProtector(PaymentMethodSecretsProtectorPurpose);
        _logger = logger;
    }

    public async Task<AffiliateReadinessResult> CheckAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId is required", nameof(tenantId));
        }

        var result = new AffiliateReadinessResult();

        // ─── Check 1: Storefront Live ───────────────────────────────────
        var storefrontConfig = await _dbContext.StoreConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(config => config.TenantId == tenantId, ct);

        result.StorefrontLive = string.Equals(
            storefrontConfig?.StorefrontStatus?.Trim(),
            "live",
            StringComparison.OrdinalIgnoreCase);

        if (!result.StorefrontLive)
        {
            result.MissingRequirements.Add("Storefront must be live");
        }

        // ─── Check 2: Active Payment Method ─────────────────────────────
        var enabledPaymentMethods = await _dbContext.TenantPaymentMethods
            .AsNoTracking()
            .Where(method => method.TenantId == tenantId && method.IsEnabled && !method.IsDeleted)
            .ToListAsync(ct);

        result.HasPaymentMethod = enabledPaymentMethods.Any(IsAuthoritativePaymentMethodReady);

        if (!result.HasPaymentMethod)
        {
            result.MissingRequirements.Add("At least one active payment method required");
        }

        // ─── Check 3: Active Shipping Method ────────────────────────────
        result.HasShippingMethod = await _dbContext.ShippingMethods
            .AsNoTracking()
            .Where(sm => sm.TenantId == tenantId && sm.IsActive && !sm.IsDeleted)
            .AnyAsync(ct);

        if (!result.HasShippingMethod)
        {
            result.MissingRequirements.Add("At least one active shipping method required");
        }

        // ─── Final Readiness ────────────────────────────────────────────
        result.IsReady = result.StorefrontLive 
                      && result.HasPaymentMethod 
                      && result.HasShippingMethod;

        // ─── Logging ────────────────────────────────────────────────────
        if (!result.IsReady)
        {
            _logger.LogWarning(
                "[AF-23] Affiliate readiness failed | TenantId: {TenantId} | Missing: {Missing}",
                tenantId,
                string.Join(", ", result.MissingRequirements));
        }

        return result;
    }

    private bool IsAuthoritativePaymentMethodReady(Core.Entities.TenantPaymentMethod method)
    {
        var validation = EvaluatePaymentMethodConfiguration(method);
        if (validation.IsConfigurationValid)
        {
            return true;
        }

        _logger.LogWarning(
            "[AF-23] Affiliate readiness ignored invalid payment method | TenantId: {TenantId} | PaymentMethodId: {PaymentMethodId} | Code: {Code} | Errors: {Errors}",
            method.TenantId,
            method.Id,
            method.Code,
            string.Join("; ", validation.ValidationErrors));

        return false;
    }

    private PaymentMethodConfigurationValidationResult EvaluatePaymentMethodConfiguration(Core.Entities.TenantPaymentMethod method)
    {
        if (string.IsNullOrWhiteSpace(method.ConfigEncrypted))
        {
            return PaymentMethodConfigurationValidator.Validate(method.Code, null);
        }

        try
        {
            var decrypted = _paymentMethodProtector.Unprotect(method.ConfigEncrypted);
            var configuration = JsonSerializer.Deserialize<Dictionary<string, string>>(decrypted);
            return PaymentMethodConfigurationValidator.Validate(method.Code, configuration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[AF-23] Payment method configuration could not be decrypted during affiliate readiness check | TenantId: {TenantId} | PaymentMethodId: {PaymentMethodId} | Code: {Code}",
                method.TenantId,
                method.Id,
                method.Code);

            var normalizedCode = method.Code?.Trim().ToUpperInvariant() ?? string.Empty;
            return new PaymentMethodConfigurationValidationResult(
                normalizedCode,
                PaymentMethodConfigurationValidator.RequiresConfiguration(normalizedCode),
                false,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new[] { "Stored payment configuration could not be decrypted." },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }
}
