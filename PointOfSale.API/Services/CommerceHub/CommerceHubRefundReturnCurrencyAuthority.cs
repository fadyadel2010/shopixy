using PointOfSale.Core.Entities;
using PointOfSale.Core.Financial;

namespace PointOfSale.API.Services.CommerceHub;

/// <summary>
/// Currency evidence on a CommerceHub refund or return is never display input.
/// The platform's EGP authority is re-proven at each durable/reflection boundary.
/// </summary>
internal static class CommerceHubRefundReturnCurrencyAuthority
{
    public static string RequireOrderCurrency(CommerceHubMarketplaceOrder order, string context)
    {
        ArgumentNullException.ThrowIfNull(order);
        return FinancialCurrencyGuard.RequireEgpCurrency(
            order.Currency,
            "commercehub.refund_return.order.currency",
            context);
    }

    public static string RequireRefundCurrency(CommerceHubRefund refund, string context)
    {
        ArgumentNullException.ThrowIfNull(refund);
        return FinancialCurrencyGuard.RequireEgpCurrency(
            refund.Currency,
            "commercehub.refund.currency",
            context);
    }

    public static string RequireConsistentExecutionCurrency(
        CommerceHubMarketplaceOrder order,
        CommerceHubSubOrder subOrder,
        CommerceHubRefund? refund,
        CommerceHubPayment? payment,
        string context)
    {
        var orderCurrency = RequireOrderCurrency(order, context);
        FinancialCurrencyGuard.RequireExpectedCurrency(
            subOrder.Currency,
            orderCurrency,
            "commercehub.refund_return.sub_order.currency",
            context);

        if (refund != null)
        {
            FinancialCurrencyGuard.RequireExpectedCurrency(
                refund.Currency,
                orderCurrency,
                "commercehub.refund.currency",
                context);
        }

        if (payment != null)
        {
            FinancialCurrencyGuard.RequireExpectedCurrency(
                payment.Currency,
                orderCurrency,
                "commercehub.refund.payment.currency",
                context);
        }

        return orderCurrency;
    }
}
