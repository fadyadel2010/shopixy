using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Services.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Payment Methods Controller.
/// 
/// Route: GET /api/storefront/payment-methods
/// 
/// PHASE 10.4.D1: Payment Methods Management
/// 
/// ARCHITECTURAL BOUNDARY (STRICT):
/// - Payment Methods are managed ONLY in POS Admin
/// - This API is READ-ONLY
/// - Only ENABLED payment methods are exposed
/// - Tenant isolation is mandatory
/// - ❌ NO SECRETS are exposed
/// </summary>
[ApiController]
[Route("api/storefront/payment-methods")]
[AllowAnonymous]
public class StorefrontPaymentMethodsController : ControllerBase
{
    private readonly IPaymentMethodReadRepository _paymentMethodReadRepository;
    private readonly ApplicationDbContext _dbContext;
    private readonly IStorefrontPaymentRuntimeReadinessService _runtimeReadinessService;
    private readonly CommerceReliabilityTelemetry _reliabilityTelemetry;
    private readonly TenantContext _tenantContext;
    private readonly ILogger<StorefrontPaymentMethodsController> _logger;

    public StorefrontPaymentMethodsController(
        IPaymentMethodReadRepository paymentMethodReadRepository,
        ApplicationDbContext dbContext,
        IStorefrontPaymentRuntimeReadinessService runtimeReadinessService,
        CommerceReliabilityTelemetry reliabilityTelemetry,
        TenantContext tenantContext,
        ILogger<StorefrontPaymentMethodsController> logger)
    {
        _paymentMethodReadRepository = paymentMethodReadRepository;
        _dbContext = dbContext;
        _runtimeReadinessService = runtimeReadinessService;
        _reliabilityTelemetry = reliabilityTelemetry;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Gets all enabled payment methods for the tenant.
    /// 
    /// Response includes:
    /// - Payment method details
    /// - Display names (EN/AR)
    /// - Fees and limits
    /// - Sorted by SortOrder
    /// 
    /// ❌ NO secrets are returned
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(StorefrontPaymentMethodsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetPaymentMethods(CancellationToken cancellationToken)
    {
        SetNoStoreHeaders();

        var traceId = GetTraceId();
        if (_tenantContext.TenantId is not Guid tenantId || tenantId == Guid.Empty)
        {
            _logger.LogWarning("Missing tenant context | TraceId: {TraceId}", traceId);
            return BadRequest(new ApiErrorResponse("TENANT_REQUIRED", "Tenant context is required", traceId));
        }

        try
        {
            var documents = await _paymentMethodReadRepository.GetEnabledPaymentMethodsAsync(
                tenantId,
                cancellationToken);

            var configuredCount = await _dbContext.TenantPaymentMethods
                .AsNoTracking()
                .IgnoreQueryFilters()
                .CountAsync(method => method.TenantId == tenantId && !method.IsDeleted, cancellationToken);

            var invalidDocuments = documents
                .Where(document => document.IsConfigurationValid == false)
                .ToList();

            if (invalidDocuments.Count > 0)
            {
                foreach (var document in invalidDocuments)
                {
                    _reliabilityTelemetry.RecordPaymentMethodExposedInvalid(document.Code, 1);
                }

                _logger.LogWarning(
                    "Suppressed invalid storefront payment methods | TenantId: {TenantId} | Codes: {Codes} | TraceId: {TraceId}",
                    tenantId,
                    string.Join(", ", invalidDocuments.Select(document => document.Code)),
                    traceId);
            }

            var unavailableRuntimeGates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var providerCode in documents
                         .Where(document =>
                             document.IsConfigurationValid == true &&
                             IsSupportedStorefrontPaymentMethod(document.Code))
                         .Select(document => document.Code.Trim().ToUpperInvariant())
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var runtimeStatus = await _runtimeReadinessService.EvaluateAsync(
                    tenantId,
                    providerCode,
                    cancellationToken);
                if (!runtimeStatus.IsReady)
                {
                    unavailableRuntimeGates[providerCode] = runtimeStatus.FirstFailedGate ?? "RUNTIME_NOT_READY";
                }
            }

            if (unavailableRuntimeGates.Count > 0)
            {
                _logger.LogWarning(
                    "Suppressed storefront payment methods whose execution runtime is unavailable | TenantId: {TenantId} | Gates: {Gates} | TraceId: {TraceId}",
                    tenantId,
                    string.Join(", ", unavailableRuntimeGates.Select(pair => $"{pair.Key}:{pair.Value}")),
                    traceId);
            }

            var response = documents
                .Where(document =>
                    document.IsConfigurationValid == true &&
                    IsSupportedStorefrontPaymentMethod(document.Code) &&
                    !unavailableRuntimeGates.ContainsKey(document.Code.Trim()))
                .Select(MapToDto)
                .ToList();

            // T-09: Fawry is never selectable in the storefront, even if a stale
            // Mongo projection says enabled/configured and a SQL tenant row is enabled.
            var suppressedFawry = documents.Count(document =>
                PointOfSale.Core.Payments.FawryFinancialAuthorityPolicy.IsFawry(document.Code));
            if (suppressedFawry > 0)
            {
                _logger.LogWarning(
                    "Suppressed Fawry storefront payment method without financial authority | TenantId: {TenantId} | Suppressed: {Suppressed} | ReasonCode: {ReasonCode} | TraceId: {TraceId}",
                    tenantId,
                    suppressedFawry,
                    PointOfSale.Core.Payments.FawryFinancialAuthorityPolicy.Code,
                    traceId);
            }

            _logger.LogDebug(
                "GetPaymentMethods | TenantId: {TenantId} | Count: {Count} | TraceId: {TraceId}",
                tenantId, response.Count, traceId);

            return Ok(new StorefrontPaymentMethodsResponseDto
            {
                Status = response.Count > 0 ? "AVAILABLE" : "EMPTY",
                PaymentMethods = response,
                TotalCount = response.Count,
                ConfiguredCount = configuredCount,
                EnabledCount = documents.Count,
                AvailableCount = response.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error getting payment methods | TenantId: {TenantId} | TraceId: {TraceId}",
                tenantId, traceId);

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiErrorResponse.ServiceUnavailable(traceId));
        }
    }

    private static StorefrontPaymentMethodDto MapToDto(PaymentMethodDocument doc)
    {
        return new()
        {
            Id = doc.PaymentMethodId,
            Code = doc.Code,
            DisplayName = doc.DisplayName,
            DisplayNameAr = doc.DisplayNameAr,
            Description = doc.Description,
            DescriptionAr = doc.DescriptionAr,
            SupportsOnline = doc.SupportsOnline,
            SupportsOffline = doc.SupportsOffline,
            Icon = doc.Icon,
            MinOrderAmount = doc.MinOrderAmount,
            MaxOrderAmount = doc.MaxOrderAmount,
            Fee = doc.Fee,
            FeeType = doc.FeeType,
            SortOrder = doc.SortOrder,
            IsAvailable = true,
            UnavailableReasonCode = null
        };
    }

    private static bool IsSupportedStorefrontPaymentMethod(string? code)
    {
        // T-09: Fawry is never an available storefront payment method. The SQL
        // runtime authority additionally blocks Fawry with
        // FAWRY_FINANCIAL_AUTHORITY_UNAVAILABLE.
        return code?.Trim().ToUpperInvariant() is "COD" or "STRIPE" or "PAYMOB" or "MANUALWALLET";
    }

    private string GetTraceId()
    {
        return Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }

    private void SetNoStoreHeaders()
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
    }
}

/// <summary>
/// Storefront payment method DTO.
/// 
/// ❌ NO secrets are included
/// </summary>
public class StorefrontPaymentMethodDto
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameAr { get; set; }
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public bool SupportsOnline { get; set; }
    public bool SupportsOffline { get; set; }
    public string? Icon { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public decimal? MaxOrderAmount { get; set; }
    public decimal Fee { get; set; }
    public string FeeType { get; set; } = "fixed";
    public int SortOrder { get; set; }
    public bool IsAvailable { get; set; } = true;
    public string? UnavailableReasonCode { get; set; }
}

public class StorefrontPaymentMethodsResponseDto
{
    public string Status { get; set; } = "AVAILABLE";
    public List<StorefrontPaymentMethodDto> PaymentMethods { get; set; } = new();
    public int TotalCount { get; set; }
    public int ConfiguredCount { get; set; }
    public int EnabledCount { get; set; }
    public int AvailableCount { get; set; }
}
