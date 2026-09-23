using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events.AbandonedCart;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Tenancy;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using Microsoft.EntityFrameworkCore;

namespace PointOfSale.Infrastructure.Jobs;

/// <summary>
/// Background job that scans for abandoned carts and triggers recovery workflows.
/// Phase: Smart Abandoned Cart Recovery Engine - Phase 2
/// 
/// SCHEDULE: Every 5 minutes
/// 
/// RESPONSIBILITIES:
/// - Find carts idle beyond FirstMessageDelayMinutes
/// - Check tenant eligibility
/// - Mark carts as abandoned
/// - Emit TriggerRecoveryEvent
/// 
/// IDEMPOTENCY:
/// - Only processes carts with RecoveryStatus = Active
/// - MarkAsTriggeredAsync prevents duplicate triggers
/// 
/// SAFETY:
/// - All exceptions caught and logged
/// - Never throws (prevents job scheduler crashes)
/// - Processes carts one at a time (no batch failures)
/// </summary>
public class AbandonedCartScannerJob
{
    private readonly IAbandonedCartService _cartService;
    private readonly IAbandonedCartEligibilityService _eligibilityService;
    private readonly ITenantAutomationSettingsService _settingsService;
    private readonly IEventPublisher _eventPublisher;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly CommerceReliabilityTelemetry _telemetry;
    private readonly ILogger<AbandonedCartScannerJob> _logger;

