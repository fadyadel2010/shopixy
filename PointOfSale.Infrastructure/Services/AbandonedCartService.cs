using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.DTOs.AbandonedCart;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Services;

/// <summary>
/// Implementation of abandoned cart service.
/// Phase: Smart Abandoned Cart Recovery Engine - Hardening
/// 
/// HARDENING CHANGES:
/// 1. Atomic upsert using PostgreSQL ON CONFLICT
/// 2. Safe state transitions using SQL UPDATE ... WHERE status = X
/// 3. Pending state for native dispatch claim protection
/// 4. Retry tracking and failure handling
/// 5. Improved logging with structured fields
/// </summary>
public class AbandonedCartService : IAbandonedCartService
{
    private const string RecoveryPurpose = "abandoned-cart-recovery";
    private const string ProtectorPurpose = "AbandonedCartRecoveryTokens.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _context;
    private readonly ITenantAutomationSettingsService _settingsService;
    private readonly IDataProtector _protector;
    private readonly ILogger<AbandonedCartService> _logger;
    
    private const int RecoveryTokenValidityDays = 7;
    private const int PendingTimeoutMinutes = 5; // Maximum time a cart can be in Pending state
    private const int TenantSettingsBatchSize = 100;
    private const int ReadyCartBatchSize = 100;
    private const int StuckPendingBatchSize = 100;

    public AbandonedCartService(
        ApplicationDbContext context,
        ITenantAutomationSettingsService settingsService,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<AbandonedCartService> logger)
    {
        _context = context;
        _settingsService = settingsService;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _logger = logger;
    }

