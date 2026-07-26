using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using PointOfSale.API.Services.Payments.Adapters;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.API.Services.Payments.ProviderContracts;
using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.Tests.Architecture;

public sealed class FawryCertificationClosure7Tests
{
    [Fact]
    public async Task HttpProviderError_MustDiscardRawProviderResponseBody()
    {
        var rawProviderBody = JsonSerializer.Serialize(new
        {
            statusCode = "9946",
            signature = "raw-provider-signature-secret",
            secureKey = "merchant-secure-key-secret",
            apiKey = "provider-api-key-secret",
            token = "provider-token-secret",
            authorization = "Bearer provider-authorization-secret",
            diagnostics = "internal-provider-diagnostics-secret"
        });
        var adapter = new FawryPaymentAdapter(new HttpClient(new StubHttpMessageHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(rawProviderBody, Encoding.UTF8, "application/json")
            })))
        {
            BaseAddress = new Uri("https://atfawry.fawrystaging.com/")
        });

        var result = await adapter.InitializePaymentAsync(CreateAction(), ValidCredentials());

        Assert.False(result.Succeeded);
        Assert.Null(result.Evidence);
        Assert.NotNull(result.Error);
        Assert.Equal(CanonicalProviderErrorType.Authentication, result.Error!.Type);
        Assert.Equal("FAWRY_HTTP_401", result.Error.Code);
        Assert.Equal("401", result.Error.Metadata["http_status"]);
        Assert.Equal(
            "Fawry returned a non-success HTTP status. Provider response body was discarded.",
            result.Error.Metadata["provider_diagnostic"]);

        var serialized = JsonSerializer.Serialize(result.Error);
        Assert.DoesNotContain(rawProviderBody, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-provider-signature-secret", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("merchant-secure-key-secret", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("provider-api-key-secret", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("provider-token-secret", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("provider-authorization-secret", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("internal-provider-diagnostics-secret", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectedEvidence_MustPreserveCanonicalErrorAndCorrelationWithoutRawProviderValues()
    {
        var action = CreateAction();
        var error = FawryProviderErrorTranslator.FromHttp(HttpStatusCode.BadRequest);

        var evidence = FawryExecutionFactory.ProviderErrorEvidence(action, error);

        Assert.Equal(EvidenceStatus.Rejected, evidence.Status);
        Assert.Equal(action.CorrelationId, evidence.CorrelationId);
        Assert.Equal("FAWRY_HTTP_400", evidence.Metadata.Values["provider_error_code"]);
        Assert.Equal(CanonicalProviderErrorType.Validation.ToString(), evidence.Metadata.Values["provider_error_type"]);
        Assert.Equal("400", evidence.Metadata.Values["http_status"]);
        Assert.Equal(
            "Fawry returned a non-success HTTP status. Provider response body was discarded.",
            evidence.Metadata.Values["provider_diagnostic"]);

        var serialized = JsonSerializer.Serialize(evidence);
        Assert.DoesNotContain("signature", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secureKey", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authorization", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw-provider", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdapterHttpErrorTranslation_MustNotReadOrRedactRawResponseBodies()
    {
        var adapter = ReadRepoFile("PointOfSale.API/Services/Payments/Adapters/FawryPaymentAdapter.cs");

        Assert.Contains("FawryProviderErrorTranslator.FromHttp(response.StatusCode)", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("SafeReadBodyAsync", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Redact(responseBody)", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadAsStringAsync(cancellationToken)", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("responseBody", adapter, StringComparison.Ordinal);
    }

    private static FawryPaymentCredentials ValidCredentials() => new("merchant-code", "api-key", "secure-key");

    private static PaymentAction CreateAction()
    {
        return PaymentActionFactory.Create(
            new PaymentActionId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            new PaymentSessionId(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")),
            new PaymentAttemptId(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")),
            PaymentActionType.ReferencePayment,
            PaymentActionStatus.Pending,
            new ActionReference("canonical-action-reference"),
            new ActionCorrelationId("correlation-closure-7"),
            DateTimeOffset.Parse("2026-07-14T11:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-07-14T11:00:00Z", CultureInfo.InvariantCulture),
            metadata: new PaymentActionMetadata(new Dictionary<string, string>
            {
                [FawryRequestMetadataKeys.Amount] = "123.45",
                [FawryRequestMetadataKeys.Currency] = "EGP",
                [FawryRequestMetadataKeys.CustomerMobile] = "01000000000",
                [FawryRequestMetadataKeys.CustomerEmail] = "buyer@example.test",
                [FawryRequestMetadataKeys.Description] = "Order ORDER-123",
                [FawryRequestMetadataKeys.Language] = "en-gb"
            }));
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
