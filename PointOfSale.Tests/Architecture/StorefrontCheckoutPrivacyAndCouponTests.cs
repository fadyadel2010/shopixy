using System;
using System.Collections.Generic;
using System.IO;

namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontCheckoutPrivacyAndCouponTests
{
    [Fact]
    public void CartSummary_MustHideCouponInput_ForGuests()
    {
        var source = ReadRepoFile(
            "PointOfSale.Storefront",
            "src",
            "components",
            "cart",
            "CartSummary.tsx");

        Assert.Contains("isAuthenticated ? (", source, StringComparison.Ordinal);
        Assert.Contains("<CouponInput />", source, StringComparison.Ordinal);
        Assert.Contains("t('cart.signInToUseCoupons')", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutStore_MustKeepSensitiveFields_OutOfLocalStoragePersistence()
    {
        var source = ReadRepoFile(
            "PointOfSale.Storefront",
            "src",
            "store",
            "checkoutStore.ts");

        Assert.Contains("SENSITIVE_STORAGE_KEY = 'checkout-sensitive-session'", source, StringComparison.Ordinal);
        Assert.Contains("window.sessionStorage", source, StringComparison.Ordinal);
        // Verify clearSensitiveCheckoutSessionStorage is actually invoked (not just declared)
        // by requiring the trailing semicolon that indicates a function call
        Assert.Matches(@"clearSensitiveCheckoutSessionStorage\s*\(\s*\)\s*;", source);
        
        // Verify the call is specifically within the submitCheckout function
        // This ensures the sensitive data is cleared after successful checkout submission
        Assert.Matches(@"submitCheckout:\s*async\s*\(\s*\)\s*=>\s*\{[\s\S]*?clearSensitiveCheckoutSessionStorage\s*\(\s*\)\s*;", source);

        var partializeStart = source.IndexOf("partialize: (state) => ({", StringComparison.Ordinal);
        Assert.True(partializeStart >= 0, "checkoutStore.ts must define a partialize block.");

        var partializeEnd = source.IndexOf("}),", partializeStart, StringComparison.Ordinal);
        Assert.True(partializeEnd > partializeStart, "checkoutStore.ts must close the partialize block.");

        var partializeBlock = source.Substring(partializeStart, partializeEnd - partializeStart);
        Assert.DoesNotContain("address:", partializeBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("manualWalletType:", partializeBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("manualWalletPhoneNumber:", partializeBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("manualWalletProofFileKey:", partializeBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("manualWalletUploadToken:", partializeBlock, StringComparison.Ordinal);

        var sensitiveSnapshotStart = source.IndexOf("function buildSensitiveCheckoutStateSnapshot(", StringComparison.Ordinal);
        Assert.True(sensitiveSnapshotStart >= 0, "checkoutStore.ts must define a sensitive session snapshot builder.");
        var sensitiveSnapshotEnd = source.IndexOf("function isSensitiveCheckoutStateEmpty(", sensitiveSnapshotStart, StringComparison.Ordinal);
        Assert.True(sensitiveSnapshotEnd > sensitiveSnapshotStart, "checkoutStore.ts must close the sensitive session snapshot builder.");

        var sensitiveSnapshotBlock = source.Substring(sensitiveSnapshotStart, sensitiveSnapshotEnd - sensitiveSnapshotStart);
        Assert.DoesNotContain("manualWalletType", sensitiveSnapshotBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("manualWalletPhoneNumber", sensitiveSnapshotBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("manualWalletProofFileKey", sensitiveSnapshotBlock, StringComparison.Ordinal);
        Assert.DoesNotContain("manualWalletUploadToken", sensitiveSnapshotBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutPage_MustClearSensitiveSessionState_WhenCheckoutCompletes()
    {
        var source = ReadRepoFile(
            "PointOfSale.Storefront",
            "src",
            "pages",
            "CheckoutPage.tsx");

        // CheckoutPage must NOT directly clear sensitive session storage
        // The store (checkoutStore.ts) owns this responsibility to avoid duplicate cleanup
        Assert.DoesNotContain("clearSensitiveCheckoutSessionStorage", source, StringComparison.Ordinal);
        
        // Verify CheckoutPage delegates to submitCheckout (which handles cleanup)
        Assert.Matches(@"submitCheckout\s*\(\s*\)", source);
    }

    private static string ReadRepoFile(params string[] segments)
    {
        var pathParts = new List<string>
        {
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."
        };

        pathParts.AddRange(segments);
        return File.ReadAllText(Path.GetFullPath(Path.Combine(pathParts.ToArray())));
    }
}
