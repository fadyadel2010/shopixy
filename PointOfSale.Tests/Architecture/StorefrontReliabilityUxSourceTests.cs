using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontReliabilityUxSourceTests
{
    [Fact]
    public void SearchUi_MustSurfaceSearchUnavailable_InsteadOfEmptyResults()
    {
        var searchStoreSource = ReadRepoFile(
            "PointOfSale.Storefront",
            "src",
            "stores",
            "searchStore.ts");
        var hookSource = ReadRepoFile(
            "PointOfSale.Storefront",
            "src",
            "hooks",
            "useProductSearch.ts");

        // Verify searchStore.ts handles SEARCH_UNAVAILABLE error code
        AssertMatchesPattern(
            searchStoreSource,
            @"normalized\.code\s*===?\s*['""]SEARCH_UNAVAILABLE['""]",
            "searchStore.ts must check for SEARCH_UNAVAILABLE error code");
        
        AssertMatchesPattern(
            searchStoreSource,
            @"['""]Search is (temporarily )?unavailable['""]",
            "searchStore.ts must provide a user-friendly search unavailable message");
        
        AssertMatchesPattern(
            searchStoreSource,
            @"autocompleteStatus:\s*['""]error['""]",
            "searchStore.ts must set autocompleteStatus to 'error' on failure");
        
        AssertMatchesPattern(
            searchStoreSource,
            @"searchStatus:\s*['""]error['""]",
            "searchStore.ts must set searchStatus to 'error' on failure");

        // Verify useProductSearch.ts handles SEARCH_UNAVAILABLE error code
        AssertMatchesPattern(
            hookSource,
            @"normalized\.code\s*===?\s*['""]SEARCH_UNAVAILABLE['""]",
            "useProductSearch.ts must check for SEARCH_UNAVAILABLE error code");
        
        AssertMatchesPattern(
            hookSource,
            @"['""]Search is (temporarily )?unavailable['""]",
            "useProductSearch.ts must provide a user-friendly search unavailable message");
        
        AssertMatchesPattern(
            hookSource,
            @"setStatus\(['""]error['""]\)",
            "useProductSearch.ts must transition to error status on failure");
    }

    [Fact]
    public void PaymentStep_MustDisableUnavailableManualWallet_And_ShowExplicitMessage()
    {
        var source = ReadRepoFile(
            "PointOfSale.Storefront",
            "src",
            "components",
            "checkout",
            "PaymentStep.tsx");

        // Verify payment method availability check
        AssertMatchesPattern(
            source,
            @"!method\.isAvailable",
            "PaymentStep must check method.isAvailable to disable unavailable payment methods");
        
        // Verify user-facing message for unavailable payment methods
        AssertMatchesPattern(
            source,
            @"['""]Payment method (is )?unavailable['""]",
            "PaymentStep must show explicit message when payment method is unavailable");
        
        // Verify canContinue logic checks both selectedCode and isAvailable
        AssertMatchesPattern(
            source,
            @"(const|let|var)\s+canContinue\s*=\s*!!\s*selectedCode\s*&&\s*!!\s*selectedMethod\?\.isAvailable",
            "PaymentStep must prevent continuation when payment method is unavailable");
    }

    [Fact]
    public void PaymentStep_MustUseAuthoritativePreviewTotal_ForPaymentMethodMinMaxChecks()
    {
        var source = ReadRepoFile(
            "PointOfSale.Storefront",
            "src",
            "components",
            "checkout",
            "PaymentStep.tsx");

        AssertMatchesPattern(
            source,
            @"orderAmountForValidation\s*=\s*pricing\?\.total\s*\?\?\s*cartSubtotal",
            "PaymentStep must use authoritative preview total when available for payment method amount limits");

        Assert.DoesNotContain("pricing?.subtotal ?? cartSubtotal", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeMarketplacePage_MustOnlyRenderBackendProvidedActiveTheme()
    {
        var source = ReadRepoFile(
            "PointOfSale.Web",
            "src",
            "modules",
            "storefront",
            "pages",
            "ThemeMarketplacePage.jsx");

        // Verify activeThemeId comes from backend (storeConfig)
        // Negative lookahead ensures no additional fallback operators after null
        AssertMatchesPattern(
            source,
            @"(const|let|var)\s+activeThemeId\s*=\s*storeConfig\?\.storefrontTheme\s*\|\|\s*null(?!\s*(\|\||\?\?))",
            "ThemeMarketplacePage must derive activeThemeId from backend storeConfig");
        
        // Verify activeTheme is derived from activeThemeId (no default fallback)
        // Negative lookahead ensures no additional fallback operators after null
        AssertMatchesPattern(
            source,
            @"(const|let|var)\s+activeTheme\s*=\s*activeThemeId\s*\?\s*getMarketplaceThemeById\s*\(\s*activeThemeId\s*\)\s*:\s*null(?!\s*(\|\||\?\?))",
            "ThemeMarketplacePage must only use backend-provided theme, not a default");
        
        // Verify no default theme fallback is used
        Assert.DoesNotContain("getDefaultMarketplaceTheme", source, StringComparison.Ordinal);
        
        // Verify explicit message when no theme is provided
        AssertMatchesPattern(
            source,
            @"(The )?backend has not provided an active theme( yet)?",
            "ThemeMarketplacePage must show explicit message when backend hasn't provided a theme");
    }

    /// <summary>
    /// Assert that source code matches a regex pattern, providing a clear failure message.
    /// This approach is more resilient to formatting changes and equivalent refactors.
    /// </summary>
    private static void AssertMatchesPattern(string source, string pattern, string failureMessage)
    {
        var regex = new Regex(pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Assert.True(
            regex.IsMatch(source),
            $"{failureMessage}\nExpected pattern: {pattern}\nSource did not match the required pattern.");
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
