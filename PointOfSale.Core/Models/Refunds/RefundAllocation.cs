using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Models.Refunds;

public sealed record RefundAllocation(RefundMethod Method, decimal CashAmount, decimal CardAmount)
{
    public decimal TotalRefundAmount => RefundAllocationSupport.RoundCurrency(CashAmount + CardAmount);
    public bool HasImmediatePayout => TotalRefundAmount > 0m;

    public static RefundAllocation AccountCredit { get; } = new(RefundMethod.AccountCredit, 0m, 0m);
}

/// <summary>
/// Represents the result of a refund allocation resolution operation.
/// Use <see cref="Succeeded"/> or <see cref="Failed"/> factory methods to create instances.
/// </summary>
public sealed record RefundAllocationResolution
{
    public bool Success { get; }
    public RefundAllocation? Allocation { get; }
    public string? ErrorMessage { get; }

    private RefundAllocationResolution(bool success, RefundAllocation? allocation, string? errorMessage)
    {
        if (success && allocation is null)
        {
            throw new ArgumentException("Success=true requires a non-null Allocation.", nameof(allocation));
        }

        if (!success && allocation is not null)
        {
            throw new ArgumentException("Success=false requires a null Allocation.", nameof(allocation));
        }

        Success = success;
        Allocation = allocation;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Creates a successful resolution with the specified allocation.
    /// </summary>
    public static RefundAllocationResolution Succeeded(RefundAllocation allocation)
    {
        ArgumentNullException.ThrowIfNull(allocation);
        return new(true, allocation, null);
    }

    /// <summary>
    /// Creates a failed resolution with the specified error message.
    /// </summary>
    public static RefundAllocationResolution Failed(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new(false, null, errorMessage);
    }
}

public static class RefundAllocationSupport
{
    public static RefundAllocationResolution Resolve(
        RefundMethod? refundMethod,
        decimal? cashAmount,
        decimal? cardAmount)
    {
        var normalizedCashAmount = RoundCurrency(cashAmount ?? 0m);
        var normalizedCardAmount = RoundCurrency(cardAmount ?? 0m);

        if (normalizedCashAmount < 0m || normalizedCardAmount < 0m)
        {
            return RefundAllocationResolution.Failed(
                "Cash and card refund amounts cannot be negative.");
        }

        if (!refundMethod.HasValue)
        {
            return RefundAllocationResolution.Failed(
                "RefundMethod is required. Use AccountCredit, Cash, Card, or Mixed.");
        }

        if (!Enum.IsDefined(typeof(RefundMethod), refundMethod.Value))
        {
            return RefundAllocationResolution.Failed(
                $"Invalid RefundMethod value: {(int)refundMethod.Value}. Must be a valid RefundMethod enum value.");
        }

        return refundMethod.Value switch
        {
            RefundMethod.AccountCredit when normalizedCashAmount != 0m || normalizedCardAmount != 0m
                => RefundAllocationResolution.Failed(
                    "AccountCredit refunds must provide zero CashAmount and zero CardAmount."),
            RefundMethod.AccountCredit
                => RefundAllocationResolution.Succeeded(RefundAllocation.AccountCredit),
            RefundMethod.Cash when normalizedCashAmount <= 0m || normalizedCardAmount != 0m
                => RefundAllocationResolution.Failed(
                    "Cash refunds must provide a positive CashAmount and a zero CardAmount."),
            RefundMethod.Card when normalizedCardAmount <= 0m || normalizedCashAmount != 0m
                => RefundAllocationResolution.Failed(
                    "Card refunds must provide a positive CardAmount and a zero CashAmount."),
            RefundMethod.Mixed when normalizedCashAmount <= 0m || normalizedCardAmount <= 0m
                => RefundAllocationResolution.Failed(
                    "Mixed refunds must provide positive CashAmount and CardAmount values."),
            _ => RefundAllocationResolution.Succeeded(
                new RefundAllocation(refundMethod.Value, normalizedCashAmount, normalizedCardAmount))
        };
    }

    public static decimal RoundCurrency(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