    /// <summary>
    /// HARDENING: Atomic upsert using PostgreSQL ON CONFLICT.
    /// Eliminates race condition between check and insert.
    /// </summary>
    public async Task UpsertCartActivityAsync(
        UpsertCartActivityRequest request,
        CancellationToken ct = default)
    {
        // Use raw SQL for atomic upsert with PostgreSQL ON CONFLICT
        var sql = @"
            INSERT INTO ""AbandonedCarts"" (
                ""Id"", ""TenantId"", ""CartId"", ""CustomerId"", 
                ""SessionId"", ""PhoneNumber"", ""Email"", ""ContactChannel"",
                ""PhoneVerified"", ""EmailVerified"", ""CartSnapshotJson"",
                ""LastActivityAtUtc"", ""RecoveryStatus"", 
                ""CreatedAtUtc"", ""UpdatedAtUtc"", ""RetryCount""
            ) VALUES (
                @p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10::jsonb, @p11, @p12, @p13, @p14, 0
            )
            ON CONFLICT (""TenantId"", ""CartId"") DO UPDATE SET
                ""CustomerId"" = COALESCE(@p3, ""AbandonedCarts"".""CustomerId""),
                ""SessionId"" = COALESCE(NULLIF(@p4, ''), ""AbandonedCarts"".""SessionId""),
                ""PhoneNumber"" = COALESCE(NULLIF(@p5, ''), ""AbandonedCarts"".""PhoneNumber""),
                ""Email"" = COALESCE(NULLIF(@p6, ''), ""AbandonedCarts"".""Email""),
                ""ContactChannel"" = COALESCE(NULLIF(@p7, ''), ""AbandonedCarts"".""ContactChannel""),
                -- Smart verified flag preservation: prevents accidental un-verification
                -- - When incoming phone/email equals existing value: preserves existing verified status
                -- - When incoming phone/email differs: applies incoming verified flag
                -- - When incoming phone/email is empty: preserves existing verified status
                -- Note: This prevents marking an unchanged phone/email as unverified via this upsert.
                -- An explicit admin endpoint would be required to support that edge case.
                ""PhoneVerified"" = CASE 
                    WHEN NULLIF(@p5, '') IS NOT NULL THEN 
                        CASE 
                            WHEN NULLIF(@p5, '') = ""AbandonedCarts"".""PhoneNumber"" THEN ""AbandonedCarts"".""PhoneVerified""
                            ELSE @p8
                        END
                    ELSE ""AbandonedCarts"".""PhoneVerified"" 
                END,
                ""EmailVerified"" = CASE 
                    WHEN NULLIF(@p6, '') IS NOT NULL THEN 
                        CASE 
                            WHEN NULLIF(@p6, '') = ""AbandonedCarts"".""Email"" THEN ""AbandonedCarts"".""EmailVerified""
                            ELSE @p9
                        END
                    ELSE ""AbandonedCarts"".""EmailVerified"" 
                END,
                ""CartSnapshotJson"" = @p10::jsonb,
                ""LastActivityAtUtc"" = @p11,
                ""UpdatedAtUtc"" = @p14,
                -- Preserve Triggered so a later order remains attributable to the sent recovery.
                ""RecoveryStatus"" = CASE 
                    WHEN ""AbandonedCarts"".""RecoveryStatus"" IN ('Abandoned', 'Pending')
                    THEN 'Active'
                    ELSE ""AbandonedCarts"".""RecoveryStatus""
                END,
                ""AbandonedAtUtc"" = CASE 
                    WHEN ""AbandonedCarts"".""RecoveryStatus"" IN ('Abandoned', 'Pending')
                    THEN NULL
                    ELSE ""AbandonedCarts"".""AbandonedAtUtc""
                END,
                ""TriggeredAtUtc"" = CASE 
                    WHEN ""AbandonedCarts"".""RecoveryStatus"" IN ('Abandoned', 'Pending')
                    THEN NULL
                    ELSE ""AbandonedCarts"".""TriggeredAtUtc""
                END,
                ""PendingAtUtc"" = CASE 
                    WHEN ""AbandonedCarts"".""RecoveryStatus"" IN ('Abandoned', 'Pending')
                    THEN NULL
                    ELSE ""AbandonedCarts"".""PendingAtUtc""
                END,
                ""RetryCount"" = CASE 
                    WHEN ""AbandonedCarts"".""RecoveryStatus"" IN ('Abandoned', 'Pending')
                    THEN 0
                    ELSE ""AbandonedCarts"".""RetryCount""
                END";

        var now = DateTime.UtcNow;
        var newId = Guid.NewGuid();
        var statusActive = AbandonedCartRecoveryStatus.Active.ToString();

        await _context.Database.ExecuteSqlRawAsync(
            sql,
            newId, request.TenantId, request.CartId, request.CustomerId,
            request.SessionId ?? string.Empty,
            request.PhoneNumber ?? string.Empty,
            request.Email ?? string.Empty,
            request.ContactChannel ?? string.Empty,
            request.PhoneVerified,
            request.EmailVerified,
            request.CartSnapshotJson,
            request.OccurredAtUtc, statusActive, now, now);

        _logger.LogDebug(
            "[Abandoned Cart] Upsert cart activity | TenantId: {TenantId} | CartId: {CartId}",
            request.TenantId, request.CartId);
    }

    public async IAsyncEnumerable<AbandonedCart> FindCartsReadyForAbandonmentAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var foundEnabledTenants = false;
        Guid? lastTenantId = null;

        while (true)
        {
            var tenantQuery = _context.Set<TenantAutomationSettings>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s => s.EnableAbandonedRecovery);

            if (lastTenantId.HasValue)
            {
                tenantQuery = tenantQuery.Where(s => s.TenantId > lastTenantId.Value);
            }

            var enabledTenantBatch = await tenantQuery
                .OrderBy(s => s.TenantId)
                .Select(s => new AbandonedCartTenantScanSettings
                {
                    TenantId = s.TenantId,
                    FirstMessageDelayMinutes = s.FirstMessageDelayMinutes
                })
                .Take(TenantSettingsBatchSize)
                .ToListAsync(ct);

            if (enabledTenantBatch.Count == 0)
            {
                break;
            }

            foundEnabledTenants = true;

