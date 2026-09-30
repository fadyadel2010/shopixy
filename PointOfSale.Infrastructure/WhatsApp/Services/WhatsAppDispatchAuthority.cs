using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.Reliability;
using Microsoft.Extensions.Configuration;
using PointOfSale.Core.Tenancy;

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
    private readonly ITenantAutomationSettingsService _settingsService;
    private readonly IAbandonedCartService _cartService;
    private readonly IConfiguration _configuration;

    public WhatsAppDispatchAuthority(
        ApplicationDbContext dbContext,
        IWhatsAppTemplateResolver templateResolver,
        IWhatsAppRateLimitService consentService,
        ITenantAutomationSettingsService settingsService,
        IAbandonedCartService cartService,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _templateResolver = templateResolver;
        _consentService = consentService;
        _settingsService = settingsService;
        _cartService = cartService;
        _configuration = configuration;
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
            "ABANDONEDCARTRECOVERYFIRST" => await AuthorizeRecoveryAsync(log, tenantId, accountId, 1, ct),
            "ABANDONEDCARTRECOVERYSECOND" => await AuthorizeRecoveryAsync(log, tenantId, accountId, 2, ct),
            "OTP" => string.IsNullOrWhiteSpace(ephemeralMessageText)
                ? null
                : new WhatsAppAuthorizedDispatch(tenantId, accountId, log.TargetPhone, ephemeralMessageText),
            _ => null
        };
    }

    private async Task<WhatsAppAuthorizedDispatch?> AuthorizeRecoveryAsync(
        WhatsAppMessageLogDocument log,
        Guid tenantId,
        Guid accountId,
        int sequence,
        CancellationToken ct)
    {
        if (!Guid.TryParse(log.RelatedEntityId, out var abandonedCartId))
        {
            return null;
        }

        var settings = await _settingsService.GetSettingsAsync(tenantId, ct);
        if (!settings.EnableAbandonedRecovery)
        {
            return null;
        }

        var cart = await _dbContext.AbandonedCarts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == abandonedCartId &&
                        item.TenantId == tenantId &&
                        item.PhoneVerified &&
                        item.PhoneNumber != "" &&
                        item.RecoveredOrderId == null,
                ct);
        if (cart == null)
        {
            return null;
        }

        var canonicalOrderExists = await _dbContext.Orders
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(order =>
                order.TenantId == tenantId &&
                order.CartId == cart.CartId &&
                order.Source == OrderSource.Ecommerce &&
                !order.IsDeleted,
                ct);
        if (canonicalOrderExists)
        {
            return null;
        }

        var schedule = await _dbContext.AbandonedCartRecoveryLogs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.AbandonedCartId == cart.Id)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (schedule == null ||
            (sequence == 1 && cart.RecoveryStatus is not (AbandonedCartRecoveryStatus.Abandoned or AbandonedCartRecoveryStatus.Pending)) ||
            (sequence == 2 &&
             (cart.RecoveryStatus != AbandonedCartRecoveryStatus.Triggered ||
              !schedule.FirstMessageSentAtUtc.HasValue ||
              cart.LastActivityAtUtc > schedule.FirstMessageSentAtUtc.Value)))
        {
            return null;
        }

        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.Id == tenantId)
            .Select(item => new { item.Name, item.Slug, item.StorefrontBaseUrl, item.Country })
            .SingleAsync(ct);
        var storeName = await _dbContext.StoreConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.StoreName != "")
            .Select(item => item.StoreName)
            .FirstOrDefaultAsync(ct) ?? tenant.Name;
        var customerName = cart.CustomerId.HasValue
            ? await _dbContext.OnlineCustomers
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(customer => customer.Id == cart.CustomerId && customer.TenantId == tenantId)
                .Select(customer => customer.DisplayName)
                .SingleOrDefaultAsync(ct)
            : null;
        var origin = StorefrontUrlAuthority.ResolveOrigin(
            tenant.StorefrontBaseUrl,
            tenant.Slug,
            _configuration["Storefront:UrlTemplate"]);
        var recoveryLink = $"{origin}/recover-cart/{Uri.EscapeDataString(_cartService.GenerateRecoveryToken(cart))}";
        var language = tenant.Country is "EG" or "SA" or "AE" or "KW" or "QA" or "BH" or "OM" or "JO" or "LB"
            ? "ar"
            : "en";
        var text = await _templateResolver.ResolveAbandonedCartRecoveryAsync(
            tenantId,
            new AbandonedCartRecoveryMessageData
            {
                CustomerName = customerName ?? string.Empty,
                StoreName = storeName,
                RecoveryLink = recoveryLink,
                Sequence = sequence
            },
            language,
            ct);

        return new WhatsAppAuthorizedDispatch(tenantId, accountId, cart.PhoneNumber, text);
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
                StoreName = tenantName,
                ShippingAddress = string.Join(", ", new[]
                {
                    order.ShippingAddress,
                    order.ShippingCity,
                    order.ShippingState,
                    order.ShippingZipCode
                }.Where(value => !string.IsNullOrWhiteSpace(value))),
                PaymentStatus = order.PaymentStatus.ToString()
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
            "ABANDONEDCARTRECOVERYFIRST" or "ABANDONEDCARTRECOVERYSECOND" => true,
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
