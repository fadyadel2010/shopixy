using PointOfSale.API.DTOs.Checkout;

namespace PointOfSale.Tests.TestDoubles;

/// <summary>
/// Provides shared test fixtures for checkout-related tests to avoid duplication and drift.
/// </summary>
public static class CheckoutTestFixtures
{
    /// <summary>
    /// Returns a canonical CheckoutExpectedPricingDto with standard test values.
    /// Used across multiple test files to ensure consistency.
    /// </summary>
    public static CheckoutExpectedPricingDto CreateDefaultExpectedPricing() =>
        new()
        {
            Subtotal = 100m,
            Discount = 10m,
            CouponDiscount = 10m,
            PromotionDiscount = 0m,
            CartPromotionDiscount = 0m,
            Shipping = 20m,
            Tax = 5m,
            PaymentFee = 0m,
            Total = 115m
        };
}
