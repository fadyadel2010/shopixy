using PointOfSale.Core.Enums;
using PointOfSale.Core.Models.Refunds;

namespace PointOfSale.Tests;

public sealed class RefundAllocationSupportTests
{
    [Fact]
    public void Resolve_AllowsAccountCreditOnly_WhenRefundMethodIsExplicit()
    {
        var result = RefundAllocationSupport.Resolve(RefundMethod.AccountCredit, 0m, 0m);

        Assert.True(result.Success);
        Assert.NotNull(result.Allocation);
        Assert.Equal(RefundMethod.AccountCredit, result.Allocation.Method);
        Assert.Equal(0m, result.Allocation.CashAmount);
        Assert.Equal(0m, result.Allocation.CardAmount);
        Assert.Equal(0m, result.Allocation.TotalRefundAmount);
    }

    [Fact]
    public void Resolve_RejectsMissingRefundMethod()
    {
        var result = RefundAllocationSupport.Resolve(null, null, null);

        Assert.False(result.Success);
        Assert.Null(result.Allocation);
        Assert.Equal(
            "RefundMethod is required. Use AccountCredit, Cash, Card, or Mixed.",
            result.ErrorMessage);
    }

    [Fact]
    public void Resolve_RejectsAccountCreditWhenPayoutAmountsAreProvided()
    {
        var result = RefundAllocationSupport.Resolve(RefundMethod.AccountCredit, 10m, 0m);

        Assert.False(result.Success);
        Assert.Null(result.Allocation);
        Assert.Equal(
            "AccountCredit refunds must provide zero CashAmount and zero CardAmount.",
            result.ErrorMessage);
    }

    [Fact]
    public void Resolve_RejectsCashRefundWhenCardAmountIsProvided()
    {
        var result = RefundAllocationSupport.Resolve(RefundMethod.Cash, 10m, 5m);

        Assert.False(result.Success);
        Assert.Null(result.Allocation);
        Assert.Equal(
            "Cash refunds must provide a positive CashAmount and a zero CardAmount.",
            result.ErrorMessage);
    }

    [Fact]
    public void Resolve_RejectsMixedRefundWhenOneSideIsMissing()
    {
        var result = RefundAllocationSupport.Resolve(RefundMethod.Mixed, 10m, 0m);

        Assert.False(result.Success);
        Assert.Null(result.Allocation);
        Assert.Equal(
            "Mixed refunds must provide positive CashAmount and CardAmount values.",
            result.ErrorMessage);
    }

    [Fact]
    public void Resolve_AllowsMixedRefundWhenCashAndCardAmountsArePositive()
    {
        var result = RefundAllocationSupport.Resolve(RefundMethod.Mixed, 12.345m, 7.655m);

        Assert.True(result.Success);
        Assert.NotNull(result.Allocation);
        Assert.Equal(RefundMethod.Mixed, result.Allocation.Method);
        Assert.Equal(12.35m, result.Allocation.CashAmount);
        Assert.Equal(7.66m, result.Allocation.CardAmount);
        Assert.Equal(20.01m, result.Allocation.TotalRefundAmount);
    }
}
