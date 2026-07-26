using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using System.Linq;
using PointOfSale.API.DTOs.Analytics;
using PointOfSale.API.Infrastructure.Http;
using PointOfSale.API.Services.Analytics;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Events;
using PointOfSale.Core.Financial;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using EntityState = Microsoft.EntityFrameworkCore.EntityState;
using OutboxMessage = PointOfSale.Core.Entities.OutboxMessage;
using AccountingOutboxMessage = PointOfSale.Core.Entities.Accounting.AccountingOutboxMessage;
using MarketplaceEventOutboxMessage = PointOfSale.Core.Entities.MarketplaceEventOutboxMessage;

namespace PointOfSale.API.Controllers;

/// <summary>
/// Storefront Analytics Event Ingestion API.
/// 
/// PHASE 1: Canonical Event Foundation
/// 
/// This controller is the SINGLE ENTRY POINT for all storefront analytics events.
/// It validates, enriches, and durably enqueues events through the MassTransit EF outbox.
/// 
/// ARCHITECTURAL RULES:
/// 1. Controller NEVER writes to analytics stores directly
/// 2. Events are durably written to the bus outbox before success is returned
/// 3. Event processing remains async after durable enqueue
/// 4. Both guest and authenticated users can submit events
/// 5. Rate limiting is enforced per session/customer
/// 6. Payload size limits are enforced
/// 
/// SECURITY:
/// - TenantId from X-Tenant-Id header (required)
/// - CustomerId from JWT token (optional - for authenticated users)
/// - IP address captured server-side
/// - User agent captured server-side
/// - All string inputs sanitized
/// 
/// IDEMPOTENCY:
/// - EventId is client-generated (recommended) or server-generated
/// - Same EventId = same event (deduplicated by consumer)
/// </summary>
[ApiController]
[Route("api/storefront/analytics")]
[EnableRateLimiting("analytics")]
public class StorefrontAnalyticsController : ControllerBase
{
    private readonly IAnalyticsEventValidator _validator;
    private readonly IAnalyticsPayloadGuard _payloadGuard;
    private readonly IAnalyticsIngestionRateLimiter _rateLimiter;
    private readonly IAnalyticsTenantCurrencyResolver _currencyResolver;
    private readonly IEventPublisher _eventPublisher;
    private readonly ApplicationDbContext _dbContext;
    private readonly AnalyticsReliabilityTelemetry _telemetry;
    private readonly CommerceReliabilityTelemetry _commerceReliabilityTelemetry;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<StorefrontAnalyticsController> _logger;

    // Limits
    private const int MaxBatchSize = 50;

