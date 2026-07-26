namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubCheckoutIdempotencyArchitectureTests
{
    [Fact]
    public void CheckoutPage_MustScopeIdempotencyKeyToAuthoritativeCheckoutFingerprint()
    {
        var source = File.ReadAllText(CommerceHubPath("src", "pages", "CheckoutPage.tsx"));

        Assert.Contains("buildCheckoutAttemptFingerprint", source, StringComparison.Ordinal);
        Assert.Contains("const requestFingerprint = buildCheckoutAttemptFingerprint(", source, StringComparison.Ordinal);
        Assert.Contains("const requestIdempotencyKey = ensureIdempotencyKeyForFingerprint(requestFingerprint);", source, StringComparison.Ordinal);
        Assert.Contains("checkoutService.placeOrder(request, requestIdempotencyKey);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutPage_MustRotateIdempotencyKeyAfterNonRetryableFailuresOnly()
    {
        var source = File.ReadAllText(CommerceHubPath("src", "pages", "CheckoutPage.tsx"));

        var nonRetryRotationPattern =
            @"if\s*\(\s*!api\.isRetryable\s*\)\s*\{\s*rotateIdempotencyKey\(\);\s*\}";

        Assert.Matches(nonRetryRotationPattern, source);
    }

    [Fact]
    public void CheckoutIdempotencyFingerprint_MustIncludeCartPricingPromoAndCustomerInputs()
    {
        var source = File.ReadAllText(CommerceHubPath("src", "pages", "checkoutIdempotency.ts"));

        Assert.Contains("promoCode", source, StringComparison.Ordinal);
        Assert.Contains("paymentMethod", source, StringComparison.Ordinal);
        Assert.Contains("fullName: customer.fullName.trim()", source, StringComparison.Ordinal);
        Assert.Contains("subtotal: Number(cart.summary.subtotal.toFixed(2))", source, StringComparison.Ordinal);
        Assert.Contains("sanitizePriceSnapshotForFingerprint(item.priceSnapshot)", source, StringComparison.Ordinal);
        Assert.Contains("quantity: item.quantity", source, StringComparison.Ordinal);
    }

    private static string CommerceHubPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.CommerceHub" }
                .Concat(parts)
                .ToArray()));
}
