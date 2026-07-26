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
/// TRANSPORT-AWARE: Routes to Playwright or Baileys based on tenant's UseBaileys flag.
/// - Playwright tenants: Uses IWhatsAppWebEngine for browser-based session management
/// - Baileys tenants: Uses Baileys HTTP transport for lightweight session management
/// </summary>
public class WhatsAppSessionHeartbeatJob
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWhatsAppWebEngine _webEngine;
    private readonly IWhatsAppSessionManager _sessionManager;
    private readonly IEventPublisher _eventPublisher;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<WhatsAppSessionHeartbeatJob> _logger;

    public WhatsAppSessionHeartbeatJob(
        ApplicationDbContext dbContext,
        IWhatsAppWebEngine webEngine,
        IWhatsAppSessionManager sessionManager,
        IEventPublisher eventPublisher,
        IServiceProvider serviceProvider,
        ILogger<WhatsAppSessionHeartbeatJob> logger)
    {
        _dbContext = dbContext;
        _webEngine = webEngine;
        _sessionManager = sessionManager;
        _eventPublisher = eventPublisher;
        _serviceProvider = serviceProvider;
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
        
        // Load accounts WITH their tenant to check UseBaileys flag
        var allAccounts = await _dbContext.WhatsAppAccounts
            .IgnoreQueryFilters() // Required for cross-tenant background jobs
            .Include(a => a.Tenant) // Include tenant for UseBaileys check
            .Where(a => a.Enabled)
            .ToListAsync(ct);

        // Separate accounts by transport type
        var playwrightAccounts = allAccounts.Where(a => !a.Tenant.UseBaileys).ToList();
        var baileysAccounts = allAccounts.Where(a => a.Tenant.UseBaileys).ToList();

        _logger.LogInformation(
            "[WhatsApp Heartbeat] Found {TotalCount} enabled accounts | Playwright: {PlaywrightCount} | Baileys: {BaileysCount}",
            allAccounts.Count, playwrightAccounts.Count, baileysAccounts.Count);

        // Check Playwright accounts (legacy)
        if (playwrightAccounts.Count > 0)
        {
            _logger.LogInformation("[WhatsApp Heartbeat] Checking {Count} Playwright accounts", playwrightAccounts.Count);
            foreach (var account in playwrightAccounts)
            {
                await CheckAndUpdatePlaywrightAccountAsync(account, ct);
            }
        }

        // Check Baileys accounts (new)
        if (baileysAccounts.Count > 0)
        {
            _logger.LogInformation("[WhatsApp Heartbeat] Checking {Count} Baileys accounts", baileysAccounts.Count);
            foreach (var account in baileysAccounts)
            {
                await CheckAndUpdateBaileysAccountAsync(account, ct);
            }
        }

        _logger.LogInformation("[WhatsApp Heartbeat] Session health check completed");
    }

    /// <summary>
    /// Check account status using Playwright engine (legacy browser-based)
    /// </summary>
    private async Task CheckAndUpdatePlaywrightAccountAsync(Core.Entities.WhatsAppAccount account, CancellationToken ct)
    {
        try
        {
            var status = await _webEngine.CheckConnectionAsync(account.Id, ct);
            var isConnected = status.IsConnected && ConnectedPhoneMatches(account.PhoneNumber, status.ConnectedPhone);
            var newStatus = isConnected
                ? WhatsAppAccountStatus.Connected
                : status.Status == WhatsAppAccountStatus.PendingQR
                    ? WhatsAppAccountStatus.PendingQR
                    : WhatsAppAccountStatus.Disconnected;

            await UpdateAccountStatusIfChangedAsync(account, newStatus, isConnected, "Playwright", ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[WhatsApp Heartbeat] Error checking Playwright account | AccountId: {AccountId}",
                account.Id);
        }
    }

    /// <summary>
    /// Check account status using Baileys transport (lightweight HTTP-based)
    /// </summary>
    private async Task CheckAndUpdateBaileysAccountAsync(Core.Entities.WhatsAppAccount account, CancellationToken ct)
    {
        try
        {
            // Get Baileys transport from keyed services
            var baileysTransport = _serviceProvider.GetRequiredKeyedService<IWhatsAppTransport>("baileys");
            
            // Check status via HTTP call to Baileys service
            var statusResult = await baileysTransport.GetStatusAsync(account.TenantId.ToString(), ct);
            
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
        
        // Status changed?
        if (newStatus != oldStatus)
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
            else
            {
                // Disconnected
                _logger.LogWarning(
                    "[WhatsApp Heartbeat] Account disconnected via {Transport} | AccountId: {AccountId} | TenantId: {TenantId}",
                    transport, account.Id, account.TenantId);

                account.Status = newStatus;
                account.LastDisconnectedAtUtc = DateTime.UtcNow;
                account.UpdatedAtUtc = DateTime.UtcNow;
                account.StatusReason = $"Heartbeat check failed ({transport})";
            }

            await _dbContext.SaveChangesAsync(ct);

            // Publish status change event
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
        else if (isConnected)
        {
            // Still connected - touch session to update last activity
            await _sessionManager.TouchSessionAsync(account.Id, ct);
        }
    }
}
