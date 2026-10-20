using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Localization;
using PointOfSale.API.Services.Payments;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.API.Controllers;

/// <summary>
/// POS Admin API controller for managing payment methods.
/// 
/// PHASE 10.4.D1: Payment Methods Management
/// 
/// ARCHITECTURE:
/// - PostgreSQL is the source of truth (Write Model)
/// - Events are emitted on Create/Update/Enable/Disable
/// - Consumers project to MongoDB for Storefront reads
/// - Secrets are encrypted using Data Protection API
/// - ❌ Secrets are NEVER returned in API responses
/// </summary>
[ApiController]
[Route("api/admin/payment-methods")]
[Authorize(Roles = "Owner,Admin,Manager")]
public class PaymentMethodsAdminController : ControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly IDataProtector _dataProtector;
    private readonly CommerceReliabilityTelemetry _reliabilityTelemetry;
    private readonly ILogger<PaymentMethodsAdminController> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IStorefrontPaymentRuntimeReadinessService _runtimeReadiness;

    private const string DataProtectionPurpose = "PaymentMethodSecrets";
    private static readonly string[] SupportedPaymentMethodCodes =
    [
        "COD",
        "STRIPE",
        "PAYMOB",
        "FAWRY",
        "MANUALWALLET"
    ];

    public PaymentMethodsAdminController(
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher,
        IDataProtectionProvider dataProtectionProvider,
        CommerceReliabilityTelemetry reliabilityTelemetry,
        ILogger<PaymentMethodsAdminController> logger,
        ITenantContext tenantContext,
        IStorefrontPaymentRuntimeReadinessService runtimeReadiness)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
        _dataProtector = dataProtectionProvider.CreateProtector(DataProtectionPurpose);
        _reliabilityTelemetry = reliabilityTelemetry;
        _logger = logger;
        _tenantContext = tenantContext;
        _runtimeReadiness = runtimeReadiness;
    }

    private Guid TenantId => _tenantContext.RequiredTenantId;

    /// <summary>
    /// Get the supported provider catalog without creating tenant payment-method rows.
    /// </summary>
    [HttpGet("catalog")]
    [ProducesResponseType(typeof(List<PaymentMethodCatalogProviderDto>), StatusCodes.Status200OK)]
    public IActionResult GetProviderCatalog()
    {
        SetNoStoreHeaders();

        return Ok(SupportedPaymentMethodCodes
            .Where(code => !FawryFinancialAuthorityPolicy.IsFawry(code))
            .Select(MapToCatalogDto)
            .OrderBy(provider => provider.SortOrder)
            .ToList());
    }

    /// <summary>
    /// Get all payment methods for the tenant (enabled + disabled).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<PaymentMethodResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaymentMethods(CancellationToken cancellationToken)
    {
        SetNoStoreHeaders();

        var tenantId = TenantId;

        var methods = await _dbContext.TenantPaymentMethods
            .Where(m => m.TenantId == tenantId && m.Code != "FAWRY")
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Code)
            .ToListAsync(cancellationToken);

        var response = methods.Select(MapToDto).ToList();
        foreach (var method in response)
        {
            await PopulateCheckoutReadinessAsync(method, tenantId, cancellationToken);
        }
        
        _logger.LogDebug("Retrieved {Count} payment methods", response.Count);
        
        return Ok(response);
    }

    /// <summary>
    /// Get a single payment method by code.
    /// </summary>
    [HttpGet("{code}")]
    [ProducesResponseType(typeof(PaymentMethodResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPaymentMethod(string code, CancellationToken cancellationToken)
    {
        SetNoStoreHeaders();

        var tenantId = TenantId;

        var method = await _dbContext.TenantPaymentMethods
            .FirstOrDefaultAsync(m => m.Code == code.ToUpperInvariant() && m.TenantId == tenantId, cancellationToken);

        if (method == null)
        {
            return NotFound(new { error = "PAYMENT_METHOD_NOT_FOUND", message = $"Payment method '{code}' not found" });
        }

        var response = MapToDto(method);
        await PopulateCheckoutReadinessAsync(response, tenantId, cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Create or update a payment method.
    /// </summary>
    [HttpPut("{code}")]
    [ProducesResponseType(typeof(PaymentMethodResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpsertPaymentMethod(
        string code,
        [FromBody] UpsertPaymentMethodDto request,
        CancellationToken cancellationToken)
    {
        if (request.Validate() is { } validationError)
            return BadRequest(new { error = "INVALID_PAYMENT_METHOD", message = validationError });

        var traceId = GetTraceId();
        var normalizedCode = code.Trim().ToUpperInvariant();

        // Validate code
        if (!IsValidCode(normalizedCode))
        {
            return BadRequest(new { error = "INVALID_CODE", message = "Code must be COD, STRIPE, PAYMOB, FAWRY, or MANUALWALLET" });
        }

        // T-09: Fawry has no certified financial settlement authority. Server-side
        // mutation rejection; no tenant-row or outbox mutation is performed.
        if (FawryFinancialAuthorityPolicy.IsFawry(normalizedCode))
        {
            return BuildFawryAuthorityRejection();
        }

        // PHASE P3 FIX: Must resolve TenantId for tenant isolation
        var tenantId = TenantId;

        var method = await _dbContext.TenantPaymentMethods
            .FirstOrDefaultAsync(m => m.Code == normalizedCode && m.TenantId == tenantId, cancellationToken);

        var isNew = method == null;
        if (CheckReviewedVersion(method) is { } versionError) return versionError;
        if (method == null)
        {
            method = new TenantPaymentMethod
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId, // CRITICAL: Set TenantId
                Code = normalizedCode,
                CreatedAt = DateTime.UtcNow
            };
        }

        // Update properties
        method.DisplayName = request.DisplayName ?? GetDefaultDisplayName(normalizedCode);
        method.DisplayNameAr = request.DisplayNameAr;
        method.Description = request.Description;
        method.DescriptionAr = request.DescriptionAr;
        method.SortOrder = request.SortOrder ?? method.SortOrder;
        method.SupportsOnline = GetSupportsOnline(normalizedCode);
        method.SupportsOffline = GetSupportsOffline(normalizedCode);
        method.IsSandbox = request.IsSandbox ?? (isNew ? true : method.IsSandbox);
        method.Icon = request.Icon ?? GetDefaultIcon(normalizedCode);
        method.MinOrderAmount = request.MinOrderAmount;
        method.MaxOrderAmount = request.MaxOrderAmount;
        method.Fee = request.Fee ?? 0;
        method.FeeType = request.FeeType ?? "fixed";
        method.UpdatedAtUtc = NextPaymentMethodMutationTimestamp(method);

        var configurationMutation = ApplyConfigurationMutation(
            method,
            request,
            traceId,
            operation: "upsert",
            rejectEmptyConfigurationPayload: false);

        if (!configurationMutation.Success)
        {
            return configurationMutation.ErrorResult!;
        }

        if (isNew)
        {
            _dbContext.TenantPaymentMethods.Add(method);
        }

        // CRITICAL: Publish event BEFORE SaveChangesAsync so it goes through EF Core Outbox
        // This ensures both entity changes and outbox messages are saved atomically
        if (isNew)
        {
            await PublishCreatedEvent(method, traceId, cancellationToken);
        }
        else
        {
            await PublishUpdatedEvent(method, configurationMutation.ConfigurationUpdated, traceId, cancellationToken);
            if (configurationMutation.PaymentMethodDisabled)
            {
                await PublishDisabledEvent(method, cancellationToken);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Payment method {Action} | Code: {Code} | Id: {Id} | TraceId: {TraceId}",
            isNew ? "created" : "updated", method.Code, method.Id, traceId);

        return Ok(MapToDto(method));
    }

    /// <summary>
    /// Update payment method configuration only.
    /// If the method doesn't exist, it will be created with default values first.
    /// </summary>
    [HttpPut("{code}/configure")]
    [ProducesResponseType(typeof(PaymentMethodResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateConfiguration(
        string code,
        [FromBody] UpsertPaymentMethodDto request,
        CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var normalizedCode = code.Trim().ToUpperInvariant();

        // Validate code first
        if (!IsValidCode(normalizedCode))
        {
            return BadRequest(new { error = "INVALID_CODE", message = "Code must be COD, STRIPE, PAYMOB, FAWRY, or MANUALWALLET" });
        }

        // T-09: Fawry has no certified financial settlement authority. Server-side
        // mutation rejection; no tenant-row or outbox mutation is performed.
        if (FawryFinancialAuthorityPolicy.IsFawry(normalizedCode))
        {
            return BuildFawryAuthorityRejection();
        }

        // Get TenantId from the current user context
        var tenantId = TenantId;

        var method = await _dbContext.TenantPaymentMethods
            .FirstOrDefaultAsync(m => m.Code == normalizedCode && m.TenantId == tenantId, cancellationToken);

        // Auto-create payment method if it doesn't exist
        var isNew = method == null;
        if (CheckReviewedVersion(method) is { } versionError) return versionError;
        if (method == null)
        {
            method = new TenantPaymentMethod
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Code = normalizedCode,
                DisplayName = GetDefaultDisplayName(normalizedCode),
                DisplayNameAr = GetDefaultDisplayNameAr(normalizedCode),
                Description = GetDefaultDescription(normalizedCode),
                DescriptionAr = GetDefaultDescriptionAr(normalizedCode),
                SupportsOnline = GetSupportsOnline(normalizedCode),
                SupportsOffline = GetSupportsOffline(normalizedCode),
                Icon = GetDefaultIcon(normalizedCode),
                SortOrder = GetDefaultSortOrder(normalizedCode),
                IsSandbox = true,
                IsEnabled = false, // Not enabled yet - just configuring
                Fee = 0,
                FeeType = "fixed",
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.TenantPaymentMethods.Add(method);

            _logger.LogInformation(
                "Auto-created payment method (via configure) | Code: {Code} | Id: {Id} | TenantId: {TenantId} | TraceId: {TraceId}",
                normalizedCode, method.Id, tenantId, traceId);
        }

        // Update specific fields
        if (request.IsSandbox.HasValue)
        {
            method.IsSandbox = request.IsSandbox.Value;
        }

        var configurationMutation = ApplyConfigurationMutation(
            method,
            request,
            traceId,
            operation: "configure",
            rejectEmptyConfigurationPayload: true);

        if (!configurationMutation.Success)
        {
            return configurationMutation.ErrorResult!;
        }

        method.UpdatedAtUtc = NextPaymentMethodMutationTimestamp(method);

        // Publish appropriate event
        if (isNew)
        {
            await PublishCreatedEvent(method, traceId, cancellationToken);
        }
        else
        {
            await PublishUpdatedEvent(method, configurationMutation.ConfigurationUpdated, traceId, cancellationToken);
            if (configurationMutation.PaymentMethodDisabled)
            {
                await PublishDisabledEvent(method, cancellationToken);
            }
        }
        
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(MapToDto(method));
    }

    /// <summary>
    /// Enable a payment method.
    /// If the method doesn't exist, it will be created with default values first.
    /// 
    /// PHASE 10.4.D3: COD can be enabled without any prior configuration.
    /// </summary>
    [HttpPost("{code}/enable")]
    [ProducesResponseType(typeof(PaymentMethodResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> EnablePaymentMethod(string code, CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var normalizedCode = code.Trim().ToUpperInvariant();

        // Validate code first
        if (!IsValidCode(normalizedCode))
        {
            return BadRequest(new { error = "INVALID_CODE", message = "Code must be COD, STRIPE, PAYMOB, FAWRY, or MANUALWALLET" });
        }

        // T-09: Fawry has no certified financial settlement authority. Server-side
        // mutation rejection; no tenant-row or outbox mutation is performed.
        if (FawryFinancialAuthorityPolicy.IsFawry(normalizedCode))
        {
            return BuildFawryAuthorityRejection();
        }

        // Get TenantId from the current user context
        var tenantId = TenantId;

        var method = await _dbContext.TenantPaymentMethods
            .FirstOrDefaultAsync(m => m.Code == normalizedCode && m.TenantId == tenantId, cancellationToken);

        // PHASE 10.4.D3: Auto-create payment method if it doesn't exist
        // This allows users to enable payment methods without having to configure them first
        var isNew = method == null;
        if (CheckReviewedVersion(method) is { } versionError) return versionError;
        if (method == null)
        {
            method = new TenantPaymentMethod
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId, // Critical: Set TenantId to avoid security violation
                Code = normalizedCode,
                DisplayName = GetDefaultDisplayName(normalizedCode),
                DisplayNameAr = GetDefaultDisplayNameAr(normalizedCode),
                Description = GetDefaultDescription(normalizedCode),
                DescriptionAr = GetDefaultDescriptionAr(normalizedCode),
                SupportsOnline = GetSupportsOnline(normalizedCode),
                SupportsOffline = GetSupportsOffline(normalizedCode),
                Icon = GetDefaultIcon(normalizedCode),
                SortOrder = GetDefaultSortOrder(normalizedCode),
                IsSandbox = true,
                IsEnabled = true, // Will be set to true below
                Fee = 0,
                FeeType = "fixed",
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.TenantPaymentMethods.Add(method);

            _logger.LogInformation(
                "Auto-created payment method | Code: {Code} | Id: {Id} | TenantId: {TenantId} | TraceId: {TraceId}",
                normalizedCode, method.Id, tenantId, traceId);
        }

        var configuration = EvaluateConfiguration(method);
        if (configuration.RequiresConfiguration && !configuration.IsConfigurationValid)
        {
            _reliabilityTelemetry.RecordPaymentConfigInvalid(normalizedCode, "enable", configuration.ValidationErrors.FirstOrDefault() ?? "config_invalid");
            return BuildConfigurationValidationErrorResult(
                normalizedCode,
                configuration.ValidationErrors,
                $"{normalizedCode} requires a valid configuration before it can be enabled.");
        }

        if (method.IsEnabled && !isNew)
        {
            return Ok(MapToDto(method)); // Already enabled
        }

        method.IsEnabled = true;
        method.UpdatedAtUtc = NextPaymentMethodMutationTimestamp(method);

        // CRITICAL: Publish event BEFORE SaveChangesAsync so it goes through EF Core Outbox
        if (isNew)
        {
            await PublishCreatedEvent(method, traceId, cancellationToken);
        }
        else
        {
            // S39: Increment version before publishing event
            method.Version++;

            await _eventPublisher.PublishAsync(new PaymentMethodEnabledEvent
            {
                EventId = Guid.NewGuid(),
                TenantId = method.TenantId,
                PaymentMethodId = method.Id,
                Code = method.Code,
                DisplayName = method.DisplayName,
                OccurredAtUtc = method.UpdatedAtUtc ?? method.UpdatedAt ?? method.CreatedAt,
                Version = method.Version
            }, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Payment method enabled | Code: {Code} | Id: {Id} | IsNew: {IsNew} | TraceId: {TraceId}",
            method.Code, method.Id, isNew, traceId);

        return Ok(MapToDto(method));
    }

    /// <summary>
    /// Disable a payment method.
    /// </summary>
    [HttpPost("{code}/disable")]
    [ProducesResponseType(typeof(PaymentMethodResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DisablePaymentMethod(string code, CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var normalizedCode = code.ToUpperInvariant();

        var tenantId = TenantId;

        var method = await _dbContext.TenantPaymentMethods
            .FirstOrDefaultAsync(m => m.Code == normalizedCode && m.TenantId == tenantId, cancellationToken);

        if (method == null)
        {
            return NotFound(new { error = "PAYMENT_METHOD_NOT_FOUND", message = $"Payment method '{code}' not found" });
        }

        if (!method.IsEnabled)
        {
            return Ok(MapToDto(method)); // Already disabled
        }

        if (CheckReviewedVersion(method) is { } versionError) return versionError;

        method.IsEnabled = false;
        method.UpdatedAtUtc = NextPaymentMethodMutationTimestamp(method);

        // CRITICAL: Publish event BEFORE SaveChangesAsync so it goes through EF Core Outbox
        await PublishDisabledEvent(method, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Payment method disabled | Code: {Code} | Id: {Id} | TraceId: {TraceId}",
            method.Code, method.Id, traceId);

        return Ok(MapToDto(method));
    }

    /// <summary>
    /// Sync all payment methods to MongoDB.
    /// This is a recovery endpoint to republish all payment method events
    /// in case the projection consumer missed events or wasn't running.
    /// </summary>
    [HttpPost("sync")]
    [Authorize(Roles = "Owner,Admin")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> SyncPaymentMethods(CancellationToken cancellationToken)
    {
        var traceId = GetTraceId();
        var tenantId = TenantId;

        var methods = await _dbContext.TenantPaymentMethods
            .Where(m => m.TenantId == tenantId && !m.IsDeleted)
            .ToListAsync(cancellationToken);

        int syncedCount = 0;
        foreach (var method in methods)
        {
            try
            {
                var configuration = EvaluateConfiguration(method);

                // S39: Increment version AFTER successful publish to avoid version gaps on failure
                var nextVersion = method.Version + 1;

                // Republish as update event to trigger projection
                await _eventPublisher.PublishAsync(new PaymentMethodUpdatedEvent
                {
                    EventId = Guid.NewGuid(),
                    TenantId = method.TenantId,
                    PaymentMethodId = method.Id,
                    Code = method.Code,
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
                    PublicConfiguration = configuration.PublicConfiguration.Count == 0
                        ? null
                        : new Dictionary<string, string>(configuration.PublicConfiguration, StringComparer.OrdinalIgnoreCase),
                    IsConfigurationValid = configuration.IsConfigurationValid,
                    HasConfiguration = configuration.HasStoredConfiguration,
                    ConfigurationUpdated = true, // Rehydrate projected configuration state from the write model
                    OccurredAtUtc = method.UpdatedAtUtc ?? method.UpdatedAt ?? method.CreatedAt,
                    Version = nextVersion
                }, cancellationToken);

                // Only increment version after successful publish
                method.Version = nextVersion;
                syncedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to sync payment method | Code: {Code} | Id: {Id} | TraceId: {TraceId}",
                    method.Code, method.Id, traceId);
            }
        }

        // Persist outbox before recording telemetry (durability ordering)
        await _dbContext.SaveChangesAsync(cancellationToken);
        _reliabilityTelemetry.RecordProjectionSyncDispatched("payment_methods", syncedCount);

        _logger.LogInformation(
            "PaymentMethods sync committed | Synced: {Synced} | TraceId: {TraceId}",
            syncedCount,
            traceId);

        return Ok(new { synced = syncedCount, committed = syncedCount });
    }

    #region Helper Methods

    private IActionResult? CheckReviewedVersion(TenantPaymentMethod? method)
    {
        // Legacy API callers remain compatible; the Admin sends the revision it
        // actually reviewed. EF's concurrency token also protects simultaneous writes.
        if (!Request.Headers.TryGetValue("If-Match", out var header)) return null;
        if (!int.TryParse(header.ToString().Trim('"'), out var expected) || expected <= 0)
            return BadRequest(new { error = "INVALID_VERSION", message = "If-Match must contain the reviewed payment-method version." });
        return method == null || method.Version != expected
            ? Conflict(new { error = "PAYMENT_METHOD_CONFLICT", message = "Payment method changed. Reload before saving." })
            : null;
    }

    private static bool IsValidCode(string code) =>
        SupportedPaymentMethodCodes.Contains(code, StringComparer.OrdinalIgnoreCase);

    private static DateTime NextPaymentMethodMutationTimestamp(TenantPaymentMethod method)
    {
        var now = DateTime.UtcNow;
        var current = method.UpdatedAtUtc ?? method.UpdatedAt ?? method.CreatedAt;
        // Add 1 microsecond (10 ticks) to ensure monotonicity after PostgreSQL round-trip
        // PostgreSQL timestamp precision is 1 microsecond, so AddTicks(1) = 100ns is insufficient
        return current >= now ? current.AddTicks(10) : now;
    }

    private static bool RequiresConfiguration(string code) =>
        PaymentMethodConfigurationValidator.RequiresConfiguration(code);

    private static bool GetSupportsOnline(string code) =>
        code is "STRIPE" or "PAYMOB" or "FAWRY";

    private static bool GetSupportsOffline(string code) =>
        code is "COD" or "MANUALWALLET";

    private static string GetDefaultDisplayName(string code) => code switch
    {
        "COD" => "Cash on Delivery",
        "STRIPE" => "Credit/Debit Card",
        "PAYMOB" => "Online Payment",
        "FAWRY" => "Fawry Pay",
        "MANUALWALLET" => "Manual Wallet (Vodafone Cash/Instapay)",
        _ => code
    };

    private static string GetDefaultDisplayNameAr(string code) => code switch
    {
        "COD" => "الدفع عند الاستلام",
        "STRIPE" => "بطاقة ائتمان/خصم",
        "PAYMOB" => "الدفع الإلكتروني",
        "FAWRY" => "فوري باي",
        "MANUALWALLET" => "محفظة إلكترونية (فودافون كاش/انستاباى)",
        _ => code
    };

    private static string? GetDefaultDescription(string code) => code switch
    {
        "COD" => "Pay with cash when your order is delivered",
        "STRIPE" => "Pay securely with your credit or debit card",
        "PAYMOB" => "Pay with various online payment options",
        "FAWRY" => "Pay at any Fawry kiosk with a reference code",
        "MANUALWALLET" => "Transfer to our wallet and upload proof of payment",
        _ => null
    };

    private static string? GetDefaultDescriptionAr(string code) => code switch
    {
        "COD" => "ادفع نقداً عند استلام طلبك",
        "STRIPE" => "ادفع بأمان ببطاقتك الائتمانية أو بطاقة الخصم",
        "PAYMOB" => "ادفع بخيارات الدفع الإلكتروني المتنوعة",
        "FAWRY" => "ادفع في أي كشك فوري باستخدام كود مرجعي",
        "MANUALWALLET" => "حول لمحفظتنا وارفع صورة التحويل",
        _ => null
    };

    private static int GetDefaultSortOrder(string code) => code switch
    {
        "COD" => 1,
        "STRIPE" => 2,
        "PAYMOB" => 3,
        "FAWRY" => 4,
        "MANUALWALLET" => 5,
        _ => 99
    };

    private static string GetDefaultIcon(string code) => code switch
    {
        "COD" => "cash",
        "STRIPE" => "credit-card",
        "PAYMOB" => "credit-card",
        "FAWRY" => "receipt",
        "MANUALWALLET" => "phone-portrait",
        _ => "payment"
    };

    private static PaymentMethodCatalogProviderDto MapToCatalogDto(string code) => new()
    {
        Code = code,
        DisplayName = GetDefaultDisplayName(code),
        DisplayNameAr = GetDefaultDisplayNameAr(code),
        Description = GetDefaultDescription(code),
        DescriptionAr = GetDefaultDescriptionAr(code),
        SortOrder = GetDefaultSortOrder(code),
        SupportsOnline = GetSupportsOnline(code),
        SupportsOffline = GetSupportsOffline(code),
        IsSandbox = true,
        Icon = GetDefaultIcon(code),
        Fee = 0,
        FeeType = "fixed",
        RequiresConfiguration = RequiresConfiguration(code),
        // T-09: Fawry has no certified financial settlement authority and must
        // render as unsupported regardless of credentials or stored state.
        IsSupported = !FawryFinancialAuthorityPolicy.IsFawry(code),
        UnsupportedReasonCode = FawryFinancialAuthorityPolicy.IsFawry(code)
            ? FawryFinancialAuthorityPolicy.Code
            : null,
        UnsupportedMessage = FawryFinancialAuthorityPolicy.IsFawry(code)
            ? FawryFinancialAuthorityPolicy.Message
            : null
    };

    private PaymentMethodResponseDto MapToDto(TenantPaymentMethod method)
    {
        var configuration = EvaluateConfiguration(method);
        var isFawry = FawryFinancialAuthorityPolicy.IsFawry(method.Code);

        return new PaymentMethodResponseDto
        {
            Id = method.Id,
            Code = method.Code,
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
            RequiresConfiguration = configuration.RequiresConfiguration,
            IsConfigurationValid = configuration.IsConfigurationValid,
            ConfigurationErrors = configuration.ValidationErrors.ToList(),
            PublicConfiguration = new Dictionary<string, string>(configuration.PublicConfiguration, StringComparer.OrdinalIgnoreCase),
            // T-09: existing Fawry tenant rows render as unsupported regardless
            // of stored IsEnabled or credential validity. Rows are never rewritten.
            IsSupported = !isFawry,
            UnsupportedReasonCode = isFawry ? FawryFinancialAuthorityPolicy.Code : null,
            UnsupportedMessage = isFawry ? FawryFinancialAuthorityPolicy.Message : null,
            CreatedAt = method.CreatedAt,
            UpdatedAt = method.UpdatedAtUtc,
            Version = method.Version
        };
    }

    private async Task PopulateCheckoutReadinessAsync(
        PaymentMethodResponseDto method,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        // Configuration validity and activation are not runtime availability.
        // Use the same authority as the public catalog, never infer it from Sandbox.
        var readiness = !method.IsSupported
            ? StorefrontPaymentRuntimeReadiness.Blocked(method.UnsupportedReasonCode ?? "PROVIDER_UNSUPPORTED")
            : !method.IsEnabled
                ? StorefrontPaymentRuntimeReadiness.Blocked("TENANT_METHOD_DISABLED")
                : !method.IsConfigurationValid
                    ? StorefrontPaymentRuntimeReadiness.Blocked("TENANT_METHOD_CONFIGURATION_INVALID")
                    : await _runtimeReadiness.EvaluateAsync(tenantId, method.Code, cancellationToken);
        method.IsAvailableForCheckout = readiness.IsReady;
        method.CheckoutUnavailableReasonCode = readiness.FirstFailedGate;
    }

    private async Task PublishCreatedEvent(TenantPaymentMethod method, string traceId, CancellationToken cancellationToken)
    {
        var configuration = EvaluateConfiguration(method);

        // S39: Increment version before publishing event
        method.Version++;

        await _eventPublisher.PublishAsync(new PaymentMethodCreatedEvent
        {
            EventId = Guid.NewGuid(),
            TenantId = method.TenantId,
            PaymentMethodId = method.Id,
            Code = method.Code,
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
            PublicConfiguration = configuration.PublicConfiguration.Count == 0
                ? null
                : new Dictionary<string, string>(configuration.PublicConfiguration, StringComparer.OrdinalIgnoreCase),
            IsConfigurationValid = configuration.IsConfigurationValid,
            HasConfiguration = configuration.HasStoredConfiguration,
            OccurredAtUtc = method.UpdatedAtUtc ?? method.UpdatedAt ?? method.CreatedAt,
            Version = method.Version
        }, cancellationToken);
    }

    private async Task PublishUpdatedEvent(TenantPaymentMethod method, bool configurationUpdated, string traceId, CancellationToken cancellationToken)
    {
        var configuration = EvaluateConfiguration(method);

        // S39: Increment version before publishing event
        method.Version++;

        await _eventPublisher.PublishAsync(new PaymentMethodUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            TenantId = method.TenantId,
            PaymentMethodId = method.Id,
            Code = method.Code,
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
            PublicConfiguration = configuration.PublicConfiguration.Count == 0
                ? null
                : new Dictionary<string, string>(configuration.PublicConfiguration, StringComparer.OrdinalIgnoreCase),
            IsConfigurationValid = configuration.IsConfigurationValid,
            HasConfiguration = configuration.HasStoredConfiguration,
            ConfigurationUpdated = configurationUpdated,
            OccurredAtUtc = method.UpdatedAtUtc ?? method.UpdatedAt ?? method.CreatedAt,
            Version = method.Version
        }, cancellationToken);
    }

    private async Task PublishDisabledEvent(TenantPaymentMethod method, CancellationToken cancellationToken)
    {
        // S39: Increment version before publishing event
        method.Version++;

        await _eventPublisher.PublishAsync(new PaymentMethodDisabledEvent
        {
            EventId = Guid.NewGuid(),
            TenantId = method.TenantId,
            PaymentMethodId = method.Id,
            Code = method.Code,
            DisplayName = method.DisplayName,
            OccurredAtUtc = method.UpdatedAtUtc ?? method.UpdatedAt ?? method.CreatedAt,
            Version = method.Version
        }, cancellationToken);
    }

    private ConfigurationMutationResult ApplyConfigurationMutation(
        TenantPaymentMethod method,
        UpsertPaymentMethodDto request,
        string traceId,
        string operation,
        bool rejectEmptyConfigurationPayload)
    {
        if (request.Reset)
        {
            if (!RequiresConfiguration(method.Code))
            {
                return ConfigurationMutationResult.Fail(BadRequest(new
                {
                    error = "CONFIGURATION_RESET_NOT_SUPPORTED",
                    message = $"Payment method '{method.Code}' does not have resettable provider configuration."
                }));
            }

            if (!request.ConfirmReset)
            {
                _reliabilityTelemetry.RecordPaymentConfigInvalid(method.Code, operation, "reset_confirmation_required");
                return ConfigurationMutationResult.Fail(BadRequest(new
                {
                    error = "CONFIGURATION_RESET_CONFIRMATION_REQUIRED",
                    message = "Configuration reset requires explicit confirmation.",
                    validationErrors = new[]
                    {
                        "Set confirmReset to true to clear the stored provider configuration."
                    }
                }));
            }

            var incomingConfiguration = PaymentMethodConfigurationValidator.NormalizeConfiguration(method.Code, request.Configuration);
            if (incomingConfiguration.Count > 0)
            {
                _reliabilityTelemetry.RecordPaymentConfigInvalid(method.Code, operation, "reset_conflict");
                return ConfigurationMutationResult.Fail(BadRequest(new
                {
                    error = "CONFIGURATION_RESET_CONFLICT",
                    message = "Configuration reset cannot be combined with new provider values.",
                    validationErrors = new[]
                    {
                        "Remove configuration fields when requesting an explicit reset."
                    }
                }));
            }

            var actor = GetActor();
            var hadStoredConfiguration = !string.IsNullOrWhiteSpace(method.ConfigEncrypted);
            var wasEnabled = method.IsEnabled;

            method.ConfigEncrypted = null;
            method.IsEnabled = false;

            _logger.LogCritical(
                "Payment method configuration reset and disabled | Code: {Code} | PaymentMethodId: {PaymentMethodId} | TenantId: {TenantId} | Actor: {Actor} | TraceId: {TraceId}",
                method.Code,
                method.Id,
                method.TenantId,
                actor,
                traceId);

            _reliabilityTelemetry.RecordPaymentConfigReset(method.Code);
            if (hadStoredConfiguration)
            {
                _reliabilityTelemetry.RecordPaymentConfigCleared(method.Code);
            }

            return ConfigurationMutationResult.CreateSuccess(
                configurationUpdated: true,
                paymentMethodDisabled: wasEnabled);
        }

        if (request.Configuration == null)
        {
            if (method.Code == "MANUALWALLET" && method.IsEnabled && !EvaluateConfiguration(method).IsConfigurationValid)
            {
                return ConfigurationMutationResult.Fail(BuildConfigurationValidationErrorResult(
                    method.Code, EvaluateConfiguration(method).ValidationErrors,
                    "Enabled Manual Wallet requires a valid persisted recipient. Configure it or reset and disable it."));
            }
            return ConfigurationMutationResult.CreateSuccess(configurationUpdated: false);
        }

        if (method.Code == "MANUALWALLET" && request.Configuration.Any(pair =>
                string.Equals(pair.Key?.Trim(), "walletNumber", StringComparison.OrdinalIgnoreCase) &&
                !PaymentMethodConfigurationValidator.IsValidManualWalletNumber(pair.Value)))
        {
            return ConfigurationMutationResult.Fail(BuildConfigurationValidationErrorResult(
                method.Code, new[] { "A valid recipient wallet number is required. Use reset with confirmation to clear and disable the method." },
                "Manual Wallet recipient cannot be cleared or invalidated."));
        }

        var normalizedIncomingConfiguration = PaymentMethodConfigurationValidator.NormalizeConfiguration(method.Code, request.Configuration);
        if (normalizedIncomingConfiguration.Count == 0)
        {
            if (method.Code == "MANUALWALLET" && method.IsEnabled && !EvaluateConfiguration(method).IsConfigurationValid)
            {
                return ConfigurationMutationResult.Fail(BuildConfigurationValidationErrorResult(
                    method.Code, EvaluateConfiguration(method).ValidationErrors,
                    "Enabled Manual Wallet requires a valid persisted recipient. Configure it or reset and disable it."));
            }
            if (rejectEmptyConfigurationPayload)
            {
                _reliabilityTelemetry.RecordPaymentConfigInvalid(method.Code, operation, "empty_payload");
                return ConfigurationMutationResult.Fail(BadRequest(new
                {
                    error = "EMPTY_CONFIGURATION_NO_CHANGE",
                    message = "Empty configuration payloads do not overwrite existing secrets.",
                    validationErrors = new[]
                    {
                        "Provide one or more provider fields, or use reset plus confirmReset to clear the configuration intentionally."
                    }
                }));
            }

            _logger.LogWarning(
                "Ignored empty payment configuration payload | Code: {Code} | PaymentMethodId: {PaymentMethodId} | TraceId: {TraceId}",
                method.Code,
                method.Id,
                traceId);

            return ConfigurationMutationResult.CreateSuccess(configurationUpdated: false);
        }

        if (!TryReadStoredConfiguration(method, out var existingConfiguration, out var decryptionError))
        {
            _reliabilityTelemetry.RecordPaymentConfigInvalid(method.Code, operation, "decrypt_failed");

            return ConfigurationMutationResult.Fail(BadRequest(new
            {
                error = "CONFIGURATION_RESET_REQUIRED",
                message = "Stored configuration could not be decrypted safely.",
                validationErrors = new[]
                {
                    decryptionError ?? "Use an explicit reset with confirmReset=true before replacing this configuration."
                }
            }));
        }

        var mergedConfiguration = PaymentMethodConfigurationValidator.MergeConfiguration(
            method.Code,
            existingConfiguration,
            normalizedIncomingConfiguration);

        var validation = ApplyEnvironmentValidation(
            method,
            PaymentMethodConfigurationValidator.Validate(method.Code, mergedConfiguration));
        if (!validation.IsConfigurationValid)
        {
            _reliabilityTelemetry.RecordPaymentConfigInvalid(
                method.Code,
                operation,
                validation.ValidationErrors.FirstOrDefault() ?? "config_invalid");

            return ConfigurationMutationResult.Fail(BuildConfigurationValidationErrorResult(
                method.Code,
                validation.ValidationErrors,
                "Payment configuration is incomplete or invalid."));
        }

        method.ConfigEncrypted = _dataProtector.Protect(JsonSerializer.Serialize(validation.NormalizedConfiguration));

        _logger.LogInformation(
            "Payment method configuration updated | Code: {Code} | PaymentMethodId: {PaymentMethodId} | Operation: {Operation} | TraceId: {TraceId}",
            method.Code,
            method.Id,
            operation,
            traceId);

        return ConfigurationMutationResult.CreateSuccess(configurationUpdated: true);
    }

    private bool TryReadStoredConfiguration(
        TenantPaymentMethod method,
        out Dictionary<string, string> configuration,
        out string? error)
    {
        configuration = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        error = null;

        if (string.IsNullOrWhiteSpace(method.ConfigEncrypted))
        {
            return true;
        }

        try
        {
            var decrypted = _dataProtector.Unprotect(method.ConfigEncrypted);
            var deserialized = JsonSerializer.Deserialize<Dictionary<string, string>>(decrypted);
            configuration = PaymentMethodConfigurationValidator.NormalizeConfiguration(method.Code, deserialized);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Stored payment-method configuration could not be decrypted | Code: {Code} | PaymentMethodId: {PaymentMethodId}",
                method.Code,
                method.Id);

            error = "Stored configuration could not be decrypted. Use an explicit reset with confirmReset=true to replace it safely.";
            return false;
        }
    }

    private PaymentMethodConfigurationSnapshot EvaluateConfiguration(TenantPaymentMethod method)
    {
        var hasStoredConfiguration = !string.IsNullOrWhiteSpace(method.ConfigEncrypted);

        if (!hasStoredConfiguration)
        {
            var validation = ApplyEnvironmentValidation(
                method,
                PaymentMethodConfigurationValidator.Validate(method.Code, null));
            return new PaymentMethodConfigurationSnapshot(validation, hasStoredConfiguration);
        }

        if (TryReadStoredConfiguration(method, out var configuration, out var _))
        {
            var validation = ApplyEnvironmentValidation(
                method,
                PaymentMethodConfigurationValidator.Validate(method.Code, configuration));
            return new PaymentMethodConfigurationSnapshot(validation, hasStoredConfiguration);
        }

        return new PaymentMethodConfigurationSnapshot(
            new PaymentMethodConfigurationValidationResult(
                method.Code?.Trim().ToUpperInvariant() ?? string.Empty,
                PaymentMethodConfigurationValidator.RequiresConfiguration(method.Code ?? string.Empty),
                false,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new[] { "Stored payment configuration could not be decrypted." },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)),
            hasStoredConfiguration);
    }

    private IActionResult BuildConfigurationValidationErrorResult(
        string code,
        IReadOnlyList<string> validationErrors,
        string message)
    {
        return BadRequest(new
        {
            error = "INVALID_PAYMENT_CONFIGURATION",
            code,
            message,
            validationErrors = validationErrors.ToArray()
        });
    }

    private static PaymentMethodConfigurationValidationResult ApplyEnvironmentValidation(
        TenantPaymentMethod method,
        PaymentMethodConfigurationValidationResult validation)
    {
        if (string.Equals(method.Code, "STRIPE", StringComparison.OrdinalIgnoreCase) &&
            validation.IsConfigurationValid)
        {
            var stripeMarker = method.IsSandbox ? "_test_" : "_live_";
            var publishableKey = validation.NormalizedConfiguration.GetValueOrDefault("publishableKey", string.Empty);
            var stripeSecretKey = validation.NormalizedConfiguration.GetValueOrDefault("secretKey", string.Empty);
            if (publishableKey.Contains(stripeMarker, StringComparison.Ordinal) &&
                stripeSecretKey.Contains(stripeMarker, StringComparison.Ordinal))
            {
                return validation;
            }

            return validation with
            {
                IsConfigurationValid = false,
                ValidationErrors = validation.ValidationErrors
                    .Append("Stripe keys do not match the selected sandbox/production environment.")
                    .ToArray()
            };
        }

        if (!string.Equals(method.Code, "PAYMOB", StringComparison.OrdinalIgnoreCase) ||
            !validation.IsConfigurationValid)
        {
            return validation;
        }

        var marker = method.IsSandbox ? "_test_" : "_live_";
        var publicKey = validation.NormalizedConfiguration.GetValueOrDefault("publicKey", string.Empty);
        var secretKey = validation.NormalizedConfiguration.GetValueOrDefault("secretKey", string.Empty);
        if (publicKey.Contains(marker, StringComparison.Ordinal) &&
            secretKey.Contains(marker, StringComparison.Ordinal))
        {
            return validation;
        }

        return validation with
        {
            IsConfigurationValid = false,
            ValidationErrors = validation.ValidationErrors
                .Append("Paymob keys do not match the selected sandbox/production environment.")
                .ToArray()
        };
    }

    private string GetActor() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ??
        User.Identity?.Name ??
        "unknown";

    private string GetTraceId() => Activity.Current?.Id ?? HttpContext.TraceIdentifier;

    /// <summary>
    /// T-09: deterministic Fawry fail-closed rejection for admin mutations.
    /// Called before any tenant-row read that could lead to a mutation, so no
    /// tenant row or outbox event is created.
    /// </summary>
    private ObjectResult BuildFawryAuthorityRejection() =>
        StatusCode(
            StatusCodes.Status422UnprocessableEntity,
            new ApiErrorResponse(
                FawryFinancialAuthorityPolicy.Code,
                PaymentErrorLocalization.GetFawryUnavailableMessage(Request.Headers.AcceptLanguage),
                GetTraceId()));

    private void SetNoStoreHeaders()
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";
    }

    private sealed record ConfigurationMutationResult(
        bool Success,
        bool ConfigurationUpdated,
        bool PaymentMethodDisabled,
        IActionResult? ErrorResult)
    {
        public static ConfigurationMutationResult CreateSuccess(
            bool configurationUpdated,
            bool paymentMethodDisabled = false) =>
            new(true, configurationUpdated, paymentMethodDisabled, null);

        public static ConfigurationMutationResult Fail(IActionResult errorResult) =>
            new(false, false, false, errorResult);
    }

    private sealed record PaymentMethodConfigurationSnapshot(
        string Code,
        bool RequiresConfiguration,
        bool IsConfigurationValid,
        bool HasStoredConfiguration,
        IReadOnlyDictionary<string, string> NormalizedConfiguration,
        IReadOnlyList<string> ValidationErrors,
        IReadOnlyDictionary<string, string> PublicConfiguration)
    {
        public PaymentMethodConfigurationSnapshot(
            PaymentMethodConfigurationValidationResult validation,
            bool hasStoredConfiguration)
            : this(
                validation.Code,
                validation.RequiresConfiguration,
                validation.IsConfigurationValid,
                hasStoredConfiguration,
                validation.NormalizedConfiguration,
                validation.ValidationErrors,
                validation.PublicConfiguration)
        {
        }
    }

    #endregion
}

#region DTOs

public class PaymentMethodResponseDto
{
    public int Version { get; set; }
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameAr { get; set; }
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public bool IsEnabled { get; set; }
    public int SortOrder { get; set; }
    public bool SupportsOnline { get; set; }
    public bool SupportsOffline { get; set; }
    public bool IsSandbox { get; set; }
    public string? Icon { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public decimal? MaxOrderAmount { get; set; }
    public decimal Fee { get; set; }
    public string FeeType { get; set; } = "fixed";
    public bool RequiresConfiguration { get; set; }
    public bool IsConfigurationValid { get; set; }
    // Populated on fresh reads, not inferred from a successful configuration write.
    public bool? IsAvailableForCheckout { get; set; }
    public string? CheckoutUnavailableReasonCode { get; set; }
    public List<string> ConfigurationErrors { get; set; } = new();
    // Allow-listed customer-facing recipient only; never gateway credentials.
    public Dictionary<string, string> PublicConfiguration { get; set; } = new();
    /// <summary>
    /// T-09: false for Fawry, which has no certified financial settlement authority.
    /// </summary>
    public bool IsSupported { get; set; } = true;
    public string? UnsupportedReasonCode { get; set; }
    public string? UnsupportedMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class PaymentMethodCatalogProviderDto
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? DisplayNameAr { get; set; }
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public int SortOrder { get; set; }
    public bool SupportsOnline { get; set; }
    public bool SupportsOffline { get; set; }
    public bool IsSandbox { get; set; }
    public string? Icon { get; set; }
    public decimal Fee { get; set; }
    public string FeeType { get; set; } = "fixed";
    public bool RequiresConfiguration { get; set; }
    public bool IsSupported { get; set; }
    /// <summary>
    /// T-09: stable reason code when <see cref="IsSupported"/> is false.
    /// </summary>
    public string? UnsupportedReasonCode { get; set; }
    public string? UnsupportedMessage { get; set; }
}

public class UpsertPaymentMethodDto
{
    public string? Validate()
    {
        if (DisplayName != null && (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Length > 100))
            return "Display name must contain 1 to 100 characters.";
        if (DisplayNameAr?.Length > 100 || Description?.Length > 500 || DescriptionAr?.Length > 500 || Icon?.Length > 50)
            return "Payment method text exceeds its supported length.";
        if (SortOrder < 0) return "Sort order cannot be negative.";
        if (Fee < 0 || MinOrderAmount < 0 || MaxOrderAmount < 0)
            return "Payment fees and order limits cannot be negative.";
        if (MinOrderAmount.HasValue && MaxOrderAmount.HasValue && MinOrderAmount > MaxOrderAmount)
            return "Maximum order amount must be greater than or equal to minimum order amount.";
        if (FeeType != null && FeeType is not ("fixed" or "percentage"))
            return "Fee type must be fixed or percentage.";
        const decimal max = 99999999999999.9999m;
        if (Fee > max || MinOrderAmount > max || MaxOrderAmount > max)
            return "Payment fee or order limit exceeds the supported amount.";
        return null;
    }

    public string? DisplayName { get; set; }
    public string? DisplayNameAr { get; set; }
    public string? Description { get; set; }
    public string? DescriptionAr { get; set; }
    public int? SortOrder { get; set; }
    public bool? IsSandbox { get; set; }
    public string? Icon { get; set; }
    public decimal? MinOrderAmount { get; set; }
    public decimal? MaxOrderAmount { get; set; }
    public decimal? Fee { get; set; }
    public string? FeeType { get; set; }
    
    /// <summary>
    /// Provider-specific configuration (encrypted before storage).
    /// 
    /// STRIPE: { "publishableKey": "pk_...", "secretKey": "sk_...", "webhookSecret": "whsec_..." }
    /// PAYMOB: { "publicKey": "egy_pk_...", "secretKey": "egy_sk_...", "integrationId": "...", "hmacSecret": "..." }
    /// COD: null (no configuration needed)
    /// </summary>
    public Dictionary<string, string>? Configuration { get; set; }

    public bool Reset { get; set; }
    public bool ConfirmReset { get; set; }
}

#endregion
