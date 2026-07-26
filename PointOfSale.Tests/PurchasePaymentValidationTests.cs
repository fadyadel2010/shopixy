using PointOfSale.API.Controllers;
using PointOfSale.Core.Enums;
using Xunit;

namespace PointOfSale.Tests;

/// <summary>
/// Tests for purchase payment split validation logic.
/// These tests verify the PurchasePaymentSplitter.Resolve method behavior.
/// </summary>
public class PurchasePaymentValidationTests
{
    [Fact]
    public void ResolvePurchasePaymentSplit_ZeroPaidAmount_WithZeroSplits_ShouldSucceed()
    {
        // Arrange
        var request = new CreatePurchaseRequest
        {
            PaymentMethod = PaymentMethod.Credit,
            PaidAmount = 0m,
            CashAmount = null,
            CardAmount = null
        };

        // Act
        var result = InvokeResolvePurchasePaymentSplit(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0m, result.PaidAmount);
        Assert.Equal(0m, result.CashAmount);
        Assert.Equal(0m, result.CardAmount);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ResolvePurchasePaymentSplit_ZeroPaidAmount_WithNonZeroCashAmount_ShouldFail()
    {
        // Arrange
        var request = new CreatePurchaseRequest
        {
            PaymentMethod = PaymentMethod.Credit,
            PaidAmount = 0m,
            CashAmount = 50m,
            CardAmount = null
        };

        // Act
        var result = InvokeResolvePurchasePaymentSplit(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(0m, result.PaidAmount);
        Assert.Equal(0m, result.CashAmount);
        Assert.Equal(0m, result.CardAmount);
        Assert.Equal("CashAmount and CardAmount must be zero when PaidAmount is zero.", result.ErrorMessage);
    }

    [Fact]
    public void ResolvePurchasePaymentSplit_ZeroPaidAmount_WithNonZeroCardAmount_ShouldFail()
    {
        // Arrange
        var request = new CreatePurchaseRequest
        {
            PaymentMethod = PaymentMethod.Credit,
            PaidAmount = 0m,
            CashAmount = null,
            CardAmount = 100m
        };

        // Act
        var result = InvokeResolvePurchasePaymentSplit(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(0m, result.PaidAmount);
        Assert.Equal(0m, result.CashAmount);
        Assert.Equal(0m, result.CardAmount);
        Assert.Equal("CashAmount and CardAmount must be zero when PaidAmount is zero.", result.ErrorMessage);
    }

    [Fact]
    public void ResolvePurchasePaymentSplit_ZeroPaidAmount_WithBothNonZeroSplits_ShouldFail()
    {
        // Arrange
        var request = new CreatePurchaseRequest
        {
            PaymentMethod = PaymentMethod.Credit,
            PaidAmount = 0m,
            CashAmount = 30m,
            CardAmount = 70m
        };

        // Act
        var result = InvokeResolvePurchasePaymentSplit(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(0m, result.PaidAmount);
        Assert.Equal(0m, result.CashAmount);
        Assert.Equal(0m, result.CardAmount);
        Assert.Equal("CashAmount and CardAmount must be zero when PaidAmount is zero.", result.ErrorMessage);
    }

    [Fact]
    public void ResolvePurchasePaymentSplit_ZeroPaidAmount_WithExplicitZeroSplits_ShouldSucceed()
    {
        // Arrange
        var request = new CreatePurchaseRequest
        {
            PaymentMethod = PaymentMethod.Credit,
            PaidAmount = 0m,
            CashAmount = 0m,
            CardAmount = 0m
        };

        // Act
        var result = InvokeResolvePurchasePaymentSplit(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(0m, result.PaidAmount);
        Assert.Equal(0m, result.CashAmount);
        Assert.Equal(0m, result.CardAmount);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ResolvePurchasePaymentSplit_WithComputedTotal_RejectsOverpayment()
    {
        var request = new CreatePurchaseRequest
        {
            PaymentMethod = PaymentMethod.Credit,
            InitialPaymentMethod = PaymentMethod.Card,
            PaidAmount = 120m
        };

        var result = InvokeResolvePurchasePaymentSplit(request, totalAmount: 100m);

        Assert.False(result.Success);
        Assert.Equal(0m, result.PaidAmount);
        Assert.Equal(0m, result.CashAmount);
        Assert.Equal(0m, result.CardAmount);
        Assert.Equal("Paid amount cannot exceed total amount.", result.ErrorMessage);
    }

    [Fact]
    public void ResolvePurchasePaymentSplit_WithComputedTotal_AllowsExactPayment()
    {
        var request = new CreatePurchaseRequest
        {
            PaymentMethod = PaymentMethod.Card,
            PaidAmount = 100m
        };

        var result = InvokeResolvePurchasePaymentSplit(request, totalAmount: 100m);

        Assert.True(result.Success);
        Assert.Equal(100m, result.PaidAmount);
        Assert.Equal(0m, result.CashAmount);
        Assert.Equal(100m, result.CardAmount);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ResolvePurchasePaymentSplit_WithComputedTotal_AllowsPartialPayment()
    {
        var request = new CreatePurchaseRequest
        {
            PaymentMethod = PaymentMethod.Credit,
            InitialPaymentMethod = PaymentMethod.Card,
            PaidAmount = 60m
        };

        var result = InvokeResolvePurchasePaymentSplit(request, totalAmount: 100m);

        Assert.True(result.Success);
        Assert.Equal(60m, result.PaidAmount);
        Assert.Equal(0m, result.CashAmount);
        Assert.Equal(60m, result.CardAmount);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ResolvePurchasePaymentSplit_WithComputedTotal_UsesRoundedCurrencyComparison()
    {
        var request = new CreatePurchaseRequest
        {
            PaymentMethod = PaymentMethod.Card,
            PaidAmount = 100.01m
        };

        var result = InvokeResolvePurchasePaymentSplit(request, totalAmount: 100.005m);

        Assert.True(result.Success);
        Assert.Equal(100.01m, result.PaidAmount);
        Assert.Equal(0m, result.CashAmount);
        Assert.Equal(100.01m, result.CardAmount);
        Assert.Null(result.ErrorMessage);
    }

    /// <summary>
    /// Calls the internal PurchasePaymentSplitter.Resolve method directly.
    /// </summary>
    private static (bool Success, decimal PaidAmount, decimal CashAmount, decimal CardAmount, string? ErrorMessage)
        InvokeResolvePurchasePaymentSplit(CreatePurchaseRequest request, decimal? totalAmount = null)
    {
        return PointOfSale.API.Services.PurchasePaymentSplitter.Resolve(request, totalAmount);
    }
}
