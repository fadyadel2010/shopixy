using System.Globalization;
using System.Text.Json;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class FawryCertificationClosure2Tests
{
    [Fact]
    public void OfficialChargeSerialization_MustContainCurrentRequiredPayAtFawryFields()
    {
        var request = FawryPaymentRequestFactory.Create(
            CreateAction("PAY-20260715-deadbeef"),
            new FawryPaymentCredentials("merchant-code", "api-key", "secure-key"));

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request.OfficialRequest));
        var root = json.RootElement;

        Assert.Equal("merchant-code", root.GetProperty("merchantCode").GetString());
        Assert.Matches("^[0-9]{18}$", root.GetProperty("merchantRefNum").GetString()!);
        Assert.Equal("01000000000", root.GetProperty("customerMobile").GetString());
        Assert.Equal("buyer@example.test", root.GetProperty("customerEmail").GetString());
        Assert.Equal("PayAtFawry", root.GetProperty("paymentMethod").GetString());
        Assert.Equal("125.50", root.GetProperty("amount").GetString());
        Assert.Equal("EGP", root.GetProperty("currencyCode").GetString());
        Assert.Equal("Order ORDER-123", root.GetProperty("description").GetString());
        Assert.Equal("ar-eg", root.GetProperty("language").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("signature").GetString()));
        Assert.Single(root.GetProperty("chargeItems").EnumerateArray());
    }

    [Fact]
    public void MerchantReference_MustBeNumericStableAndBoundToMerchantPaymentReference()
    {
        var credentials = new FawryPaymentCredentials("merchant-code", "api-key", "secure-key");

        var first = FawryPaymentRequestFactory.Create(CreateAction("PAY-20260715-one"), credentials);
        var replay = FawryPaymentRequestFactory.Create(CreateAction("PAY-20260715-one"), credentials);
        var different = FawryPaymentRequestFactory.Create(CreateAction("PAY-20260715-two"), credentials);

        Assert.Equal(first.OfficialRequest.MerchantRefNum, replay.OfficialRequest.MerchantRefNum);
        Assert.NotEqual(first.OfficialRequest.MerchantRefNum, different.OfficialRequest.MerchantRefNum);
        Assert.Matches("^[0-9]{18}$", first.OfficialRequest.MerchantRefNum);
        Assert.Equal(first.OfficialRequest.MerchantRefNum, first.IdempotencyProjection.Value);
    }

    [Theory]
    [InlineData(FawryRequestMetadataKeys.CustomerMobile)]
    [InlineData(FawryRequestMetadataKeys.CustomerEmail)]
    [InlineData(FawryRequestMetadataKeys.Description)]
    [InlineData(FawryRequestMetadataKeys.Language)]
    public void RequiredFields_MustFailClosedBeforeProviderIo(string missingField)
    {
        var metadata = ValidMetadata("PAY-20260715-required");
        metadata.Remove(missingField);

        var exception = Assert.Throws<ArgumentException>(() =>
            FawryPaymentRequestFactory.Create(
                CreateAction(metadata),
                new FawryPaymentCredentials("merchant-code", "api-key", "secure-key")));

        Assert.Contains(missingField, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerAndLanguageMapping_MustNormalizeOnlyOfficialContractValues()
    {
        var request = FawryPaymentRequestFactory.Create(
            CreateAction("PAY-20260715-normalization"),
            new FawryPaymentCredentials("merchant-code", "api-key", "secure-key"));

        Assert.Equal("01000000000", request.OfficialRequest.CustomerMobile);
        Assert.Equal("buyer@example.test", request.OfficialRequest.CustomerEmail);
        Assert.Equal("ar-eg", request.OfficialRequest.Language);

        var invalid = ValidMetadata("PAY-20260715-invalid");
        invalid[FawryRequestMetadataKeys.Language] = "fr-fr";
        Assert.Throws<ArgumentException>(() =>
            FawryPaymentRequestFactory.Create(
                CreateAction(invalid),
                new FawryPaymentCredentials("merchant-code", "api-key", "secure-key")));
    }

    [Fact]
    public void ArchitectureGuard_MustKeepCorrectionInApplicationAndAdapterOnly()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var paymentService = Read(root, "PointOfSale.API/Services/Payments/PaymentService.cs");
        var applicationService = Read(root, "PointOfSale.API/Services/Payments/Fawry/FawryRuntimeApplicationService.cs");
        var adapter = Read(root, "PointOfSale.API/Services/Payments/Adapters/FawryPaymentAdapter.cs");
        var runtime = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        Assert.Contains("ResolveFawryCustomerContractAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("order.GuestPhone", paymentService, StringComparison.Ordinal);
        Assert.Contains("_dbContext.OnlineCustomers", paymentService, StringComparison.Ordinal);
        Assert.Contains("_dbContext.Customers", paymentService, StringComparison.Ordinal);
        Assert.Contains("CustomerMobile: customer.Mobile", paymentService, StringComparison.Ordinal);
        Assert.Contains("CustomerEmail: customer.Email", paymentService, StringComparison.Ordinal);
        Assert.Contains("Language: FawryDefaultLanguage", paymentService, StringComparison.Ordinal);
        Assert.Contains("FawryRequestMetadataKeys.Language", applicationService, StringComparison.Ordinal);
        Assert.Contains("FawryCustomerMobile.Normalize", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Fawry", runtime, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DbContext", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", adapter, StringComparison.Ordinal);
    }

    private static PaymentAction CreateAction(string merchantReference) =>
        CreateAction(ValidMetadata(merchantReference));

    private static PaymentAction CreateAction(IReadOnlyDictionary<string, string> metadata) =>
        PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            new PaymentSessionId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")),
            new PaymentAttemptId(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")),
            PaymentActionType.ReferencePayment,
            PaymentActionStatus.Pending,
            new ActionReference("PAY-20260715-action"),
            new ActionCorrelationId("fawry-certification-closure-2"),
            DateTimeOffset.Parse("2026-07-15T10:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-07-15T10:00:00Z", CultureInfo.InvariantCulture),
            metadata: new PaymentActionMetadata(metadata));

    private static Dictionary<string, string> ValidMetadata(string merchantReference) => new(StringComparer.Ordinal)
    {
        [FawryRequestMetadataKeys.Amount] = "125.5",
        [FawryRequestMetadataKeys.Currency] = "egp",
        [FawryRequestMetadataKeys.MerchantReference] = merchantReference,
        [FawryRequestMetadataKeys.CustomerMobile] = "+20 100 000 0000",
        [FawryRequestMetadataKeys.CustomerEmail] = " Buyer@Example.Test ",
        [FawryRequestMetadataKeys.PaymentMethod] = "PAYATFAWRY",
        [FawryRequestMetadataKeys.Description] = "Order ORDER-123",
        [FawryRequestMetadataKeys.Language] = "ar"
    };

    private static string Read(string root, string relativePath) =>
        File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

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
