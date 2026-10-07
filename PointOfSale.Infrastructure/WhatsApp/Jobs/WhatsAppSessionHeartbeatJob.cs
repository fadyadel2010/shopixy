using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.WhatsApp;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;

namespace PointOfSale.Infrastructure.WhatsApp.Jobs;

/// <summary>
/// Hangfire job that periodically checks the health of all active WhatsApp sessions.
/// Runs every 5 minutes for connected accounts.
/// 
/// Baileys is the sole WhatsApp session authority.
/// </summary>
public class WhatsAppSessionHeartbeatJob
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IEventPublisher _eventPublisher;
    private readonly IWhatsAppTransport _transport;
    private readonly ILogger<WhatsAppSessionHeartbeatJob> _logger;

    public WhatsAppSessionHeartbeatJob(
        ApplicationDbContext dbContext,
        IEventPublisher eventPublisher,
        [FromKeyedServices("baileys")] IWhatsAppTransport transport,
        ILogger<WhatsAppSessionHeartbeatJob> logger)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
        _transport = transport;
        _logger = logger;
    }

    /// <summary>
    /// Main job execution method - called by Hangfire.
    /// </summary>
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[WhatsApp Heartbeat] Starting session health check...");

        // CRITICAL: Must ignore query filters because this job runs without tenant context
        // The global filter (TenantId == CurrentTenantId) would return 0 rows when CurrentTenantId is null
        
        var accounts = await _dbContext.WhatsAppAccounts
            .IgnoreQueryFilters() // Required for cross-tenant background jobs
            .Where(a => a.Enabled)
            .ToListAsync(ct);

        _logger.LogInformation(
            "[WhatsApp Heartbeat] Found {Count} enabled Baileys accounts",
            accounts.Count);

        foreach (var account in accounts)
        {
            await CheckAndUpdateBaileysAccountAsync(account, ct);
        }

        _logger.LogInformation("[WhatsApp Heartbeat] Session health check completed");
    }

    /// <summary>
    /// Check account status using Baileys transport (lightweight HTTP-based)
    /// </summary>
    private async Task CheckAndUpdateBaileysAccountAsync(Core.Entities.WhatsAppAccount account, CancellationToken ct)
    {
        try
        {
            var statusResult = await _transport.GetStatusAsync(account.TenantId.ToString(), ct);

            if (!statusResult.Success || statusResult.Status == WhatsAppConnectionStatus.Unknown)
            {
                _logger.LogWarning(
                    "[WhatsApp Heartbeat] Baileys status unavailable, skipping status update | AccountId: {AccountId} | TenantId: {TenantId}",
                    account.Id,
                    account.TenantId);
                return;
            }
            
            var isConnected = statusResult.Success &&
                              statusResult.Status == WhatsAppConnectionStatus.Connected &&
                              ConnectedPhoneMatches(account.PhoneNumber, statusResult.ConnectedPhone);
            var newStatus = isConnected 
                ? WhatsAppAccountStatus.Connected 
                : statusResult.Status == WhatsAppConnectionStatus.Connected
                    ? WhatsAppAccountStatus.Disconnected
                    : MapBaileysStatusToAccountStatus(statusResult.Status);

            await UpdateAccountStatusIfChangedAsync(account, newStatus, isConnected, "Baileys", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[WhatsApp Heartbeat] Error checking Baileys account | AccountId: {AccountId} | TenantId: {TenantId}",
                account.Id, account.TenantId);
            
            // On Baileys transport error, don't change account status - service might be down temporarily
            _logger.LogWarning(
                "[WhatsApp Heartbeat] Baileys service unreachable, skipping status update | AccountId: {AccountId}",
                account.Id);
        }
    }

    private static bool ConnectedPhoneMatches(string configuredPhone, string? connectedPhone)
    {
        if (string.IsNullOrWhiteSpace(connectedPhone))
        {
            return false;
        }

        static string Digits(string value) => new(value.Where(char.IsDigit).ToArray());
        return string.Equals(Digits(configuredPhone), Digits(connectedPhone), StringComparison.Ordinal);
    }

    /// <summary>
    /// Maps Baileys connection status to WhatsApp account status
    /// </summary>
    private static WhatsAppAccountStatus MapBaileysStatusToAccountStatus(WhatsAppConnectionStatus status)
    {
        return status switch
        {
            WhatsAppConnectionStatus.Connected => WhatsAppAccountStatus.Connected,
            WhatsAppConnectionStatus.QrPending => WhatsAppAccountStatus.PendingQR,
            WhatsAppConnectionStatus.Disconnected => WhatsAppAccountStatus.Disconnected,
            WhatsAppConnectionStatus.Unknown => WhatsAppAccountStatus.Disconnected,
            _ => WhatsAppAccountStatus.Disconnected
        };
    }

    /// <summary>
    /// Update account status in database if changed
    /// </summary>
    private async Task UpdateAccountStatusIfChangedAsync(
        Core.Entities.WhatsAppAccount account, 
        WhatsAppAccountStatus newStatus, 
        bool isConnected,
        string transport,
        CancellationToken ct)
    {
        var oldStatus = account.Status;
        var statusChanged = newStatus != oldStatus;
        var mustDisable = newStatus == WhatsAppAccountStatus.Disconnected && account.Enabled;

        if (statusChanged || mustDisable)
        {
            if (isConnected)
            {
                // Reconnected! Update to Connected
                _logger.LogInformation(
                    "[WhatsApp Heartbeat] Account reconnected via {Transport} | AccountId: {AccountId} | TenantId: {TenantId} | OldStatus: {OldStatus}",
                    transport, account.Id, account.TenantId, oldStatus);
                
                account.Status = WhatsAppAccountStatus.Connected;
                account.LastConnectedAtUtc = DateTime.UtcNow;
                account.UpdatedAtUtc = DateTime.UtcNow;
                account.StatusReason = $"Reconnected via {transport} persistent session";
            }
            else if (newStatus == WhatsAppAccountStatus.Disconnected)
            {
                // Disconnected
                _logger.LogWarning(
                    "[WhatsApp Heartbeat] Account disconnected via {Transport} | AccountId: {AccountId} | TenantId: {TenantId}",
                    transport, account.Id, account.TenantId);

                account.Status = newStatus;
                account.Enabled = false;
                account.LastDisconnectedAtUtc = DateTime.UtcNow;
                account.UpdatedAtUtc = DateTime.UtcNow;
                account.StatusReason = $"Heartbeat check failed ({transport})";
            }
            else
            {
                account.Status = newStatus;
                account.UpdatedAtUtc = DateTime.UtcNow;
                account.StatusReason = $"Awaiting QR scan ({transport})";
            }

            await _dbContext.SaveChangesAsync(ct);

            if (statusChanged)
            {
                await _eventPublisher.PublishAsync(new WhatsAppAccountStatusChangedEvent
                {
                    TenantId = account.TenantId,
                    AccountId = account.Id,
                    OldStatus = oldStatus,
                    NewStatus = account.Status,
                    Reason = account.StatusReason,
                    OccurredAtUtc = DateTime.UtcNow
                }, ct);
            }
        }
    }
}
