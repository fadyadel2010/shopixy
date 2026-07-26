using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class FawryProviderAdapterCertificationTests
{
    [Fact]
    public void CanonicalRequestMapping_MustCreateOfficialFawryChargeRequest()
    {
        var action = CreateAction(new Dictionary<string, string>
        {
            [FawryRequestMetadataKeys.Amount] = "123.45",
            [FawryRequestMetadataKeys.Currency] = "egp",
            [FawryRequestMetadataKeys.MerchantReference] = "PAY-20260715-deadbeef",
            [FawryRequestMetadataKeys.CustomerReference] = "456",
            [FawryRequestMetadataKeys.CustomerMobile] = "+201000000000",
            [FawryRequestMetadataKeys.CustomerEmail] = "buyer@example.test",
            [FawryRequestMetadataKeys.PaymentMethod] = "PAY_AT_FAWRY",
            [FawryRequestMetadataKeys.Description] = "Order ORDER-123",
            [FawryRequestMetadataKeys.Language] = "en-gb",
            [FawryRequestMetadataKeys.ExpirationUtc] = "2026-07-15T10:30:00Z",
            [FawryRequestMetadataKeys.RedirectUrl] = "https://pos.example.test/fawry/return",
            [FawryRequestMetadataKeys.OrderWebHookUrl] = "https://pos.example.test/webhooks/fawry/order"
        });

        var request = FawryPaymentRequestFactory.Create(action, ValidCredentials());

        Assert.Equal("merchant-code", request.OfficialRequest.MerchantCode);
        Assert.Matches("^[0-9]{18}$", request.OfficialRequest.MerchantRefNum);
        Assert.Equal("456", request.OfficialRequest.CustomerProfileId);
        Assert.Equal("01000000000", request.OfficialRequest.CustomerMobile);
        Assert.Equal("buyer@example.test", request.OfficialRequest.CustomerEmail);
        Assert.Equal("123.45", request.OfficialRequest.Amount);
        Assert.Equal("EGP", request.OfficialRequest.CurrencyCode);
        Assert.Equal("PayAtFawry", request.OfficialRequest.PaymentMethod);
        Assert.Equal("en-gb", request.OfficialRequest.Language);
        Assert.Equal("1784111400000", request.OfficialRequest.PaymentExpiry);
        Assert.Equal("https://pos.example.test/fawry/return", request.OfficialRequest.ReturnUrl);
        Assert.Equal("https://pos.example.test/webhooks/fawry/order", request.OfficialRequest.OrderWebHookUrl);
        Assert.Single(request.OfficialRequest.ChargeItems);
        Assert.Equal(request.MerchantReference.Value, request.IdempotencyProjection.Value);
        Assert.Equal(ExpectedSignature("merchant-code", request.OfficialRequest.MerchantRefNum, "456", "PayAtFawry", "123.45", "secure-key"), request.Signature);
    }

    [Fact]
    public void IdempotencyProjection_MustBeDeterministicForSameCanonicalAction()
    {
        var first = FawryPaymentRequestFactory.Create(CreateAction(), ValidCredentials());
        var second = FawryPaymentRequestFactory.Create(CreateAction(), ValidCredentials());

        Assert.Equal(first.IdempotencyProjection, second.IdempotencyProjection);
        Assert.Equal(first.OfficialRequest.MerchantRefNum, second.OfficialRequest.MerchantRefNum);
        Assert.Matches("^[0-9]{18}$", first.OfficialRequest.MerchantRefNum);
    }

    [Fact]
    public void CanonicalResponseMapping_MustTranslateOfficialResponseToProviderEvidence()
    {
        var action = CreateAction();
        var response = AcceptedResponse();

        var evidence = FawryEvidenceFactory.FromChargeResponse(action, response);

        Assert.Equal(action.Id, evidence.ActionId);
        Assert.Equal(EvidenceType.ProviderNotification, evidence.Type);
        Assert.Equal(EvidenceStatus.Recorded, evidence.Status);
        Assert.Equal("fawry", evidence.Source.Value);
        Assert.Equal("963852741", evidence.Reference.Value);
        Assert.Equal(action.CorrelationId, evidence.CorrelationId);
        Assert.Equal("fawry:charge:963852741", evidence.PayloadReference.Value);
        Assert.Equal("AwaitingCustomer", evidence.Metadata.Values["canonical_status"]);
        Assert.Equal("PENDING", evidence.Metadata.Values["provider_status"]);
        Assert.Equal("https://atfawry.example.test/pay/963852741", evidence.Metadata.Values["payment_url"]);
    }

    [Fact]
    public async Task OfficialEndpointUsage_MustPostToChargeEndpointAndReturnEvidence()
    {
        FawryChargeRequest? capturedRequest = null;
        var handler = new StubHttpMessageHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://atfawry.fawrystaging.com/ECommerceWeb/Fawry/payments/charge", request.RequestUri!.ToString());
            capturedRequest = JsonSerializer.Deserialize<FawryChargeRequest>(
                await request.Content!.ReadAsStringAsync(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.NotNull(capturedRequest);
            return JsonResponse(HttpStatusCode.OK, AcceptedResponse(capturedRequest!, ValidCredentials()));
        });
        var adapter = new FawryPaymentAdapter(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://atfawry.fawrystaging.com/")
        });

        var result = await adapter.InitializePaymentAsync(CreateAction(), ValidCredentials());

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Evidence);
        Assert.NotNull(result.Request);
        Assert.NotNull(capturedRequest);
        Assert.Equal(result.Request!.OfficialRequest.MerchantRefNum, capturedRequest!.MerchantRefNum);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ErrorTranslation_MustReturnCanonicalProviderErrorWithoutLeakingException()
    {
        var adapter = new FawryPaymentAdapter(new HttpClient(new StubHttpMessageHandler(_ =>
            Task.FromResult(JsonResponse(HttpStatusCode.Unauthorized, new { statusCode = "AUTH_FAILED", signature = "secret" }))))
        {
            BaseAddress = new Uri("https://atfawry.fawrystaging.com/")
        });

        var result = await adapter.InitializePaymentAsync(CreateAction(), ValidCredentials());

        Assert.False(result.Succeeded);
        Assert.Null(result.Evidence);
        Assert.NotNull(result.Error);
        Assert.Equal(CanonicalProviderErrorType.Authentication, result.Error!.Type);
        Assert.Equal("FAWRY_HTTP_401", result.Error.Code);
        Assert.Equal("fawry", result.Error.Metadata["provider"]);
        Assert.Equal("Fawry returned a non-success HTTP status. Provider response body was discarded.", result.Error.Metadata["provider_diagnostic"]);
        Assert.DoesNotContain("secure-key", JsonSerializer.Serialize(result.Error), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signature", JsonSerializer.Serialize(result.Error), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(result.Error), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ArchitectureIsolation_MustKeepAdapterFreeOfRuntimeMutationSqlConfigurationAndLogging()
    {
        var adapter = ReadRepoFile("PointOfSale.API/Services/Payments/Adapters/FawryPaymentAdapter.cs");
        var runtimeFiles = Directory.GetFiles(
            Path.Combine(DiscoverSolutionRoot(AppContext.BaseDirectory), "PointOfSale.Core", "Payments", "Runtime"),
            "*.cs",
            SearchOption.AllDirectories);

        Assert.Contains("public interface IFawryPaymentAdapter", adapter, StringComparison.Ordinal);
        Assert.Contains("HttpClient", adapter, StringComparison.Ordinal);
        Assert.Contains("ECommerceWeb/Fawry/payments/charge", adapter, StringComparison.Ordinal);
        Assert.Contains("ProviderEvidenceFactory.Create", adapter, StringComparison.Ordinal);
        Assert.Contains("CanonicalProviderError", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("IConfiguration", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("IOptions", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("DbContext", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ExecuteSql", adapter, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ILogger", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PointOfSale.API.Services.Payments.Stripe", adapter, StringComparison.Ordinal);

        foreach (var runtimeFile in runtimeFiles)
        {
            var source = File.ReadAllText(runtimeFile);
            Assert.DoesNotContain("Fawry", source, StringComparison.Ordinal);
            Assert.DoesNotContain("fawry", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("atfawry", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static FawryPaymentCredentials ValidCredentials() => new("merchant-code", "api-key", "secure-key");

    private static FawryChargeResponse AcceptedResponse(
        FawryChargeRequest? request = null,
        FawryPaymentCredentials? credentials = null)
    {
        request ??= FawryPaymentRequestFactory.Create(CreateAction(), ValidCredentials()).OfficialRequest;
        credentials ??= ValidCredentials();
        var response = new FawryChargeResponse
        {
            Type = "ChargeResponse",
            StatusCode = "200",
            MerchantRefNumber = request.MerchantRefNum,
            ReferenceNumber = "963852741",
            OrderStatus = "PENDING",
            PaymentAmount = request.Amount,
            OrderAmount = request.Amount,
            FawryFees = "1.00",
            CurrencyCode = request.CurrencyCode,
            PaymentMethod = request.PaymentMethod,
            PaymentTime = "1720958400000",
            CustomerMobile = request.CustomerMobile,
            CustomerMail = request.CustomerEmail,
            CustomerProfileId = request.CustomerProfileId,
            ExpirationTime = "2026-07-15T10:30:00Z",
            PaymentUrl = "https://atfawry.example.test/pay/963852741"
        };

        return response with
        {
            Signature = ExpectedResponseSignature(response, credentials.SecureKey)
        };
    }

    private static PaymentAction CreateAction(IReadOnlyDictionary<string, string>? metadata = null)
    {
        var values = metadata ?? new Dictionary<string, string>
        {
            [FawryRequestMetadataKeys.Amount] = "123.45",
            [FawryRequestMetadataKeys.Currency] = "EGP",
            [FawryRequestMetadataKeys.CustomerMobile] = "01000000000",
            [FawryRequestMetadataKeys.CustomerEmail] = "buyer@example.test",
            [FawryRequestMetadataKeys.Description] = "Order ORDER-123",
            [FawryRequestMetadataKeys.Language] = "en-gb"
        };

        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            new PaymentSessionId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")),
            new PaymentAttemptId(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")),
            PaymentActionType.ReferencePayment,
            PaymentActionStatus.Pending,
            new ActionReference("canonical-action-reference"),
            new ActionCorrelationId("correlation-123"),
            DateTimeOffset.Parse("2026-07-14T11:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-07-14T11:00:00Z", CultureInfo.InvariantCulture),
            metadata: new PaymentActionMetadata(values));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                Encoding.UTF8,
                "application/json")
        };
    }

    private static string ExpectedSignature(
        string merchantCode,
        string merchantRefNum,
        string customerProfileId,
        string paymentMethod,
        string amount,
        string secureKey)
    {
        var source = string.Concat(merchantCode, merchantRefNum, customerProfileId, paymentMethod, amount, secureKey);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }

    private static string ExpectedResponseSignature(FawryChargeResponse response, string secureKey)
    {
        var source = string.Concat(
            response.ReferenceNumber,
            response.MerchantRefNumber,
            decimal.Parse(response.PaymentAmount!, CultureInfo.InvariantCulture).ToString("0.00", CultureInfo.InvariantCulture),
            decimal.Parse(response.OrderAmount!, CultureInfo.InvariantCulture).ToString("0.00", CultureInfo.InvariantCulture),
            response.OrderStatus,
            response.PaymentMethod,
            decimal.Parse(response.FawryFees!, CultureInfo.InvariantCulture).ToString("0.00", CultureInfo.InvariantCulture),
            response.ShippingFees,
            response.AuthNumber,
            response.CustomerMail,
            response.CustomerMobile,
            secureKey);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
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

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }
}
