namespace PointOfSale.Tests.Architecture;

public sealed class POSAdminCheckout2AdministrationStorefrontApiCertificationTests
{
    [Fact]
    public void StorefrontCheckoutApiRoutes_MustExposeOnlyCertifiedCheckoutSurface()
    {
        var checkoutController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");
        var sessionController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");

        Assert.Contains("[Route(\"api/storefront/checkout\")]", checkoutController, StringComparison.Ordinal);
        Assert.Contains("[HttpPost]", checkoutController, StringComparison.Ordinal);
        Assert.Contains("[HttpPost(\"pricing\")]", checkoutController, StringComparison.Ordinal);

        Assert.Contains("[Route(\"api/storefront/checkout-session\")]", sessionController, StringComparison.Ordinal);
        Assert.Contains("[HttpPost]", sessionController, StringComparison.Ordinal);
        Assert.Contains("[HttpPatch(\"{id:guid}\")]", sessionController, StringComparison.Ordinal);
        Assert.Contains("[HttpGet(\"recover/{token}\")]", sessionController, StringComparison.Ordinal);

        Assert.DoesNotContain("CommerceHubCheckoutController", checkoutController, StringComparison.Ordinal);
        Assert.DoesNotContain("Affiliate", checkoutController, StringComparison.Ordinal);
    }