            foreach (var settings in enabledTenantBatch)
            {
                var threshold = DateTime.UtcNow.AddMinutes(-settings.FirstMessageDelayMinutes);

                _logger.LogInformation(
                    "[Abandoned Cart] Scanning tenant | TenantId: {TenantId} | Threshold: {Threshold} | DelayMinutes: {DelayMinutes}",
                    settings.TenantId, threshold, settings.FirstMessageDelayMinutes);

                var readyCartCount = 0;
                Guid? lastCartId = null;

                while (true)
                {
                    var readyCartQuery = _context.Set<AbandonedCart>()
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .Where(c => c.TenantId == settings.TenantId
                            && ((c.RecoveryStatus == AbandonedCartRecoveryStatus.Active
                                 && c.LastActivityAtUtc <= threshold) ||
                                (c.RecoveryStatus == AbandonedCartRecoveryStatus.Abandoned
                                 && c.RetryCount < AbandonedCart.MaxRetryCount
                                 && c.PhoneVerified
                                 && c.PhoneNumber != "")));

                    if (lastCartId.HasValue)
                    {
                        readyCartQuery = readyCartQuery.Where(c => c.Id > lastCartId.Value);
                    }

                    var readyCartBatch = await readyCartQuery
                        .OrderBy(c => c.Id)
                        .Take(ReadyCartBatchSize)
                        .ToListAsync(ct);

                    if (readyCartBatch.Count == 0)
                    {
                        break;
                    }

                    readyCartCount += readyCartBatch.Count;
                    lastCartId = readyCartBatch[^1].Id;

                    foreach (var cart in readyCartBatch)
                    {
                        yield return cart;
                    }
                }

                if (readyCartCount > 0)
                {
                    _logger.LogInformation(
                        "[Abandoned Cart] Found {Count} carts ready for abandonment | TenantId: {TenantId}",
                        readyCartCount, settings.TenantId);
                }
            }

