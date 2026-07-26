using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Infrastructure.WhatsApp.Services;

public sealed record WhatsAppAuthorizedDispatch(
    Guid TenantId,
    Guid AccountId,
    string TargetPhone,
    string MessageText);

/// <summary>
/// Rebuilds outbound communication from current SQL authority. Event payloads and Mongo snapshots
/// select work only; they cannot decide customer, order, payment, account, or tenant truth.
/// </summary>
public sealed class WhatsAppDispatchAuthority
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IWhatsAppTemplateResolver _templateResolver;
    private readonly IWhatsAppRateLimitService _consentService;

    public WhatsAppDispatchAuthority(
        ApplicationDbContext dbContext,
        IWhatsAppTemplateResolver templateResolver,
        IWhatsAppRateLimitService consentService)
    {
        _dbContext = dbContext;
        _templateResolver = templateResolver;
        _consentService = consentService;
    }

    public async Task<WhatsAppAuthorizedDispatch?> AuthorizeAsync(
        WhatsAppMessageLogDocument log,
        string? ephemeralMessageText,
        CancellationToken ct)
    {
        if (!Guid.TryParse(log.TenantId, out var tenantId) ||
            !Guid.TryParse(log.AccountId, out var accountId) ||
            !await TenantLineageAuthority.IsActiveTenantAsync(_dbContext, tenantId, ct))
        {
            return null;
        }

        var account = await _dbContext.WhatsAppAccounts
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.Id == accountId &&
                     x.TenantId == tenantId &&
                     x.Enabled &&
                     x.Status == WhatsAppAccountStatus.Connected,
                ct);

        if (account == null || !AccountOwnsMessageType(account, log.MessageType))
        {
            return null;
        }

        return NormalizeMessageType(log.MessageType) switch
        {
            "ORDERCONFIRMATION" => await AuthorizeOrderConfirmationAsync(log, tenantId, accountId, ct),
            "PAYMENTAPPROVED" => await AuthorizePaymentApprovedAsync(log, tenantId, accountId, ct),
            "PAYMENTREJECTED" => await AuthorizePaymentRejectedAsync(log, tenantId, accountId, ct),
            "PAYMENTAPPROVALREQUEST" => null,
            "OTP" => string.IsNullOrWhiteSpace(ephemeralMessageText)
                ? null
                : new WhatsAppAuthorizedDispatch(tenantId, accountId, log.TargetPhone, ephemeralMessageText),
            _ => null
        };
    }

    public async Task<bool> HasMessagingEligibilityAsync(
        Guid tenantId,
        string normalizedPhone,
        string messageType,
        CancellationToken ct)
    {
        if (NormalizeMessageType(messageType) == "OTP")
        {
            return true;
        }

        return await _consentService.CanSendMessageAsync(tenantId, normalizedPhone, ct);
    }

    private async Task<WhatsAppAuthorizedDispatch?> AuthorizeOrderConfirmationAsync(
        WhatsAppMessageLogDocument log,
        Guid tenantId,
        Guid accountId,
        CancellationToken ct)
    {
        if (!Guid.TryParse(log.RelatedEntityId, out var orderId))
        {
            return null;
        }

        var order = await _dbContext.Orders
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(x => x.Items)
            .SingleOrDefaultAsync(
                x => x.Id == orderId &&
                     x.TenantId == tenantId &&
                     !x.IsDeleted &&
                     x.Status != OrderStatus.Cancelled &&
                     x.Status != OrderStatus.Refunded,
                ct);

        if (order == null || string.IsNullOrWhiteSpace(order.GuestPhone))
        {
            return null;
        }

        var tenantName = await _dbContext.Tenants
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(x => x.Id == tenantId)
            .Select(x => x.Name)
            .SingleAsync(ct);

        var text = await _templateResolver.ResolveOrderConfirmationAsync(
            tenantId,
            new OrderConfirmationData
            {
                OrderNumber = order.OrderNumber,
                CustomerName = order.GuestName ?? "Customer",
                Total = order.Total,
                Currency = order.Currency,
                ItemsCount = order.Items.Count,
                StoreName = tenantName
            },
            ct: ct);

        return new WhatsAppAuthorizedDispatch(tenantId, accountId, order.GuestPhone, text);
    }

    private async Task<WhatsAppAuthorizedDispatch?> AuthorizePaymentApprovedAsync(
        WhatsAppMessageLogDocument log,
        Guid tenantId,
        Guid accountId,
        CancellationToken ct)
    {
        var authority = await LoadPaymentAuthorityAsync(log, tenantId, ct);
        if (authority == null ||
            authority.IntentStatus != PaymentIntentStatus.Paid ||
            authority.OrderPaymentStatus is not (PaymentStatus.Collected or PaymentStatus.Settled or PaymentStatus.PartiallyRefunded))
        {
            return null;
        }

        var text = $"Payment approved\n\nOrder #{authority.OrderNumber}\n\n" +
                   "Your payment has been confirmed. Your order is being prepared for shipping.";
        return new WhatsAppAuthorizedDispatch(tenantId, accountId, authority.Phone, text);
    }

    private async Task<WhatsAppAuthorizedDispatch?> AuthorizePaymentRejectedAsync(
        WhatsAppMessageLogDocument log,
        Guid tenantId,
        Guid accountId,
        CancellationToken ct)
    {
        var authority = await LoadPaymentAuthorityAsync(log, tenantId, ct);
        if (authority == null ||
            authority.IntentStatus != PaymentIntentStatus.Rejected ||
            authority.OrderPaymentStatus != PaymentStatus.Rejected)
        {
            return null;
        }

        var reason = string.IsNullOrWhiteSpace(authority.RejectionReason)
            ? "Payment proof was not accepted."
            : authority.RejectionReason;
        var text = $"Payment rejected\n\nOrder #{authority.OrderNumber}\n\nReason: {reason}";
        return new WhatsAppAuthorizedDispatch(tenantId, accountId, authority.Phone, text);
    }

    private async Task<PaymentNotificationAuthority?> LoadPaymentAuthorityAsync(
        WhatsAppMessageLogDocument log,
        Guid tenantId,
        CancellationToken ct)
    {
        if (!Guid.TryParse(log.RelatedEntityId, out var paymentIntentId))
        {
            return null;
        }

        return await (
                from intent in _dbContext.PaymentIntents.AsNoTracking().IgnoreQueryFilters()
                join order in _dbContext.Orders.AsNoTracking().IgnoreQueryFilters()
                    on new { intent.OrderId, intent.TenantId } equals new { OrderId = order.Id, order.TenantId }
                where intent.Id == paymentIntentId &&
                      intent.TenantId == tenantId &&
                      !intent.IsDeleted &&
                      !order.IsDeleted &&
                      order.Status != OrderStatus.Cancelled &&
                      order.Status != OrderStatus.Refunded &&
                      order.GuestPhone != null &&
                      order.GuestPhone != ""
                select new PaymentNotificationAuthority(
                    order.OrderNumber,
                    order.GuestPhone!,
                    order.PaymentStatus,
                    intent.Status,
                    intent.RejectionReason))
            .SingleOrDefaultAsync(ct);
    }

    private static bool AccountOwnsMessageType(Core.Entities.WhatsAppAccount account, string messageType) =>
        NormalizeMessageType(messageType) switch
        {
            "ORDERCONFIRMATION" => account.UseForOrderConfirmation,
            "OTP" => account.UseForOtpVerification,
            "PAYMENTAPPROVALREQUEST" or "PAYMENTAPPROVED" or "PAYMENTREJECTED" => account.UseForPaymentApproval,
            _ => false
        };

    private static string NormalizeMessageType(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private sealed record PaymentNotificationAuthority(
        string OrderNumber,
        string Phone,
        PaymentStatus OrderPaymentStatus,
        PaymentIntentStatus IntentStatus,
        string? RejectionReason);
}
