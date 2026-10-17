using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Infrastructure;

public sealed class MylerzOfficialContractCompatibilityTests
{
    private const string ContractUnavailable = MylerzAdapter.ContractUnavailableErrorCode;

    [Fact]
    public async Task CreateShipment_WithoutMerchantQualification_FailsBeforeOutboundCall()
    {
        var handler = new RecordingHandler();
        var adapter = CreateAdapter(handler);

        var result = await adapter.CreateShipmentAsync(CreateShipmentRequest(), Credentials());

        Assert.False(result.Success);
        Assert.Equal(ContractUnavailable, result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UncertainCreationLookup_IsUnsupportedWithoutOutboundCall()
    {
        var handler = new RecordingHandler();
        var adapter = CreateAdapter(handler);
        Assert.Null(await adapter.GetShipmentByCarrierRequestIdAsync("ship-original-request", Credentials()));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Quote_WithoutMerchantBillingQualification_FailsBeforeOutboundCall()
    {
        var handler = new RecordingHandler();
        var adapter = CreateAdapter(handler);

        var result = await adapter.GetQuoteAsync(QuoteRequest(), Credentials());

        Assert.False(result.Success);
        Assert.Equal(ContractUnavailable, result.ErrorCode);
        Assert.Empty(result.Quotes);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Tracking_WithoutMerchantStatusQualification_FailsBeforeOutboundCall()
    {
        var handler = new RecordingHandler();
        var adapter = CreateAdapter(handler);

        var result = await adapter.GetTrackingAsync("track-1", Credentials());

        Assert.False(result.Success);
        Assert.Equal(ContractUnavailable, result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Cancellation_WithoutOfficialPublicContract_FailsBeforeOutboundCall()
    {
        var handler = new RecordingHandler();
        var adapter = CreateAdapter(handler);

        var result = await adapter.CancelShipmentAsync("track-1", Credentials());

        Assert.False(result.Success);
        Assert.Equal(ContractUnavailable, result.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CredentialValidation_RequiresTheDocumentedTokenResponse_NotJustHttp200()
    {
        var handler = new RecordingHandler();
        var adapter = CreateAdapter(handler);

        var result = await adapter.ValidateCredentialsAsync(Credentials());

        Assert.False(result.Success);
        Assert.Equal("MYLERZ_TOKEN_RESPONSE_SCHEMA_UNQUALIFIED", result.ErrorCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void Webhook_WithoutOfficialPublicContract_IsNotAdmitted()
    {
        var handler = new RecordingHandler();
        var adapter = CreateAdapter(handler);

        var result = adapter.ParseWebhook(
            """
            {
              "tracking_number": "track-1",
              "status": "delivered"
            }
            """,
            signature: null,
            webhookSecret: null);

        Assert.Null(result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void ServiceLevels_WithoutOfficialPublicContract_AreNotAdvertisedByAdapter()
    {
        var adapter = CreateAdapter(new RecordingHandler());

        Assert.Empty(adapter.GetSupportedServiceLevels());
    }

    private static MylerzAdapter CreateAdapter(RecordingHandler handler)
    {
        var client = new HttpClient(handler);
        return new MylerzAdapter(
            new TestHttpClientFactory(client),
            NullLogger<MylerzAdapter>.Instance);
    }

    private static CarrierCredentials Credentials() =>
        new()
        {
            ApiKey = "test-api-key",
            ApiSecret = "test-api-secret",
            AccountId = "merchant-1",
            UseSandbox = false
        };

    private static ShippingQuoteRequest QuoteRequest() =>
        new()
        {
            OriginCity = "Cairo",
            OriginCountry = "EG",
            DestinationCity = "Cairo",
            DestinationCountry = "EG",
            WeightKg = 1,
            PackageCount = 1
        };

    private static CreateShipmentRequest CreateShipmentRequest() =>
        new()
        {
            InternalShipmentId = "shipment-1",
            OrderNumber = "order-1",
            OriginName = "Origin",
            OriginAddress = "Origin address",
            OriginCity = "Cairo",
            OriginCountry = "EG",
            OriginPhone = "01000000000",
            RecipientName = "Recipient",
            RecipientAddress = "Recipient address",
            RecipientCity = "Cairo",
            RecipientCountry = "EG",
            RecipientPhone = "01000000001"
        };

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }
}
