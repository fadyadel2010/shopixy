using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class FawryCertificationClosure3Tests
{
    private static readonly FawryPaymentCredentials Credentials = new("merchant-code", "api-key", "secure-key");

    [Fact]
    public async Task ValidOfficialSignedResponse_MustBeAcceptedAsRecordedProviderEvidence()
    {
        var result = await ExecuteAsync(request => SignedResponse(request));

        Assert.True(result.Succeeded);
        Assert.Null(result.Error);
        Assert.NotNull(result.Evidence);
        Assert.Equal(EvidenceStatus.Recorded, result.Evidence!.Status);
        Assert.Equal("963455678", result.Evidence.Reference.Value);
        Assert.Equal("PENDING", result.Evidence.Metadata.Values["provider_status"]);
        Assert.Equal("125.50", result.Evidence.Metadata.Values["amount"]);
    }

    [Fact]
    public async Task SignatureMismatch_MustFailClosedBeforeProviderEvidence()
    {
        var result = await ExecuteAsync(request => SignedResponse(request) with
        {
            Signature = new string('0', 64)
        });

        AssertRejected(result, "FAWRY_RESPONSE_SIGNATURE_INVALID", CanonicalProviderErrorType.Authentication);
    }

    [Fact]
    public async Task AmountMismatch_WithValidProviderSignature_MustFailClosed()
    {
        var result = await ExecuteAsync(request => SignedResponse(
            request,
            response => response with { PaymentAmount = "126.50", OrderAmount = "126.50" }));

        AssertRejected(result, "FAWRY_RESPONSE_AMOUNT_MISMATCH");
    }

    [Fact]
    public async Task CurrencyMismatch_WhenProviderReturnsCurrency_MustFailClosed()
    {
        var result = await ExecuteAsync(request => SignedResponse(
            request,
            response => response with { CurrencyCode = "USD" }));

        AssertRejected(result, "FAWRY_RESPONSE_CURRENCY_MISMATCH");
    }

    [Fact]
    public async Task MerchantReferenceMismatch_WithValidProviderSignature_MustFailClosed()
    {
        var result = await ExecuteAsync(request => SignedResponse(
            request,
            response => response with { MerchantRefNumber = "999999999999999999" }));

        AssertRejected(result, "FAWRY_RESPONSE_MERCHANT_REFERENCE_MISMATCH");
    }

    [Fact]
    public async Task MerchantIdentityMismatch_WhenProviderReturnsMerchantCode_MustFailClosed()
    {
        var result = await ExecuteAsync(request => SignedResponse(
            request,
            response => response with { MerchantCode = "different-merchant" }));

        AssertRejected(result, "FAWRY_RESPONSE_MERCHANT_IDENTITY_MISMATCH", CanonicalProviderErrorType.Authentication);
    }

    [Fact]
    public async Task FawryReferenceMissing_MustFailClosed()
    {
        var result = await ExecuteAsync(request => SignedResponse(
            request,
            response => response with { ReferenceNumber = null }));

        AssertRejected(result, "FAWRY_RESPONSE_REFERENCE_MISSING");
    }

    [Theory]
    [InlineData("type", "FAWRY_RESPONSE_TYPE_INVALID")]
    [InlineData("statusCode", "FAWRY_RESPONSE_STATUS_CODE_INVALID")]
    [InlineData("merchantRefNumber", "FAWRY_RESPONSE_MERCHANT_REFERENCE_MISSING")]
    [InlineData("paymentAmount", "FAWRY_RESPONSE_AMOUNT_INVALID")]
    [InlineData("orderAmount", "FAWRY_RESPONSE_AMOUNT_INVALID")]
    [InlineData("paymentMethod", "FAWRY_RESPONSE_PAYMENT_METHOD_MISMATCH")]
    [InlineData("orderStatus", "FAWRY_RESPONSE_ORDER_STATUS_INVALID")]
    [InlineData("signature", "FAWRY_RESPONSE_SIGNATURE_MISSING")]
    public async Task MissingRequiredOfficialField_MustFailClosed(string field, string expectedCode)
    {
        var result = await ExecuteAsync(request =>
        {
            var response = SignedResponse(request);
            return field switch
            {
                "type" => response with { Type = null },
                "statusCode" => response with { StatusCode = null },
                "merchantRefNumber" => response with { MerchantRefNumber = null },
                "paymentAmount" => response with { PaymentAmount = null },
                "orderAmount" => response with { OrderAmount = null },
                "paymentMethod" => response with { PaymentMethod = null },
                "orderStatus" => response with { OrderStatus = null },
                "signature" => response with { Signature = null },
                _ => throw new InvalidOperationException($"Unknown field '{field}'.")
            };
        });

        AssertRejected(
            result,
            expectedCode,
            expectedCode.Contains("SIGNATURE", StringComparison.Ordinal)
                ? CanonicalProviderErrorType.Authentication
                : CanonicalProviderErrorType.Validation);
    }

    [Fact]
    public async Task MalformedResponse_MustFailClosed()
    {
        var adapter = CreateAdapter(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ not-json", Encoding.UTF8, "application/json")
        }));

        var result = await adapter.InitializePaymentAsync(CreateAction(), Credentials);

        AssertRejected(result, "FAWRY_RESPONSE_INVALID");
    }

    [Theory]
    [InlineData("COMPLETED", "FAWRY_RESPONSE_ORDER_STATUS_INVALID")]
    [InlineData("SUCCESS", "FAWRY_RESPONSE_STATUS_CODE_INVALID")]
    public async Task UnexpectedProviderValue_MustFailClosed(string value, string expectedCode)
    {
        var result = await ExecuteAsync(request =>
        {
            var response = SignedResponse(request);
            return expectedCode == "FAWRY_RESPONSE_ORDER_STATUS_INVALID"
                ? Sign(response with { OrderStatus = value })
                : response with { StatusCode = value };
        });

        AssertRejected(result, expectedCode);
    }

    [Fact]
    public async Task IntegrityFailure_MustBecomeRejectedEvidenceCanonicalErrorAndRuntimeFailure()
    {
        var action = CreateAction();
        var adapter = CreateAdapter(async requestMessage =>
        {
            var request = await DeserializeRequestAsync(requestMessage);
            return JsonResponse(SignedResponse(
                request,
                response => response with { PaymentAmount = "126.50", OrderAmount = "126.50" }));
        });
        var bridge = new FawryRuntimeBridge(adapter, new PaymentRuntimeOrchestrator());
        var executionRequest = FawryExecutionFactory.CreateRequest(
            action,
            CreateSession(action),
            Credentials,
            DateTimeOffset.Parse("2026-07-15T10:02:00Z", CultureInfo.InvariantCulture));

        var result = await bridge.ExecuteAsync(executionRequest);

        Assert.Equal(FawryExecutionStatus.ProviderFailed, result.ExecutionStatus);
        Assert.Equal("FAWRY_RESPONSE_AMOUNT_MISMATCH", result.ProviderError!.Code);
        Assert.Equal(EvidenceStatus.Rejected, result.ProviderEvidence.Status);
        Assert.Equal("FAWRY_RESPONSE_AMOUNT_MISMATCH", result.ProviderEvidence.Metadata.Values["provider_error_code"]);
        Assert.Equal(RuntimeOverallStatus.Failed, result.RuntimeResult.OverallStatus);
        Assert.Equal(FailureStage.Admission, result.RuntimeResult.Failure!.FailureStage);
    }

    [Fact]
    public void ArchitectureGuard_MustKeepResponseProtocolInsideAdapterAndRuntimeFrozen()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var adapter = Read(root, "PointOfSale.API/Services/Payments/Adapters/FawryPaymentAdapter.cs");
        var application = Read(root, "PointOfSale.API/Services/Payments/Fawry/FawryRuntimeApplicationService.cs");
        var bridge = Read(root, "PointOfSale.API/Services/Payments/Fawry/FawryRuntimeBridge.cs");
        var runtime = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    Path.Combine(root, "PointOfSale.Core", "Payments", "Runtime"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        Assert.Contains("FawryChargeResponseIntegrityValidator.Validate", adapter, StringComparison.Ordinal);
        Assert.Contains("CryptographicOperations.FixedTimeEquals", adapter, StringComparison.Ordinal);
        Assert.Contains("FAWRY_RESPONSE_MERCHANT_REFERENCE_MISMATCH", adapter, StringComparison.Ordinal);
        Assert.Contains("FAWRY_RESPONSE_AMOUNT_MISMATCH", adapter, StringComparison.Ordinal);
        Assert.Contains("FAWRY_RESPONSE_CURRENCY_MISMATCH", adapter, StringComparison.Ordinal);
        Assert.Contains("FawryExecutionFactory.ProviderErrorEvidence", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("FawryChargeResponse", application, StringComparison.Ordinal);
        Assert.DoesNotContain("Fawry", runtime, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DbContext", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentLifecycleService", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentRuntimeOrchestrator", adapter, StringComparison.Ordinal);
    }

    private static async Task<FawryPaymentAdapterResult> ExecuteAsync(
        Func<FawryChargeRequest, FawryChargeResponse> responseFactory)
    {
        var adapter = CreateAdapter(async requestMessage =>
        {
            var request = await DeserializeRequestAsync(requestMessage);
            return JsonResponse(responseFactory(request));
        });

        return await adapter.InitializePaymentAsync(CreateAction(), Credentials);
    }

    private static FawryPaymentAdapter CreateAdapter(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
    {
        return new FawryPaymentAdapter(new HttpClient(new StubHttpMessageHandler(handler))
        {
            BaseAddress = new Uri("https://atfawry.fawrystaging.com/")
        });
    }

    private static FawryChargeResponse SignedResponse(
        FawryChargeRequest request,
        Func<FawryChargeResponse, FawryChargeResponse>? mutate = null)
    {
        var response = new FawryChargeResponse
        {
            Type = "ChargeResponse",
            StatusCode = "200",
            ReferenceNumber = "963455678",
            MerchantRefNumber = request.MerchantRefNum,
            OrderAmount = request.Amount,
            PaymentAmount = request.Amount,
            FawryFees = "1.00",
            PaymentMethod = request.PaymentMethod,
            OrderStatus = "PENDING",
            PaymentTime = "1607879720568",
            CustomerMobile = request.CustomerMobile,
            CustomerMail = request.CustomerEmail,
            CustomerProfileId = request.CustomerProfileId
        };
        response = mutate?.Invoke(response) ?? response;
        return Sign(response);
    }

    private static FawryChargeResponse Sign(FawryChargeResponse response)
    {
        var source = string.Concat(
            response.ReferenceNumber,
            response.MerchantRefNumber,
            Amount(response.PaymentAmount),
            Amount(response.OrderAmount),
            response.OrderStatus,
            response.PaymentMethod,
            OptionalAmount(response.FawryFees),
            OptionalAmount(response.ShippingFees),
            response.AuthNumber,
            response.CustomerMail,
            response.CustomerMobile,
            Credentials.SecureKey);
        var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
        return response with { Signature = signature };
    }

    private static string Amount(string? value) =>
        decimal.Parse(value!, NumberStyles.Number, CultureInfo.InvariantCulture).ToString("0.00", CultureInfo.InvariantCulture);

    private static string OptionalAmount(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : Amount(value);

    private static void AssertRejected(
        FawryPaymentAdapterResult result,
        string expectedCode,
        CanonicalProviderErrorType expectedType = CanonicalProviderErrorType.Validation)
    {
        Assert.False(result.Succeeded);
        Assert.Null(result.Evidence);
        Assert.NotNull(result.Error);
        Assert.Equal(expectedCode, result.Error!.Code);
        Assert.Equal(expectedType, result.Error.Type);
    }

    private static PaymentAction CreateAction() =>
        PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            new PaymentSessionId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")),
            new PaymentAttemptId(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")),
            PaymentActionType.ReferencePayment,
            PaymentActionStatus.Pending,
            new ActionReference("PAY-20260715-closure-3"),
            new ActionCorrelationId("fawry-certification-closure-3"),
            DateTimeOffset.Parse("2026-07-15T10:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-07-15T10:01:00Z", CultureInfo.InvariantCulture),
            metadata: new PaymentActionMetadata(new Dictionary<string, string>
            {
                [FawryRequestMetadataKeys.Amount] = "125.50",
                [FawryRequestMetadataKeys.Currency] = "EGP",
                [FawryRequestMetadataKeys.MerchantReference] = "PAY-20260715-closure-3",
                [FawryRequestMetadataKeys.CustomerReference] = "777777",
                [FawryRequestMetadataKeys.CustomerMobile] = "01000000000",
                [FawryRequestMetadataKeys.CustomerEmail] = "buyer@example.test",
                [FawryRequestMetadataKeys.PaymentMethod] = "PayAtFawry",
                [FawryRequestMetadataKeys.Description] = "Order ORDER-123",
                [FawryRequestMetadataKeys.Language] = "en-gb"
            }));

    private static PaymentSession CreateSession(PaymentAction action)
    {
        var attempt = PaymentAttemptFactory.Create(
            action.AttemptId,
            action.SessionId,
            new AttemptNumber(1),
            action.CreatedUtc,
            new ProviderReference("fawry"),
            PaymentStatus.Pending,
            new FailureReason(string.Empty),
            0);
        return PaymentSessionFactory.Create(
            action.SessionId,
            new PaymentId(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd")),
            Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            new PaymentReference("FAWRY"),
            new Money(125.50m, new Currency("EGP")),
            PaymentStatus.Pending,
            action.CreatedUtc,
            action.LastUpdatedUtc,
            attempts: [attempt]);
    }

    private static async Task<FawryChargeRequest> DeserializeRequestAsync(HttpRequestMessage request)
    {
        var body = await request.Content!.ReadAsStringAsync();
        return JsonSerializer.Deserialize<FawryChargeRequest>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static HttpResponseMessage JsonResponse(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Encoding.UTF8,
            "application/json")
    };

    private static string Read(string root, string relativePath) =>
        File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string DiscoverSolutionRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request);
    }
}
