using PointOfSale.API.Controllers;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services;

/// <summary>
/// Internal helper for resolving purchase payment splits.
/// Validates and calculates cash/card amounts based on payment method and request data.
/// </summary>
internal static class PurchasePaymentSplitter
{
    /// <summary>
    /// Resolves the payment split for a purchase request.
    /// </summary>
    /// <returns>
    /// A tuple containing:
    /// - Success: Whether the split resolution succeeded
    /// - PaidAmount: The normalized paid amount used for persistence
    /// - CashAmount: The resolved cash portion
    /// - CardAmount: The resolved card portion
    /// - ErrorMessage: Error description if Success is false
    /// </returns>
    internal static (bool Success, decimal PaidAmount, decimal CashAmount, decimal CardAmount, string? ErrorMessage)
        Resolve(CreatePurchaseRequest request, decimal? totalAmount = null)
    {
        // Validate raw values before rounding to catch tiny negative values
        if (request.PaidAmount < 0)
        {
            return (false, 0m, 0m, 0m, "Paid amount cannot be negative.");
        }

        if ((request.CashAmount ?? 0m) < 0 || (request.CardAmount ?? 0m) < 0)
        {
            return (false, 0m, 0m, 0m, "Cash and card amounts cannot be negative.");
        }

        var normalizedPaidAmount = RoundCurrency(request.PaidAmount);
        var cashAmount = RoundCurrency(request.CashAmount ?? 0m);
        var cardAmount = RoundCurrency(request.CardAmount ?? 0m);

        if (totalAmount.HasValue)
        {
            var normalizedTotalAmount = RoundCurrency(totalAmount.Value);
            if (normalizedPaidAmount > normalizedTotalAmount)
            {
                return (false, 0m, 0m, 0m, "Paid amount cannot exceed total amount.");
            }
        }

        if (normalizedPaidAmount == 0)
        {
            // When PaidAmount is zero, split fields must also be zero
            if (cashAmount != 0 || cardAmount != 0)
            {
                return (false, 0m, 0m, 0m, "CashAmount and CardAmount must be zero when PaidAmount is zero.");
            }
            
            return (true, 0m, 0m, 0m, null);
        }

        var explicitSplitProvided = request.CashAmount.HasValue || request.CardAmount.HasValue;

        if (explicitSplitProvided)
        {
            if (RoundCurrency(cashAmount + cardAmount) != normalizedPaidAmount)
            {
                return (false, 0m, 0m, 0m, "CashAmount and CardAmount must add up exactly to PaidAmount.");
            }

            return request.PaymentMethod switch
            {
                PaymentMethod.Cash when cardAmount != 0m
                    => (false, 0m, 0m, 0m, "Cash purchases cannot include a card amount."),
                PaymentMethod.Card when cashAmount != 0m
                    => (false, 0m, 0m, 0m, "Card purchases cannot include a cash amount."),
                PaymentMethod.Mixed when cashAmount <= 0m || cardAmount <= 0m
                    => (false, 0m, 0m, 0m, "Mixed purchases must include both cash and card amounts."),
                PaymentMethod.BankTransfer
                    => (false, 0m, 0m, 0m, $"Unsupported payment method {request.PaymentMethod}."),
                _ => (true, normalizedPaidAmount, cashAmount, cardAmount, null)
            };
        }

        if (request.PaymentMethod == PaymentMethod.Credit)
        {
            return request.InitialPaymentMethod switch
            {
                PaymentMethod.Cash => (true, normalizedPaidAmount, normalizedPaidAmount, 0m, null),
                PaymentMethod.Card => (true, normalizedPaidAmount, 0m, normalizedPaidAmount, null),
                PaymentMethod.Mixed => (false, 0m, 0m, 0m, "Credit purchases with partial payment must provide CashAmount and CardAmount for mixed settlement."),
                _ => (false, 0m, 0m, 0m, "Credit purchases with a paid amount must specify InitialPaymentMethod or explicit CashAmount/CardAmount.")
            };
        }

        return request.PaymentMethod switch
        {
            PaymentMethod.Cash => (true, normalizedPaidAmount, normalizedPaidAmount, 0m, null),
            PaymentMethod.Card => (true, normalizedPaidAmount, 0m, normalizedPaidAmount, null),
            PaymentMethod.Mixed => (false, 0m, 0m, 0m, "Mixed purchases must provide CashAmount and CardAmount."),
            _ => (false, 0m, 0m, 0m, $"Unsupported payment method {request.PaymentMethod}.")
        };
    }

    private static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