    [Fact]
    public void Controllers_MustOwnHttpAuthorizationValidationAndDelegateRuntimeAuthority()
    {
        var checkoutController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");
        var sessionController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");

        Assert.Contains("ValidateRequestContextAsync", checkoutController, StringComparison.Ordinal);
        Assert.Contains("ResolveAuthorizedCustomerHeader", checkoutController, StringComparison.Ordinal);
        Assert.Contains("CUSTOMER_ID_MISMATCH", checkoutController, StringComparison.Ordinal);
        Assert.Contains("_checkoutService.ProcessCheckoutAsync", checkoutController, StringComparison.Ordinal);
        Assert.Contains("_checkoutService.PreviewPricingAsync", checkoutController, StringComparison.Ordinal);

        Assert.Contains("ValidateAuthenticatedCustomerTenant", sessionController, StringComparison.Ordinal);
        Assert.Contains("isOwnerBySessionId", sessionController, StringComparison.Ordinal);
        Assert.Contains("isOwnerByCustomerId", sessionController, StringComparison.Ordinal);
        Assert.Contains("_sessionService.CreateOrResumeSessionWithOutcomeAsync", sessionController, StringComparison.Ordinal);
        Assert.Contains("_sessionService.UpdateSessionWithOutcomeAsync", sessionController, StringComparison.Ordinal);
        Assert.Contains("_sessionService.ResolveRecoverySessionAsync", sessionController, StringComparison.Ordinal);
        Assert.Contains("_cartService.GetCartAsync", sessionController, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutDtos_MustAdmitCandidatesOnlyAndRejectClientAuthority()
    {
        var checkoutDtos = ReadRepoFile("PointOfSale.API/DTOs/Checkout/CheckoutDtos.cs");
        var sessionDtos = ReadRepoFile("PointOfSale.API/DTOs/Checkout/CheckoutSessionDtos.cs");
        var checkoutRequest = ExtractClass(checkoutDtos, "CheckoutRequestDto");
        var createSessionRequest = ExtractClass(sessionDtos, "CreateCheckoutSessionRequest");
        var updateSessionRequest = ExtractClass(sessionDtos, "UpdateCheckoutSessionRequest");

        Assert.Contains("public Guid CartId", checkoutRequest, StringComparison.Ordinal);
        Assert.Contains("public Guid ShippingMethodId", checkoutRequest, StringComparison.Ordinal);
        Assert.Contains("public string PaymentMethod", checkoutRequest, StringComparison.Ordinal);
        Assert.Contains("public CheckoutExpectedPricingDto ExpectedPricing", checkoutRequest, StringComparison.Ordinal);

        Assert.DoesNotContain("public Guid OrderId", checkoutRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("public string OrderNumber", checkoutRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("UnitPrice", checkoutRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("Inventory", checkoutRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentStatus", checkoutRequest, StringComparison.Ordinal);

        Assert.Contains("public string? ItemsSnapshotJson", createSessionRequest, StringComparison.Ordinal);
        Assert.Contains("public decimal? CartTotal", createSessionRequest, StringComparison.Ordinal);
        Assert.Contains("public string? Currency", createSessionRequest, StringComparison.Ordinal);
        Assert.Contains("public string? ItemsSnapshotJson", updateSessionRequest, StringComparison.Ordinal);
        Assert.Contains("public decimal? CartTotal", updateSessionRequest, StringComparison.Ordinal);
        Assert.Contains("public string? Currency", updateSessionRequest, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutSessionApis_MustOverwriteClientSnapshotsWithLiveCartAuthority()
    {
        var sessionController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");

        Assert.Contains("var authoritativeCart = await LoadAuthoritativeCartAsync(", sessionController, StringComparison.Ordinal);
        Assert.Contains("BuildAuthoritativeItemsSnapshotJson(authoritativeCart)", sessionController, StringComparison.Ordinal);
        Assert.Contains("CartTotal = authoritativeCart.GrandTotal", sessionController, StringComparison.Ordinal);
        Assert.Contains("Currency = authoritativeCart.Currency", sessionController, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsSnapshotJson = request.ItemsSnapshotJson", sessionController, StringComparison.Ordinal);
        Assert.DoesNotContain("CartTotal = request.CartTotal", sessionController, StringComparison.Ordinal);
        Assert.DoesNotContain("Currency = request.Currency", sessionController, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorResponses_MustUseStandardEnvelopeAndCorrelationIds()
    {
        var checkoutController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");
        var sessionController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");

        Assert.Contains("new ApiErrorResponse(ex.ErrorCode, ex.Message, traceId)", checkoutController, StringComparison.Ordinal);
        Assert.Contains("new ApiErrorResponse(\"RECOVERY_TOKEN_MISSING\"", sessionController, StringComparison.Ordinal);
        Assert.Contains("ApiErrorResponse.TenantHeaderMissing(traceId)", sessionController, StringComparison.Ordinal);
        Assert.Contains("new ApiErrorResponse(\"SESSION_EXPIRED\"", sessionController, StringComparison.Ordinal);
        Assert.Contains("new ApiErrorResponse(\"SESSION_NOT_FOUND\"", sessionController, StringComparison.Ordinal);
        Assert.Contains("SESSION_CONVERTED", sessionController, StringComparison.Ordinal);
        Assert.Contains("RECOVERY_CART_REVALIDATION_FAILED", sessionController, StringComparison.Ordinal);
        Assert.DoesNotContain("new { error =", checkoutController, StringComparison.Ordinal);
        Assert.DoesNotContain("new { error =", sessionController, StringComparison.Ordinal);
    }

    [Fact]
    public void Controllers_MustNotOwnCheckoutBusinessDomainLogic()
    {
        var checkoutController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutController.cs");
        var sessionController = ReadRepoFile("PointOfSale.API/Controllers/CheckoutSessionController.cs");
        var combined = checkoutController + sessionController;

        Assert.DoesNotContain("DbContext", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductVariantStockRepository", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("ShippingCalculationService", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("TaxCalculationService", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("PromotionEngine", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("CouponValidationService", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateOrderAsync", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("ReserveInventory", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginTransaction", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void CertificationDocument_MustExistForPOSAdminCheckout2Apis()
    {
        var document = ReadRepoFile("docs/architecture/POSADMIN_CHECKOUT_2_ADMINISTRATION_STOREFRONT_APIS.md");

        Assert.Contains("# POSADMIN-CHECKOUT-2", document, StringComparison.Ordinal);
        Assert.Contains("API Certified: YES", document, StringComparison.Ordinal);
        Assert.Contains("Controllers Certified: YES", document, StringComparison.Ordinal);
        Assert.Contains("Runtime delegation preserved? **Yes.**", document, StringComparison.Ordinal);
    }

    private static string ExtractClass(string source, string className)
    {
        var marker = $"class {className}";
        var markerIndex = source.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            throw new InvalidOperationException($"Class {className} was not found.");
        }

        var bodyStart = source.IndexOf('{', markerIndex);
        if (bodyStart < 0)
        {
            throw new InvalidOperationException($"Class {className} has no body.");
        }

        var depth = 0;
        for (var i = bodyStart; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[bodyStart..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Class {className} body was not closed.");
    }

    private static string ReadRepoFile(string relativePath)
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (current.GetFiles("*.sln").Any() || current.GetDirectories("PointOfSale.API").Any())
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
