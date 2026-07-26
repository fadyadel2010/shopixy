using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.WhatsApp;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace PointOfSale.Infrastructure.Consumers.WhatsApp;

/// <summary>
/// Consumes WhatsAppAccountStatusChangedEvent to handle session monitoring and reconnection.
/// </summary>
public class WhatsAppSessionMonitorConsumer : IConsumer<WhatsAppAccountStatusChangedEvent>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<WhatsAppSessionMonitorConsumer> _logger;

    public WhatsAppSessionMonitorConsumer(
        ApplicationDbContext dbContext,
        ILogger<WhatsAppSessionMonitorConsumer> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WhatsAppAccountStatusChangedEvent> context)
    {
        var msg = context.Message;
        
        _logger.LogInformation(
            "WhatsApp Account Status Changed | AccountId: {AccountId} | OldStatus: {OldStatus} | NewStatus: {NewStatus} | Reason: {Reason}",
            msg.AccountId, msg.OldStatus, msg.NewStatus, msg.Reason);

        // CRITICAL: Must ignore query filters because this consumer runs without tenant context
        // The global filter (TenantId == CurrentTenantId) would fail when CurrentTenantId is null
        var account = await _dbContext.WhatsAppAccounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x =>
                x.Id == msg.AccountId &&
                msg.TenantId.HasValue &&
                x.TenantId == msg.TenantId.Value);
        if (account == null)
        {
            _logger.LogWarning("Account {AccountId} not found for status update", msg.AccountId);
            return;
        }

        // Update status
        account.Status = msg.NewStatus;
        account.StatusReason = msg.Reason;
        account.UpdatedAtUtc = DateTime.UtcNow;

        // Update connection timestamps
        if (msg.NewStatus == WhatsAppAccountStatus.Connected)
        {
            account.LastConnectedAtUtc = DateTime.UtcNow;
            account.Enabled = true; // Auto-enable on successful connection
        }
        else if (msg.NewStatus == WhatsAppAccountStatus.Disconnected || 
                 msg.NewStatus == WhatsAppAccountStatus.Banned)
        {
            account.LastDisconnectedAtUtc = DateTime.UtcNow;
            
            // If banned, disable the account
            if (msg.NewStatus == WhatsAppAccountStatus.Banned)
            {
                account.Enabled = false;
            }
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Account status updated in database | AccountId: {AccountId} | NewStatus: {NewStatus}",
            msg.AccountId, msg.NewStatus);
    }
}
