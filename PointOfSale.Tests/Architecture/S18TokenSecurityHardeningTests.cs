using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PointOfSale.Tests.Architecture;

public sealed class S18TokenSecurityHardeningTests
{
    private static readonly Lazy<string?> CachedSolutionRoot = new(() => FindSolutionRoot(AppContext.BaseDirectory));

    [Fact]
    public void Storefront_MustStripTrackingTokensBeforeAnalyticsCanObserveUrl()
    {
        var mainSource = ReadRepoFile("PointOfSale.Storefront", "src", "main.tsx");
        var tokenContextSource = ReadRepoFile(
            "PointOfSale.Storefront",
            "src",
            "features",
            "checkout",
            "activeCheckoutOrderContext.ts");
        var analyticsContextSource = ReadRepoFile(
            "PointOfSale.Storefront",
            "src",
            "analytics",
            "AnalyticsContextResolver.ts");
        var checkoutCompletionSource = ReadRepoFile(
            "PointOfSale.Storefront",
            "src",
            "features",
            "checkout",
            "checkoutCompletion.ts");
        var analyticsValidatorSource = ReadRepoFile(
            "PointOfSale.API",
            "Services",
            "Analytics",
            "AnalyticsEventValidator.cs");

        Assert.Contains("removeTrackingTokenFromCurrentUrl();", mainSource, StringComparison.Ordinal);
        Assert.True(
            mainSource.IndexOf("removeTrackingTokenFromCurrentUrl();", StringComparison.Ordinal) <
            mainSource.IndexOf("initializeLanguage();", StringComparison.Ordinal));
        Assert.Contains("window.history.replaceState", tokenContextSource, StringComparison.Ordinal);
        Assert.Contains("storeTrackingTokenForOrder", tokenContextSource, StringComparison.Ordinal);
        Assert.Contains("tracking_token_removed_from_url_total", ReadRepoFile(
            "PointOfSale.Infrastructure",
            "Instrumentation",
            "CommerceReliabilityTelemetry.cs"), StringComparison.Ordinal);
        Assert.Contains("url.search = '';", analyticsContextSource, StringComparison.Ordinal);
        Assert.Contains("url.hash = '';", analyticsContextSource, StringComparison.Ordinal);
        Assert.Contains("SanitizeAnalyticsUrl(request.Context.PageUrl", analyticsValidatorSource, StringComparison.Ordinal);
        Assert.Contains("GetLeftPart(UriPartial.Path)", analyticsValidatorSource, StringComparison.Ordinal);
        Assert.Contains("clearCheckoutTokenForOrder(orderId);", checkoutCompletionSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Storefront_TrackingLinksAndApiCalls_MustNeverCarryTrackingTokenInUrl()
    {
        var confirmationSource = ReadRepoFile("PointOfSale.Storefront", "src", "pages", "OrderConfirmationPage.tsx");
        var pendingSource = ReadRepoFile("PointOfSale.Storefront", "src", "pages", "CheckoutPendingPage.tsx");
        var trackingPageSource = ReadRepoFile("PointOfSale.Storefront", "src", "pages", "OrderTrackingPage.tsx");
        var useOrderSource = ReadRepoFile("PointOfSale.Storefront", "src", "hooks", "useOrder.ts");

        Assert.DoesNotContain("track?token", confirmationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("track?token", pendingSource, StringComparison.Ordinal);
        Assert.DoesNotContain("searchParams.get('token')", trackingPageSource, StringComparison.Ordinal);
        Assert.Contains("'X-Tracking-Token'", useOrderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("params: { token }", useOrderSource, StringComparison.Ordinal);
    }

    [Fact]
    public void GuestTrackingBackend_MustFailClosed_AndAllowHeaderOrCookieToken()
    {
        var controllerSource = ReadRepoFile("PointOfSale.API", "Controllers", "StorefrontOrdersReadController.cs");

        Assert.Contains("ReadTrackingToken()", controllerSource, StringComparison.Ordinal);
        Assert.Contains("\"X-Tracking-Token\"", controllerSource, StringComparison.Ordinal);
        Assert.Contains("GuestOrderAccessCookie.ReadTrackingToken", controllerSource, StringComparison.Ordinal);
        Assert.Contains("StatusCodes.Status403Forbidden", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("[FromQuery] string? token", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("return Ok(MapToMaskedTrackingDto(order));", controllerSource, StringComparison.Ordinal);
        Assert.Contains("SendGuestTrackingLinkAsync", controllerSource, StringComparison.Ordinal);
        Assert.Contains("Accepted(CreateGuestTrackingRecoveryResponse())", controllerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DegradedAuth_MustNotUseGuestTrackingFallback()
    {
        var useOrderSource = ReadRepoFile("PointOfSale.Storefront", "src", "hooks", "useOrder.ts");
        var authStoreSource = ReadRepoFile("PointOfSale.Storefront", "src", "auth", "store", "authStore.ts");

        var degradedIndex = useOrderSource.IndexOf("authStatus === 'degraded'", StringComparison.Ordinal);
        var guestEndpointIndex = useOrderSource.IndexOf("track-guest", StringComparison.Ordinal);

        Assert.True(degradedIndex >= 0);
        Assert.True(guestEndpointIndex > degradedIndex);
        Assert.Contains("emitAuthDegradedRecovery", authStoreSource, StringComparison.Ordinal);
        Assert.Contains("status: 'degraded'", authStoreSource, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] segments)
    {
        // Strategy 1: Try standard bin/Debug/net*/ layout (4 levels up)
        var pathParts = new List<string>
        {
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."
        };
        pathParts.AddRange(segments);
        var standardPath = Path.GetFullPath(Path.Combine(pathParts.ToArray()));

        if (File.Exists(standardPath))
        {
            return File.ReadAllText(standardPath);
        }

        // Strategy 2: Search for solution root via marker files (.git, .sln) - cached
        var solutionRoot = CachedSolutionRoot.Value;
        if (solutionRoot != null)
        {
            var fromSolutionRoot = Path.Combine(new[] { solutionRoot }.Concat(segments).ToArray());
            if (File.Exists(fromSolutionRoot))
            {
                return File.ReadAllText(fromSolutionRoot);
            }
        }

        // Strategy 3: Throw clear error if file not found
        throw new FileNotFoundException(
            $"Could not locate repository file: {string.Join("/", segments)}. " +
            $"Tried standard path and solution root search from {AppContext.BaseDirectory}");
    }

    private static string? FindSolutionRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);

        while (directory != null)
        {
            // Look for solution file or .git directory as markers
            if (directory.GetFiles("*.sln").Any() || 
                directory.GetDirectories(".git").Any())
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