    public StorefrontAnalyticsController(
        IAnalyticsEventValidator validator,
        IAnalyticsPayloadGuard payloadGuard,
        IAnalyticsIngestionRateLimiter rateLimiter,
        IAnalyticsTenantCurrencyResolver currencyResolver,
        IEventPublisher eventPublisher,
        ApplicationDbContext dbContext,
        AnalyticsReliabilityTelemetry telemetry,
        CommerceReliabilityTelemetry commerceReliabilityTelemetry,
        ITenantContext tenantContext,
        ILogger<StorefrontAnalyticsController> logger)
    {
        _validator = validator;
        _payloadGuard = payloadGuard;
        _rateLimiter = rateLimiter;
        _currencyResolver = currencyResolver;
        _eventPublisher = eventPublisher;
        _dbContext = dbContext;
        _telemetry = telemetry;
        _commerceReliabilityTelemetry = commerceReliabilityTelemetry;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Submit a single analytics event.
    /// 
    /// Works for both guest and authenticated users.
    /// - Guests: Must provide SessionId in Actor
    /// - Authenticated: CustomerId resolved from JWT
    /// </summary>
    /// <param name="request">The analytics event</param>
    /// <returns>Acceptance confirmation</returns>
    [HttpPost("event")]
    [AllowAnonymous] // Both guests and authenticated users can submit
    [ProducesResponseType(typeof(AnalyticsEventResponseDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(AnalyticsEventResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AnalyticsEventResponseDto), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SubmitEvent([FromBody] AnalyticsEventRequestDto request, CancellationToken cancellationToken)
    {
        // Resolve tenant
        var tenantId = ResolveTenantId();
        if (tenantId == Guid.Empty)
        {
            return BadRequest(AnalyticsEventResponseDto.Failure("X-Tenant-Id header is required"));
        }

        if (request.Type == AnalyticsEventType.PurchaseCompleted)
        {
            return BadRequest(AnalyticsEventResponseDto.Failure(
                "PurchaseCompleted must be emitted by the server after payment succeeds."));
        }

        // Resolve customer (null for guests)
        var customerId = ResolveCustomerId();
        var requestSource = GetClientIpAddress();

        // Compute rate limiter identity: prefer customerId, then SessionId, then IP
        var requestIdentity = !string.IsNullOrEmpty(customerId?.ToString())
            ? $"customer:{customerId}"
            : (!string.IsNullOrEmpty(request.Actor?.SessionId)
                ? $"session:{request.Actor.SessionId}"
                : $"ip:{requestSource}");

        var rateLimit = await _rateLimiter.CheckAsync(tenantId, requestIdentity, 1);
        if (!rateLimit.Allowed)
        {
            Response.Headers["Retry-After"] = rateLimit.RetryAfterSeconds.ToString();
            _logger.LogWarning(
                "[Analytics] Request rate limited | TenantId: {TenantId} | RequestIdentity: {RequestIdentity} | RequestSource: {RequestSource} | Reason: {Reason}",
                tenantId,
                requestIdentity,
                requestSource,
                rateLimit.Reason);

            return StatusCode(
                StatusCodes.Status429TooManyRequests,
                AnalyticsEventResponseDto.Failure("Analytics ingestion is temporarily rate limited. Please retry."));
        }

        var payloadInspection = _payloadGuard.Inspect(request.Payload);
        _telemetry.RecordPayloadSize(tenantId, payloadInspection.SerializedSizeBytes, payloadInspection.IsValid ? "within_limits" : "rejected");
        if (!payloadInspection.IsValid)
        {
            _telemetry.RecordPayloadRejected(tenantId, payloadInspection.Reason);
            _logger.LogWarning(
                "[Analytics] Payload rejected before enqueue | TenantId: {TenantId} | RequestSource: {RequestSource} | Reason: {Reason} | PayloadSizeBytes: {PayloadSizeBytes}",
                tenantId,
                requestSource,
                payloadInspection.Reason,
                payloadInspection.SerializedSizeBytes);

            return BadRequest(AnalyticsEventResponseDto.Failure("Analytics payload exceeds allowed limits."));
        }

        var currencyResolution = await _currencyResolver.ResolveAsync(
            tenantId,
            new[] { request.Payload?.OrderId },
            cancellationToken);

        // Capture server-side context
        var ipAddress = requestSource;
        var userAgent = Request.Headers.UserAgent.ToString();
        var acceptLanguage = Request.Headers.AcceptLanguage.ToString();
        if (!TryResolveCurrency(tenantId, request.Payload?.OrderId, currencyResolution, out var currency, out var currencyError))
        {
            return BadRequest(AnalyticsEventResponseDto.Failure(currencyError));
        }

        // Validate and transform
        var result = _validator.Validate(
            request,
            tenantId,
            customerId,
            ipAddress,
            userAgent,
            acceptLanguage,
            currency);

        if (!result.IsValid)
        {
            _logger.LogWarning(
                "[Analytics] Event validation failed | TenantId: {TenantId} | RequestSource: {RequestSource} | Error: {Error}",
                tenantId,
                requestSource,
                result.Error);
            
            return BadRequest(AnalyticsEventResponseDto.Failure(result.Error!));
        }

        // Publish to event bus (non-blocking)
        try
        {
            var integrationEvent = AnalyticsEventReceivedEvent.Create(result.Event!);
            await _eventPublisher.PublishAsync(integrationEvent, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            
                // Post-commit telemetry must not throw and convert a successful commit into 5xx
                try
                {
                    _telemetry.RecordEnqueueSuccess(1);
                    RecordReliabilityMetric(request);
                }
                catch (Exception telemetryEx)
                {
                _logger.LogWarning(telemetryEx,
                    "[Analytics] Post-commit telemetry failed (non-fatal) | TenantId: {TenantId} | EventId: {EventId}",
                    tenantId, result.Event!.EventId);
            }

            _logger.LogDebug(
                "[Analytics] Event durably accepted | TenantId: {TenantId} | EventId: {EventId} | Type: {Type} | Currency: {Currency}",
                tenantId, result.Event!.EventId, result.Event.Type, currency);

            return Accepted(AnalyticsEventResponseDto.Success(result.Event!.EventId));
        }
        catch (Exception ex)
        {
            _telemetry.RecordEnqueueFailure(1);
            _logger.LogError(ex,
                "[Analytics] Failed to persist event to outbox | TenantId: {TenantId} | EventId: {EventId}",
                tenantId, result.Event?.EventId);

            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                AnalyticsEventResponseDto.Failure("Analytics event could not be stored. Please retry."));
        }
    }

    /// <summary>
    /// Submit multiple analytics events in a batch.
    /// 
    /// Useful for:
    /// - Buffered events from offline mode
    /// - High-traffic pages with many impressions
    /// - Reducing HTTP overhead
    /// 
    /// Maximum 50 events per batch.
    /// </summary>
    /// <param name="request">Batch of analytics events</param>
    /// <returns>Batch acceptance summary</returns>
    [HttpPost("events")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AnalyticsEventBatchResponseDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(AnalyticsEventBatchResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AnalyticsEventBatchResponseDto), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SubmitEventBatch([FromBody] AnalyticsEventBatchRequestDto request, CancellationToken cancellationToken)
    {
        // Validate batch size
        if (request.Events == null || request.Events.Count == 0)
        {
            return BadRequest(new AnalyticsEventBatchResponseDto
            {
                Errors = new List<AnalyticsEventErrorDto>
                {
                    new() { Index = -1, Error = "Events array is empty" }
                }
            });
        }

        if (request.Events.Count > MaxBatchSize)
        {
            return BadRequest(new AnalyticsEventBatchResponseDto
            {
                Errors = new List<AnalyticsEventErrorDto>
                {
                    new() { Index = -1, Error = $"Batch size exceeds maximum ({MaxBatchSize})" }
                }
            });
        }

        // Resolve context (same for all events in batch)
        var tenantId = ResolveTenantId();
        if (tenantId == Guid.Empty)
        {
            return BadRequest(new AnalyticsEventBatchResponseDto
            {
                Errors = new List<AnalyticsEventErrorDto>
                {
                    new() { Index = -1, Error = "X-Tenant-Id header is required" }
                }
            });
        }

        var customerId = ResolveCustomerId();
        var requestSource = GetClientIpAddress();

        // Compute rate limiter identity: prefer customerId, then SessionId from first event, then IP
        var firstEventSessionId = request.Events.FirstOrDefault()?.Actor?.SessionId;
        var requestIdentity = !string.IsNullOrEmpty(customerId?.ToString())
            ? $"customer:{customerId}"
            : (!string.IsNullOrEmpty(firstEventSessionId)
                ? $"session:{firstEventSessionId}"
                : $"ip:{requestSource}");

        var rateLimit = await _rateLimiter.CheckAsync(tenantId, requestIdentity, request.Events.Count);
        if (!rateLimit.Allowed)
        {
            Response.Headers["Retry-After"] = rateLimit.RetryAfterSeconds.ToString();
            _logger.LogWarning(
                "[Analytics] Batch request rate limited | TenantId: {TenantId} | RequestIdentity: {RequestIdentity} | RequestSource: {RequestSource} | Reason: {Reason}",
                tenantId,
                requestIdentity,
                requestSource,
                rateLimit.Reason);

            return StatusCode(
                StatusCodes.Status429TooManyRequests,
                new AnalyticsEventBatchResponseDto
                {
                    Errors =
                    {
                        new AnalyticsEventErrorDto
                        {
                            Index = -1,
                            Error = "Analytics ingestion is temporarily rate limited. Please retry."
                        }
                    }
                });
        }

        var ipAddress = requestSource;
        var userAgent = Request.Headers.UserAgent.ToString();
        var acceptLanguage = Request.Headers.AcceptLanguage.ToString();

        var response = new AnalyticsEventBatchResponseDto();
        var acceptedEventIds = new List<Guid>();
        var acceptedRequests = new List<AnalyticsEventRequestDto>();
        var validatedRequests = new List<(int Index, AnalyticsEventRequestDto Request)>();

        // First pass: filter by payload validation
        for (int i = 0; i < request.Events.Count; i++)
        {
            var eventRequest = request.Events[i];
            if (eventRequest.Type == AnalyticsEventType.PurchaseCompleted)
            {
                response.Rejected++;
                response.Errors.Add(new AnalyticsEventErrorDto
                {
                    Index = i,
                    EventId = eventRequest.EventId,
                    Error = "PurchaseCompleted must be emitted by the server after payment succeeds."
                });
                continue;
            }

            var payloadInspection = _payloadGuard.Inspect(eventRequest.Payload);
            _telemetry.RecordPayloadSize(tenantId, payloadInspection.SerializedSizeBytes, payloadInspection.IsValid ? "within_limits" : "rejected");
            
            if (!payloadInspection.IsValid)
            {
                _telemetry.RecordPayloadRejected(tenantId, payloadInspection.Reason);
                response.Rejected++;
                response.Errors.Add(new AnalyticsEventErrorDto
                {
                    Index = i,
                    EventId = eventRequest.EventId,
                    Error = "Analytics payload exceeds allowed limits."
                });

                _logger.LogWarning(
                    "[Analytics] Batch payload rejected before enqueue | TenantId: {TenantId} | RequestSource: {RequestSource} | Index: {Index} | Reason: {Reason} | PayloadSizeBytes: {PayloadSizeBytes}",
                    tenantId,
                    requestSource,
                    i,
                    payloadInspection.Reason,
                    payloadInspection.SerializedSizeBytes);
                continue;
            }

            validatedRequests.Add((i, eventRequest));
        }

        // Resolve currency only for validated requests
        var currencyResolution = await _currencyResolver.ResolveAsync(
            tenantId,
            validatedRequests.Select(vr => vr.Request.Payload?.OrderId),
            cancellationToken);

        // Second pass: process validated requests
        for (int j = 0; j < validatedRequests.Count; j++)
        {
            var (i, eventRequest) = validatedRequests[j];

            if (!TryResolveCurrency(tenantId, eventRequest.Payload?.OrderId, currencyResolution, out var currency, out var currencyError))
            {
                response.Rejected++;
                response.Errors.Add(new AnalyticsEventErrorDto
                {
                    Index = i,
                    EventId = eventRequest.EventId,
                    Error = currencyError
                });
                continue;
            }

            var result = _validator.Validate(
                eventRequest,
                tenantId,
                customerId,
                ipAddress,
                userAgent,
                acceptLanguage,
                currency);

            if (!result.IsValid)
            {
                response.Rejected++;
                response.Errors.Add(new AnalyticsEventErrorDto
                {
                    Index = i,
                    EventId = eventRequest.EventId,
                    Error = result.Error!
                });
                continue;
            }

            // Capture snapshot before this iteration to detect entries added during publish
            // Store the underlying entity instances (not EntityEntry wrappers) for stable identity comparison
            var preIterationOutboxEntries = _dbContext.ChangeTracker.Entries()
                .Where(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added &&
                            IsOutboxInfrastructureEntity(e.Entity))
                .Select(e => e.Entity)
                .ToHashSet();

            try
            {
                var integrationEvent = AnalyticsEventReceivedEvent.Create(result.Event!);
                await _eventPublisher.PublishAsync(integrationEvent, cancellationToken);
                acceptedEventIds.Add(result.Event!.EventId);
                acceptedRequests.Add(eventRequest);
            }
            catch (Exception ex)
            {
                // Selectively detach only the outbox infrastructure entries added in this failed iteration
                // to prevent orphaned outbox messages from being persisted.
                // We must NOT clear the entire ChangeTracker, as that would drop previously
                // staged successful outbox records from earlier iterations, making acceptedEventIds
                // inconsistent with what will actually be persisted.
                var currentOutboxEntries = _dbContext.ChangeTracker.Entries()
                    .Where(e => e.State == EntityState.Added &&
                                IsOutboxInfrastructureEntity(e.Entity))
                    .ToList();

                // Detach only entries that were added in this iteration (not in the pre-iteration snapshot)
                // Compare the underlying entity instances, not the EntityEntry wrappers
                var entriesToDetach = currentOutboxEntries
                    .Where(e => !preIterationOutboxEntries.Contains(e.Entity))
                    .ToList();

                foreach (var entry in entriesToDetach)
                {
                    entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                }

                _logger.LogWarning(ex,
                    "[Analytics] Failed to publish batch event | TenantId: {TenantId} | RequestSource: {RequestSource} | Index: {Index} | EventId: {EventId}",
                    tenantId,
                    requestSource,
                    i,
                    result.Event?.EventId);

                response.Rejected++;
                response.Errors.Add(new AnalyticsEventErrorDto
                {
                    Index = i,
                    EventId = result.Event?.EventId,
                    Error = "Failed to queue analytics event. Please retry."
                });
            }
        }

        if (acceptedEventIds.Count > 0)
        {
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                
                // Post-commit telemetry must not throw and convert a successful commit into 5xx
                try
                {
                    _telemetry.RecordEnqueueSuccess(acceptedEventIds.Count);
                    RecordReliabilityMetrics(acceptedRequests);
                }
                catch (Exception telemetryEx)
                {
                    _logger.LogWarning(telemetryEx,
                        "[Analytics] Post-commit telemetry failed (non-fatal) | TenantId: {TenantId} | AcceptedCount: {AcceptedCount}",
                        tenantId, acceptedEventIds.Count);
                }
                
                response.Accepted = acceptedEventIds.Count;
                response.AcceptedEventIds.AddRange(acceptedEventIds);
            }
            catch (Exception ex)
            {
                _telemetry.RecordEnqueueFailure(acceptedEventIds.Count);
                _logger.LogError(ex,
                    "[Analytics] Failed to persist batch to outbox | TenantId: {TenantId} | AcceptedCount: {AcceptedCount}",
                    tenantId,
                    acceptedEventIds.Count);

                return StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    new AnalyticsEventBatchResponseDto
                    {
                        Accepted = 0,
                        Rejected = response.Rejected + acceptedEventIds.Count,
                        Errors =
                        {
                            new AnalyticsEventErrorDto
                            {
                                Index = -1,
                                Error = "Analytics events could not be stored. Please retry the batch."
                            }
                        }
                    });
            }
        }

        _logger.LogInformation(
            "[Analytics] Batch durably processed | TenantId: {TenantId} | RequestSource: {RequestSource} | Accepted: {Accepted} | Rejected: {Rejected}",
            tenantId,
            requestSource,
            response.Accepted,
            response.Rejected);

        return Accepted(response);
    }

    /// <summary>
    /// Health check for analytics endpoint.
    /// Used by frontend SDK to verify connectivity.
    /// </summary>
    [HttpGet("health")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Health()
    {
        return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
    }

    #region Helpers

    private Guid ResolveTenantId()
    {
        if (_tenantContext.HasTenant && _tenantContext.TenantId.HasValue)
        {
            return _tenantContext.TenantId.Value;
        }

        return PointOfSale.API.Helpers.StorefrontTenantContextResolver.GetTenantId(HttpContext) ?? Guid.Empty;
    }

    private Guid? ResolveCustomerId()
    {
        // Check if user is authenticated with StorefrontJwt
        var customerIdClaim = User.FindFirst("CustomerId")?.Value;
        if (!string.IsNullOrEmpty(customerIdClaim) && Guid.TryParse(customerIdClaim, out var customerId))
        {
            return customerId;
        }

        return null;
    }

    private string GetClientIpAddress()
    {
        return ClientIpAddressResolver.GetTrustedClientIp(HttpContext, "0.0.0.0");
    }

    private bool TryResolveCurrency(
        Guid tenantId,
        Guid? orderId,
        AnalyticsCurrencyResolution resolution,
        out string currency,
        out string error)
    {
        var context = $"tenantId:{tenantId:D};orderId:{orderId?.ToString("D") ?? "none"}";

        try
        {
            var tenantCurrency = FinancialCurrencyGuard.RequireCurrency(
                resolution.TenantCurrency,
                "analytics.tenant.currency",
                context);

            if (orderId.HasValue && orderId.Value != Guid.Empty)
            {
                if (!resolution.OrderCurrencies.TryGetValue(orderId.Value, out var orderCurrency))
                {
                    _commerceReliabilityTelemetry.RecordCurrencyMissing(tenantId, orderId, "analytics");
                    _logger.LogError(
                        "[Analytics] Missing order currency for analytics event | TenantId: {TenantId} | OrderId: {OrderId}",
                        tenantId,
                        orderId.Value);

                    currency = string.Empty;
                    error = "Analytics event rejected because order currency is missing.";
                    return false;
                }

                currency = FinancialCurrencyGuard.RequireMatch(
                    orderCurrency ?? string.Empty,
                    "analytics.order.currency",
                    tenantCurrency,
                    "analytics.tenant.currency",
                    context);
            }
            else
            {
                currency = tenantCurrency;
            }

            error = string.Empty;
            return true;
        }
        catch (FinancialCurrencyMismatchException ex)
        {
            _commerceReliabilityTelemetry.RecordCurrencyMismatch(tenantId, orderId, "analytics");
            _logger.LogError(
                ex,
                "[Analytics] Currency mismatch rejected | TenantId: {TenantId} | OrderId: {OrderId}",
                tenantId,
                orderId);
            currency = string.Empty;
            error = "Analytics event rejected because currency is inconsistent with tenant configuration.";
            return false;
        }
        catch (FinancialCurrencyMissingException ex)
        {
            _commerceReliabilityTelemetry.RecordCurrencyMissing(tenantId, orderId, "analytics");
            _logger.LogError(
                ex,
                "[Analytics] Missing currency rejected | TenantId: {TenantId} | OrderId: {OrderId}",
                tenantId,
                orderId);
            currency = string.Empty;
            error = "Analytics event rejected because currency is missing.";
            return false;
        }
        catch (FinancialCurrencyInvalidException ex)
        {
            _commerceReliabilityTelemetry.RecordCurrencyInvalid(tenantId, orderId, "analytics");
            _logger.LogError(
                ex,
                "[Analytics] Invalid currency rejected | TenantId: {TenantId} | OrderId: {OrderId}",
                tenantId,
                orderId);
            currency = string.Empty;
            error = "Analytics event rejected because currency is invalid.";
            return false;
        }

        currency = string.Empty;
        error = "Analytics event rejected because currency could not be resolved.";
        return false;
    }

    /// <summary>
    /// Determines if an entity is outbox infrastructure (should not be persisted with business data).
    /// Matches the predicate used by MassTransitEventPublisher.IsOutboxInfrastructureEntity.
    /// </summary>
    private static bool IsOutboxInfrastructureEntity(object entity)
    {
        return entity is OutboxMessage or AccountingOutboxMessage or MarketplaceEventOutboxMessage ||
               entity.GetType().FullName?.StartsWith("MassTransit.EntityFrameworkCoreIntegration.Outbox", StringComparison.Ordinal) == true;
    }

    private void RecordReliabilityMetric(AnalyticsEventRequestDto request)
    {
        switch (request.Type)
        {
            case AnalyticsEventType.CheckoutFinalizeFailure:
                _commerceReliabilityTelemetry.RecordCheckoutFinalizeFailure(ExtractExtraValue(request, "reason"));
                break;
            case AnalyticsEventType.AuthStateDegraded:
                _commerceReliabilityTelemetry.RecordAuthStateDegraded(
                    ExtractExtraValue(request, "source"),
                    ExtractExtraValue(request, "reason"));
                break;
            case AnalyticsEventType.CheckoutSelectionInvalidated:
                _commerceReliabilityTelemetry.RecordCheckoutSelectionInvalidated(
                    ExtractExtraValue(request, "selectionType"),
                    ExtractExtraValue(request, "reason"));
                break;
            case AnalyticsEventType.CartStateMismatchDetected:
                _commerceReliabilityTelemetry.RecordCartStateMismatchDetected(
                    ExtractExtraValue(request, "surface"),
                    ExtractExtraValue(request, "reason"));
                break;
            case AnalyticsEventType.GuestPaymentRecoverySuccess:
                _commerceReliabilityTelemetry.RecordGuestPaymentRecoverySuccess(
                    ExtractExtraValue(request, "source"));
                break;
            case AnalyticsEventType.CartUnexpectedClearBlocked:
                _commerceReliabilityTelemetry.RecordCartUnexpectedClearBlocked(
                    ExtractExtraValue(request, "reason"));
                break;
            case AnalyticsEventType.TrackingTokenRemovedFromUrl:
                _commerceReliabilityTelemetry.RecordTrackingTokenRemovedFromUrl(
                    ExtractExtraValue(request, "source"));
                break;
            case AnalyticsEventType.AuthDegradedRecovery:
                _commerceReliabilityTelemetry.RecordAuthDegradedRecovery(
                    ExtractExtraValue(request, "source"),
                    ExtractExtraValue(request, "outcome"));
                break;
            case AnalyticsEventType.CheckoutStateLeakPrevented:
                _commerceReliabilityTelemetry.RecordCheckoutStateLeakPrevented(
                    ExtractExtraValue(request, "trigger"));
                break;
        }
    }

    private void RecordReliabilityMetrics(IEnumerable<AnalyticsEventRequestDto> requests)
    {
        foreach (var request in requests)
        {
            RecordReliabilityMetric(request);
        }
    }

    private static string ExtractExtraValue(AnalyticsEventRequestDto request, string key)
    {
        if (request.Payload?.Extra == null ||
            !request.Payload.Extra.TryGetValue(key, out var rawReason) ||
            rawReason == null)
        {
            return "unknown";
        }

        var reason = rawReason.ToString();
        return string.IsNullOrWhiteSpace(reason)
            ? "unknown"
            : reason.Trim();
    }

    #endregion
}
