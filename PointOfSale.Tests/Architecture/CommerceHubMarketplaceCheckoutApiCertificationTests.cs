namespace PointOfSale.Tests.Architecture;

public sealed class CommerceHubMarketplaceCheckoutApiCertificationTests
{
    [Fact]
    public void MarketplaceCheckoutApiSurface_MustReuseCertifiedCommerceHubCheckoutAndCartEndpoints()
    {
        var checkoutController = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubCheckoutController.cs");
        var cartController = ReadRepoFile("PointOfSale.API/Controllers/CommerceHubCartController.cs");

        Assert.Contains("[Route(\"api/commercehub/checkout\")]", checkoutController, StringComparison.Ordinal);
        Assert.Contains("private readonly ICommerceHubCheckoutService _checkoutService;", checkoutController, StringComparison.Ordinal);
        Assert.Contains("_checkoutService.CheckoutAsync(request, customerId.Value, traceId, idempotencyKey, cancellationToken)", checkoutController, StringComparison.Ordinal);
        Assert.Contains("Authorize(AuthenticationSchemes = CustomerSecurityConstants.AuthenticationScheme", checkoutController, StringComparison.Ordinal);
        Assert.Contains("Idempotency-Key", checkoutController, StringComparison.Ordinal);
        Assert.Contains("new ApiErrorResponse", checkoutController, StringComparison.Ordinal);

        Assert.Contains("[Route(\"api/commercehub/cart\")]", cartController, StringComparison.Ordinal);
        Assert.Contains("private readonly ICommerceHubCartService _cartService;", cartController, StringComparison.Ordinal);
        Assert.Contains("_cartService.GetCartAsync(owner, traceId, cancellationToken)", cartController, StringComparison.Ordinal);
        Assert.Contains("_cartService.SaveCartAsync(request, owner, traceId, cancellationToken)", cartController, StringComparison.Ordinal);
        Assert.Contains("_cartService.CalculateAsync(request, owner, traceId, cancellationToken)", cartController, StringComparison.Ordinal);
        Assert.Contains("new ApiErrorResponse", cartController, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceApis_MustNotIntroduceMarketplaceCheckoutPaymentShippingOrShipmentExecutionEndpoints()
    {
        var productionSources = EnumerateProductionFiles("*.cs")
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => new { Path = path, Text = File.ReadAllText(path) })
            .ToArray();

        var combinedSources = string.Join('\n', productionSources.Select(source => source.Text));

        Assert.DoesNotContain("class MarketplaceCheckoutController", combinedSources, StringComparison.Ordinal);
        Assert.DoesNotContain("class MarketplaceCheckoutService", combinedSources, StringComparison.Ordinal);
        Assert.DoesNotContain("class MarketplacePaymentController", combinedSources, StringComparison.Ordinal);
        Assert.DoesNotContain("class MarketplacePaymentService", combinedSources, StringComparison.Ordinal);
        Assert.DoesNotContain("class MarketplaceShippingController", combinedSources, StringComparison.Ordinal);
        Assert.DoesNotContain("class MarketplaceShippingService", combinedSources, StringComparison.Ordinal);
        Assert.DoesNotContain("class MarketplaceShipmentController", combinedSources, StringComparison.Ordinal);
        Assert.DoesNotContain("class MarketplaceShipmentService", combinedSources, StringComparison.Ordinal);
        Assert.DoesNotContain("[Route(\"api/marketplace/checkout", combinedSources, StringComparison.Ordinal);
        Assert.DoesNotContain("[Route(\"api/marketplace/payment", combinedSources, StringComparison.Ordinal);
        Assert.DoesNotContain("[Route(\"api/marketplace/shipping", combinedSources, StringComparison.Ordinal);
        Assert.DoesNotContain("[Route(\"api/marketplace/shipment", combinedSources, StringComparison.Ordinal);
    }

    [Fact]
    public void MerchantGroupingAndSplitPreviewApis_MustRemainPlanningAndReuseCheckoutAuthority()
    {
        var cartService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCartService.cs");
        var checkoutService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCheckoutService.cs");
        var checkoutDtos = ReadRepoFile("PointOfSale.API/DTOs/CommerceHub/CommerceHubCheckoutDtos.cs");

        Assert.Contains(".GroupBy(line => line.StoreId)", cartService, StringComparison.Ordinal);
        Assert.Contains("StoreId = group.Key", cartService, StringComparison.Ordinal);
        Assert.Contains("request.ShippingSelections", cartService, StringComparison.Ordinal);

        Assert.Contains("IsolationLevel.Serializable", checkoutService, StringComparison.Ordinal);
        Assert.Contains("LockCartForCheckoutAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("EnsureLockedCartMatchesCheckoutRequest", checkoutService, StringComparison.Ordinal);
        Assert.Contains("LoadCommerceHubListingsForCheckoutAuthorityAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("LoadTenantPaymentMethodsForCheckoutAuthorityAsync", checkoutService, StringComparison.Ordinal);
        Assert.Contains("using var shippingAssignmentBoundary = CommerceHubBoundaryMutationScope.BeginShippingAssignment();", checkoutService, StringComparison.Ordinal);
        Assert.Contains("using var paymentLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginPaymentLifecycle();", checkoutService, StringComparison.Ordinal);
        Assert.Contains("using var financialBoundary = CommerceHubBoundaryMutationScope.BeginFinancialWorkflow();", checkoutService, StringComparison.Ordinal);

        Assert.Contains("public List<CommerceHubCheckoutStoreRequestDto> Stores", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("public List<CommerceHubCheckoutShippingSelectionRequestDto> ShippingSelections", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("Shipping selections must be provided for every store in the cart.", checkoutDtos, StringComparison.Ordinal);
        Assert.Contains("Each checkout item must include the unit price shown to the customer.", checkoutDtos, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceDtos_MustNotOwnCheckoutPaymentShippingInventoryOrderOrShipmentTruth()
    {
        var marketplaceDtos = EnumerateProductionFiles("*.cs")
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}DTOs{Path.DirectorySeparatorChar}Marketplace{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                           path.Contains($"{Path.DirectorySeparatorChar}DTOs{Path.DirectorySeparatorChar}MarketplaceReviews{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(File.ReadAllText)
            .ToArray();

        var combinedDtos = string.Join('\n', marketplaceDtos);

        foreach (var forbiddenMarker in new[]
        {
            "CheckoutTotal",
            "PaymentIntent",
            "PaymentSecret",
            "ClientSecret",
            "ShippingTruth",
            "ShipmentTruth",
            "InventoryReservation",
            "OrderTruth",
            "CarrierExecution",
            "ProviderSecret"
        })
        {
            Assert.DoesNotContain(forbiddenMarker, combinedDtos, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void MarketplaceApiCertificationDocumentation_MustBeFrozen()
    {
        var certification = ReadRepoFile("docs/architecture/COMMERCEHUB_MARKETPLACE_CHECKOUT_2_APIS.md");

        foreach (var marker in new[]
        {
            "## Executive Summary",
            "## API Inventory",
            "## Controller Review",
            "## DTO Review",
            "## Authority Matrix",
            "## Reuse Matrix",
            "## Security Review",
            "## Regression Coverage",
            "## Certification Decision",
            "Marketplace APIs Certified? **Yes.**",
            "Checkout authority introduced? **No.**"
        })
        {
            Assert.Contains(marker, certification, StringComparison.Ordinal);
        }
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);

    private static IEnumerable<string> EnumerateProductionFiles(string pattern)
    {
        var root = FindRepoRoot(AppContext.BaseDirectory);
        return new[]
            {
                Path.Combine(root, "PointOfSale.API"),
                Path.Combine(root, "PointOfSale.Core"),
                Path.Combine(root, "PointOfSale.Infrastructure")
            }
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories));
    }

    private static string FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