            lastTenantId = enabledTenantBatch[^1].TenantId;
        }

        if (!foundEnabledTenants)
        {
            _logger.LogInformation("[Abandoned Cart] No tenants with automation enabled");
            yield break;
        }

        await foreach (var cart in ResetAndStreamStuckPendingCartsAsync(ct))
        {
            yield return cart;
        }
    }

    private async IAsyncEnumerable<AbandonedCart> ResetAndStreamStuckPendingCartsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var pendingThreshold = DateTime.UtcNow.AddMinutes(-PendingTimeoutMinutes);

        while (true)
        {
            var stuckPendingBatch = await _context.Set<AbandonedCart>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.RecoveryStatus == AbandonedCartRecoveryStatus.Pending
                    && c.PendingAtUtc <= pendingThreshold)
                .OrderBy(c => c.PendingAtUtc)
                .ThenBy(c => c.Id)
                .Take(StuckPendingBatchSize)
                .ToListAsync(ct);

            if (stuckPendingBatch.Count == 0)
            {
                yield break;
            }

            _logger.LogWarning(
                "[Abandoned Cart] Found {Count} stuck pending carts, resetting to Abandoned",
                stuckPendingBatch.Count);

            var stuckPendingIds = stuckPendingBatch
                .Select(c => c.Id)
                .ToList();
            var updatedAtUtc = DateTime.UtcNow;

            var rowsAffected = await _context.Set<AbandonedCart>()
                .IgnoreQueryFilters()
                .Where(c => stuckPendingIds.Contains(c.Id)
                    && c.RecoveryStatus == AbandonedCartRecoveryStatus.Pending
                    && c.PendingAtUtc <= pendingThreshold)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(c => c.RecoveryStatus, AbandonedCartRecoveryStatus.Abandoned)
                        .SetProperty(c => c.PendingAtUtc, (DateTime?)null)
                        .SetProperty(c => c.UpdatedAtUtc, updatedAtUtc),
                    ct);

            if (rowsAffected == 0)
            {
                // Skip this batch if it was raced, but continue processing other batches
                continue;
            }

            // Re-query to get only the carts that were actually updated
            var actuallyUpdatedCarts = await _context.Set<AbandonedCart>()
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => stuckPendingIds.Contains(c.Id)
                    && c.RecoveryStatus == AbandonedCartRecoveryStatus.Abandoned
                    && c.UpdatedAtUtc == updatedAtUtc)
                .ToListAsync(ct);

            foreach (var cart in actuallyUpdatedCarts)
            {
                yield return cart;
            }
        }
    }

    public async Task<bool> TryMarkAsAbandonedAsync(
        Guid abandonedCartId,
        DateTime expectedLastActivityAtUtc,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var rowsAffected = await _context.Set<AbandonedCart>()
            .IgnoreQueryFilters()
            .Where(cart => cart.Id == abandonedCartId &&
                           cart.RecoveryStatus == AbandonedCartRecoveryStatus.Active &&
                           cart.LastActivityAtUtc == expectedLastActivityAtUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(cart => cart.RecoveryStatus, AbandonedCartRecoveryStatus.Abandoned)
                    .SetProperty(cart => cart.AbandonedAtUtc, now)
                    .SetProperty(cart => cart.UpdatedAtUtc, now),
                ct);

        return rowsAffected == 1;
    }

    /// <summary>
    /// HARDENING: Transition to Pending state before native dispatch.
    /// Returns false if cart is not in Abandoned state (preventing duplicate triggers).
    /// </summary>
    public async Task<bool> TryMarkAsPendingAsync(Guid abandonedCartId, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[Abandoned Cart] TryMarkAsPendingAsync called | Id: {Id}",
            abandonedCartId);

        // HARDENING: Use atomic UPDATE with WHERE clause for safe state transition
        var sql = @"
            UPDATE ""AbandonedCarts""
            SET ""RecoveryStatus"" = 'Pending',
                ""PendingAtUtc"" = @p1,
                ""UpdatedAtUtc"" = @p1
            WHERE ""Id"" = @p0 
              AND ""RecoveryStatus"" = 'Abandoned'";

        var now = DateTime.UtcNow;
        var rowsAffected = await _context.Database.ExecuteSqlRawAsync(sql, abandonedCartId, now);

        if (rowsAffected == 1)
        {
            _logger.LogInformation(
                "[Abandoned Cart] Marked as pending | Id: {Id}",
                abandonedCartId);
            return true;
        }
        else
        {
            // Log more details about why it failed
            var currentStatus = await _context.Set<AbandonedCart>()
                .IgnoreQueryFilters()
                .Where(c => c.Id == abandonedCartId)
                .Select(c => c.RecoveryStatus.ToString())
                .FirstOrDefaultAsync(ct);

            _logger.LogWarning(
                "[Abandoned Cart] Failed to mark as pending | Id: {Id} | CurrentStatus: {CurrentStatus} | Expected: Abandoned",
                abandonedCartId, currentStatus ?? "NOT_FOUND");
            return false;
        }
    }

    /// <summary>
    /// HARDENING: Confirm successful native dispatch.
    /// Transitions from Pending to Triggered.
    /// </summary>
    public async Task<bool> ConfirmTriggerSuccessAsync(Guid abandonedCartId, CancellationToken ct = default)
    {
        var sql = @"
            UPDATE ""AbandonedCarts""
            SET ""RecoveryStatus"" = 'Triggered',
                ""TriggeredAtUtc"" = @p1,
                ""PendingAtUtc"" = NULL,
                ""RetryCount"" = 0,
                ""LastErrorMessage"" = NULL,
                ""UpdatedAtUtc"" = @p1
            WHERE ""Id"" = @p0 
              AND ""RecoveryStatus"" = 'Pending'";

        var now = DateTime.UtcNow;
        var rowsAffected = await _context.Database.ExecuteSqlRawAsync(sql, abandonedCartId, now);

        if (rowsAffected == 1)
        {
            _logger.LogInformation(
                "[Abandoned Cart] Trigger confirmed | Id: {Id}",
                abandonedCartId);
            return true;
        }
        else
        {
            _logger.LogWarning(
                "[Abandoned Cart] Failed to confirm trigger (not in Pending state) | Id: {Id}",
                abandonedCartId);
            return false;
        }
    }

    /// <summary>
    /// HARDENING: Handle native dispatch failure without overwriting a concurrent
    /// completed-order transition.
    /// </summary>
    public async Task RecordTriggerFailureAsync(Guid abandonedCartId, string errorMessage, CancellationToken ct = default)
    {
        var normalizedError = errorMessage.Length > 2000 ? errorMessage[..2000] : errorMessage;
        var now = DateTime.UtcNow;
        var rowsAffected = await _context.Database.ExecuteSqlRawAsync(
            @"UPDATE ""AbandonedCarts""
              SET ""RetryCount"" = ""RetryCount"" + 1,
                  ""LastErrorMessage"" = @p1,
                  ""RecoveryStatus"" = CASE
                      WHEN ""RetryCount"" + 1 >= @p3 THEN 'Failed'
                      ELSE 'Abandoned'
                  END,
                  ""PendingAtUtc"" = NULL,
                  ""UpdatedAtUtc"" = @p2
              WHERE ""Id"" = @p0
                AND ""RecoveryStatus"" IN ('Abandoned', 'Pending')",
            abandonedCartId,
            normalizedError,
            now,
            AbandonedCart.MaxRetryCount);

        if (rowsAffected == 1)
        {
            _logger.LogWarning(
                "[Abandoned Cart] Native dispatch failed and retry state was updated | Id: {Id} | Error: {Error}",
                abandonedCartId,
                normalizedError);
        }
    }

    public async Task ReleasePendingAsync(Guid abandonedCartId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        await _context.Set<AbandonedCart>()
            .IgnoreQueryFilters()
            .Where(cart => cart.Id == abandonedCartId && cart.RecoveryStatus == AbandonedCartRecoveryStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(cart => cart.RecoveryStatus, AbandonedCartRecoveryStatus.Abandoned)
                .SetProperty(cart => cart.PendingAtUtc, (DateTime?)null)
                .SetProperty(cart => cart.UpdatedAtUtc, now), ct);
    }

    public async Task<bool> MarkAsTriggeredAsync(Guid abandonedCartId, CancellationToken ct = default)
    {
        // DEPRECATED: Use TryMarkAsPendingAsync + ConfirmTriggerSuccessAsync instead
        // Kept for backward compatibility
        // CRITICAL: IgnoreQueryFilters() because this might be called from background jobs
        var cart = await _context.Set<AbandonedCart>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == abandonedCartId, ct);

        if (cart == null)
        {
            _logger.LogWarning(
                "[Abandoned Cart] Cart not found for marking as triggered | Id: {Id}",
                abandonedCartId);
            return false;
        }

        if (cart.RecoveryStatus == AbandonedCartRecoveryStatus.Triggered)
        {
            // Already triggered, prevent duplicate
            _logger.LogDebug(
                "[Abandoned Cart] Cart already triggered, skipping | TenantId: {TenantId} | CartId: {CartId}",
                cart.TenantId, cart.CartId);
            return false;
        }

        cart.RecoveryStatus = AbandonedCartRecoveryStatus.Triggered;
        cart.TriggeredAtUtc = DateTime.UtcNow;
        cart.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[Abandoned Cart] Marked as triggered | TenantId: {TenantId} | CartId: {CartId}",
            cart.TenantId, cart.CartId);

        return true;
    }

    public async Task MarkAsRecoveredAsync(Guid tenantId, Guid cartId, Guid orderId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || cartId == Guid.Empty || orderId == Guid.Empty)
        {
            _logger.LogWarning(
                "[Abandoned Cart] Rejected recovered transition with invalid authority input | TenantId: {TenantId} | CartId: {CartId} | OrderId: {OrderId}",
                tenantId, cartId, orderId);
            return;
        }

        // A completed order always closes future work. Attribution additionally requires
        // the canonical Ecommerce order to have been created after the successful send.
        var sql = @"
            UPDATE ""AbandonedCarts""
            SET ""RecoveryStatus"" = CASE
                    WHEN ""RecoveryStatus"" = 'Triggered'
                         AND ""TriggeredAtUtc"" IS NOT NULL
                         AND EXISTS (
                             SELECT 1
                             FROM ""Orders"" AS canonical_order
                             WHERE canonical_order.""Id"" = @p2
                               AND canonical_order.""TenantId"" = @p0
                               AND canonical_order.""CartId"" = @p1
                               AND canonical_order.""Source"" = 0
                               AND canonical_order.""IsDeleted"" = FALSE
                                AND canonical_order.""CreatedAt"" >= ""AbandonedCarts"".""TriggeredAtUtc""
                         ) THEN 'Recovered'
                    ELSE 'Canceled'
                END,
                ""RecoveredAtUtc"" = CASE
                    WHEN ""RecoveryStatus"" = 'Triggered'
                         AND ""TriggeredAtUtc"" IS NOT NULL
                         AND EXISTS (
                             SELECT 1
                             FROM ""Orders"" AS canonical_order
                             WHERE canonical_order.""Id"" = @p2
                               AND canonical_order.""TenantId"" = @p0
                               AND canonical_order.""CartId"" = @p1
                               AND canonical_order.""Source"" = 0
                               AND canonical_order.""IsDeleted"" = FALSE
                                AND canonical_order.""CreatedAt"" >= ""AbandonedCarts"".""TriggeredAtUtc""
                         ) THEN @p3
                    ELSE NULL
                END,
                ""RecoveredOrderId"" = @p2,
                ""UpdatedAtUtc"" = @p3
            WHERE ""TenantId"" = @p0
              AND ""CartId"" = @p1
              AND ""RecoveryStatus"" NOT IN ('Recovered', 'Expired', 'Failed', 'Canceled')";

        var now = DateTime.UtcNow;
        var rowsAffected = await _context.Database.ExecuteSqlRawAsync(sql, tenantId, cartId, orderId, now);

        if (rowsAffected > 0)
        {
            _logger.LogInformation(
                "[Abandoned Cart] Marked as recovered | TenantId: {TenantId} | CartId: {CartId} | OrderId: {OrderId}",
                tenantId, cartId, orderId);
        }
        else
        {
            _logger.LogDebug(
                "[Abandoned Cart] Cart not found, cross-tenant, or already terminated | TenantId: {TenantId} | CartId: {CartId}",
                tenantId, cartId);
        }
    }

    /// <summary>
    /// HARDENING: Mark expired carts.
    /// Called by cleanup job to expire carts past recovery window.
    /// </summary>
    public async Task<int> ExpireOldTriggeredCartsAsync(int expirationDays, CancellationToken ct = default)
    {
        var threshold = DateTime.UtcNow.AddDays(-expirationDays);

        var sql = @"
            UPDATE ""AbandonedCarts""
            SET ""RecoveryStatus"" = 'Expired',
                ""ExpiredAtUtc"" = @p1,
                ""UpdatedAtUtc"" = @p1
            WHERE ""RecoveryStatus"" = 'Triggered'
              AND ""TriggeredAtUtc"" < @p0";

        var now = DateTime.UtcNow;
        var rowsAffected = await _context.Database.ExecuteSqlRawAsync(sql, threshold, now);

        if (rowsAffected > 0)
        {
            _logger.LogInformation(
                "[Abandoned Cart] Expired {Count} old triggered carts",
                rowsAffected);
        }

        return rowsAffected;
    }

    public string GenerateRecoveryToken(AbandonedCart cart)
    {
        ArgumentNullException.ThrowIfNull(cart);

        if (cart.TenantId == Guid.Empty)
        {
            throw new InvalidOperationException("Abandoned-cart recovery token generation requires a tenant ID.");
        }

        if (cart.CartId == Guid.Empty)
        {
            throw new InvalidOperationException("Abandoned-cart recovery token generation requires a cart ID.");
        }

        var normalizedSessionId = NormalizeOptional(cart.SessionId);
        if (normalizedSessionId == null && cart.CustomerId is null)
        {
            throw new InvalidOperationException(
                $"Abandoned-cart recovery token generation requires session or customer ownership | TenantId: {cart.TenantId:D} | CartId: {cart.CartId:D}");
        }

        var payload = new RecoveryTokenPayload
        {
            TenantId = cart.TenantId,
            CartId = cart.CartId,
            SessionId = normalizedSessionId,
            CustomerId = cart.CustomerId,
            Purpose = RecoveryPurpose,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(RecoveryTokenValidityDays)
        };

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var protectedPayload = _protector.Protect(json);
        return EncodeBase64Url(Encoding.UTF8.GetBytes(protectedPayload));
    }

    public AbandonedCartRecoveryTokenValidationResult ValidateRecoveryToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return AbandonedCartRecoveryTokenValidationResult.Invalid("missing_token");
        }

        try
        {
            var protectedBytes = DecodeBase64Url(token);
            var protectedPayload = Encoding.UTF8.GetString(protectedBytes);
            var json = _protector.Unprotect(protectedPayload);
            var payload = JsonSerializer.Deserialize<RecoveryTokenPayload>(json, JsonOptions);

            if (payload == null)
            {
                return AbandonedCartRecoveryTokenValidationResult.Invalid("payload_missing");
            }

            var normalizedSessionId = NormalizeOptional(payload.SessionId);

            if (!string.Equals(payload.Purpose, RecoveryPurpose, StringComparison.Ordinal))
            {
                return AbandonedCartRecoveryTokenValidationResult.Invalid(
                    "invalid_purpose",
                    payload.TenantId,
                    payload.CartId,
                    normalizedSessionId,
                    payload.CustomerId,
                    payload.ExpiresAtUtc);
            }

            if (payload.TenantId == Guid.Empty || payload.CartId == Guid.Empty)
            {
                return AbandonedCartRecoveryTokenValidationResult.Invalid(
                    "invalid_subject",
                    payload.TenantId,
                    payload.CartId,
                    normalizedSessionId,
                    payload.CustomerId,
                    payload.ExpiresAtUtc);
            }

            if (normalizedSessionId == null && payload.CustomerId is not Guid)
            {
                return AbandonedCartRecoveryTokenValidationResult.Invalid(
                    "missing_owner",
                    payload.TenantId,
                    payload.CartId,
                    normalizedSessionId,
                    payload.CustomerId,
                    payload.ExpiresAtUtc);
            }

            if (payload.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                return AbandonedCartRecoveryTokenValidationResult.Invalid(
                    "expired",
                    payload.TenantId,
                    payload.CartId,
                    normalizedSessionId,
                    payload.CustomerId,
                    payload.ExpiresAtUtc);
            }

            return AbandonedCartRecoveryTokenValidationResult.Valid(
                payload.TenantId,
                payload.CartId,
                normalizedSessionId,
                payload.CustomerId,
                payload.ExpiresAtUtc);
        }
        catch (CryptographicException)
        {
            return AbandonedCartRecoveryTokenValidationResult.SignatureFailure("signature_validation_failed");
        }
        catch (FormatException)
        {
            return AbandonedCartRecoveryTokenValidationResult.Invalid("malformed_token");
        }
        catch (JsonException)
        {
            return AbandonedCartRecoveryTokenValidationResult.Invalid("malformed_payload");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Abandoned Cart] Unexpected recovery token validation failure");
            return AbandonedCartRecoveryTokenValidationResult.Invalid("unexpected_validation_error");
        }
    }

    public async Task<AbandonedCart?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        // CRITICAL: IgnoreQueryFilters() because this might be called from background jobs
        return await _context.Set<AbandonedCart>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<AbandonedCart?> GetByCartIdAsync(Guid tenantId, Guid cartId, CancellationToken ct = default)
    {
        // CRITICAL: IgnoreQueryFilters() because this might be called from MassTransit consumers
        return await _context.Set<AbandonedCart>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.CartId == cartId, ct);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EncodeBase64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value
            .Replace('-', '+')
            .Replace('_', '/');

        var padding = normalized.Length % 4;
        if (padding > 0)
        {
            normalized = normalized.PadRight(normalized.Length + (4 - padding), '=');
        }

        var decoded = Convert.FromBase64String(normalized);
        if (!string.Equals(EncodeBase64Url(decoded), value, StringComparison.Ordinal))
        {
            throw new FormatException("Recovery token is not canonical Base64URL.");
        }

        return decoded;
    }

    private class RecoveryTokenPayload
    {
        public Guid TenantId { get; set; }
        public Guid CartId { get; set; }
        public string? SessionId { get; set; }
        public Guid? CustomerId { get; set; }
        public string Purpose { get; set; } = string.Empty;
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }
}

internal sealed class AbandonedCartTenantScanSettings
{
    public Guid TenantId { get; init; }
    public int FirstMessageDelayMinutes { get; init; }
}