    public AbandonedCartScannerJob(
        IAbandonedCartService cartService,
        IAbandonedCartEligibilityService eligibilityService,
        ITenantAutomationSettingsService settingsService,
        IEventPublisher eventPublisher,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        CommerceReliabilityTelemetry telemetry,
        ILogger<AbandonedCartScannerJob> logger)
    {
        _cartService = cartService;
        _eligibilityService = eligibilityService;
        _settingsService = settingsService;
        _eventPublisher = eventPublisher;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _telemetry = telemetry;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        var scanCorrelationId = Guid.NewGuid().ToString("N");

        try
        {
            _logger.LogInformation(
                "[Abandoned Cart Scanner] Starting scan | CorrelationId: {CorrelationId}",
                scanCorrelationId);

            var discoveredCount = 0;
            var processedCount = 0;
            var skippedCount = 0;
            var triggeredCount = 0;

            await foreach (var cart in _cartService.FindCartsReadyForAbandonmentAsync(ct))
            {
                discoveredCount++;

                try
                {
                    // Check tenant eligibility
                    var isEligible = await _eligibilityService.CanTriggerAbandonedRecoveryAsync(cart.TenantId, ct);
                    if (!isEligible)
                    {
                        _logger.LogInformation(
                            "[Abandoned Cart Scanner] Tenant not eligible, skipping | TenantId: {TenantId} | CartId: {CartId} | SessionId: {SessionId} | CustomerId: {CustomerId} | CorrelationId: {CorrelationId}",
                            cart.TenantId,
                            cart.CartId,
                            cart.SessionId,
                            cart.CustomerId,
                            scanCorrelationId);
                        _telemetry.RecordAbandonedCartSkipped(cart.TenantId, "recovery_scanner", "tenant_not_eligible");
                        skippedCount++;
                        continue;
                    }

                    var contact = DetermineRecoveryContact(cart);
                    if (contact == null)
                    {
                        await _cartService.MarkAsAbandonedAsync(cart.Id, ct);

                        _logger.LogInformation(
                            "[Abandoned Cart Scanner] Recovery skipped - no verified contact | TenantId: {TenantId} | CartId: {CartId} | SessionId: {SessionId} | CustomerId: {CustomerId} | CorrelationId: {CorrelationId}",
                            cart.TenantId,
                            cart.CartId,
                            cart.SessionId,
                            cart.CustomerId,
                            scanCorrelationId);

                        _telemetry.RecordRecoverySkippedNoContact(cart.TenantId, "no_verified_contact");
                        skippedCount++;
                        processedCount++;
                        continue;
                    }

                    // Get tenant settings for recovery parameters
                    var settings = await _settingsService.GetSettingsAsync(cart.TenantId, ct);

                    var storefrontContext = await ResolveStorefrontContextAsync(cart.TenantId, ct);
                    var storeName = storefrontContext.StoreName;
                    var storefrontBaseUrl = storefrontContext.StorefrontBaseUrl;

                    var recoveryToken = _cartService.GenerateRecoveryToken(cart);
                    var recoveryLink = $"{storefrontBaseUrl}/recover-cart/{Uri.EscapeDataString(recoveryToken)}";

                    // Emit trigger event
                    var triggerEvent = new TriggerRecoveryEvent
                    {
                        TenantId = cart.TenantId,
                        AbandonedCartId = cart.Id,
                        CartId = cart.CartId,
                        SessionId = cart.SessionId,
                        CustomerId = cart.CustomerId,
                        PhoneNumber = contact.Channel == "phone" ? contact.Value : null,
                        Email = contact.Channel == "email" ? contact.Value : null,
                        ContactChannel = contact.Channel,
                        CartSnapshotJson = cart.CartSnapshotJson,
                        FirstMessageDelayMinutes = settings.FirstMessageDelayMinutes,
                        SecondMessageDelayMinutes = settings.SecondMessageDelayMinutes,
                        WorkingHoursFrom = settings.WorkingHoursFrom,
                        WorkingHoursTo = settings.WorkingHoursTo,
                        StoreName = storeName,
                        StorefrontBaseUrl = storefrontBaseUrl,
                        RecoveryLink = recoveryLink,
                        CorrelationId = scanCorrelationId
                    };

                    _logger.LogInformation(
                        "[Abandoned Cart Scanner] Publishing TriggerRecoveryEvent | TenantId: {TenantId} | CartId: {CartId} | AbandonedCartId: {AbandonedCartId} | SessionId: {SessionId} | CustomerId: {CustomerId} | ContactChannel: {ContactChannel} | CorrelationId: {CorrelationId}",
                        cart.TenantId,
                        cart.CartId,
                        cart.Id,
                        cart.SessionId,
                        cart.CustomerId,
                        contact.Channel,
                        scanCorrelationId);

                    _telemetry.RecordRecoveryAttempted(cart.TenantId, contact.Channel);

                    await _eventPublisher.PublishAsync(triggerEvent, ct);

                    _logger.LogInformation(
                        "[Abandoned Cart Scanner] TriggerRecoveryEvent published successfully | TenantId: {TenantId} | CartId: {CartId} | CorrelationId: {CorrelationId}",
                        cart.TenantId,
                        cart.CartId,
                        scanCorrelationId);

                    // Mark as abandoned AFTER successful publish to ensure cart remains retryable on transient failures
                    await _cartService.MarkAsAbandonedAsync(cart.Id, ct);

                    triggeredCount++;
                    processedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[Abandoned Cart Scanner] Error processing cart | TenantId: {TenantId} | CartId: {CartId} | SessionId: {SessionId} | CustomerId: {CustomerId} | CorrelationId: {CorrelationId}",
                        cart.TenantId,
                        cart.CartId,
                        cart.SessionId,
                        cart.CustomerId,
                        scanCorrelationId);
                    
                    // Continue with next cart (don't let one failure stop the job)
                }
            }

            if (discoveredCount == 0)
            {
                _logger.LogInformation("[Abandoned Cart Scanner] No carts ready for abandonment");
                return;
            }

            _logger.LogInformation(
                "[Abandoned Cart Scanner] Scan complete | Discovered: {Discovered} | Processed: {Processed} | Triggered: {Triggered} | Skipped: {Skipped} | CorrelationId: {CorrelationId}",
                discoveredCount,
                processedCount,
                triggeredCount,
                skippedCount,
                scanCorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "[Abandoned Cart Scanner] Fatal error during scan | CorrelationId: {CorrelationId}",
                scanCorrelationId);
            // Don't throw - prevents job scheduler crashes
        }
    }

    private static RecoveryContact? DetermineRecoveryContact(AbandonedCart cart)
    {
        if (cart.PhoneVerified && !string.IsNullOrWhiteSpace(cart.PhoneNumber))
        {
            return new RecoveryContact("phone", cart.PhoneNumber);
        }

        if (cart.EmailVerified && !string.IsNullOrWhiteSpace(cart.Email))
        {
            return new RecoveryContact("email", cart.Email);
        }

        return null;
    }

    private async Task<StorefrontContext> ResolveStorefrontContextAsync(Guid tenantId, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var tenant = await dbContext.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(t => new TenantStorefrontIdentity(
                t.Id,
                t.Name,
                t.Slug,
                t.StorefrontBaseUrl))
            .FirstOrDefaultAsync(t => t.Id == tenantId, ct);

        if (tenant is null)
        {
            throw new InvalidOperationException(
                $"Cannot publish abandoned-cart recovery event because tenant {tenantId} was not found.");
        }

        var storeName = await dbContext.StoreConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && !string.IsNullOrWhiteSpace(s.StoreName))
            .Select(s => s.StoreName)
            .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(storeName))
        {
            storeName = tenant.Name;
        }

        if (string.IsNullOrWhiteSpace(storeName))
        {
            throw new InvalidOperationException(
                $"Cannot publish abandoned-cart recovery event because tenant {tenantId} has no store name.");
        }

        return new StorefrontContext(
            storeName,
            ResolveStorefrontBaseUrl(tenant));
    }

    private string ResolveStorefrontBaseUrl(TenantStorefrontIdentity tenant)
    {
        return StorefrontUrlAuthority.ResolveOrigin(
            tenant.StorefrontBaseUrl,
            tenant.Slug,
            _configuration["Storefront:UrlTemplate"]);
    }

    private sealed record StorefrontContext(string StoreName, string StorefrontBaseUrl);
    private sealed record TenantStorefrontIdentity(Guid Id, string Name, string? Slug, string? StorefrontBaseUrl);
    private sealed record RecoveryContact(string Channel, string Value);
}
