using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Infrastructure;

public sealed class FedExTransportRuntimeTests
{
    [Fact]
    public async Task SendJsonAsync_UsesNamedHttpClientInjectsBearerAndCorrelationHeaders()
    {
        var handler = new RecordingHandler("""{"transactionId":"tx-1"}""");
        var factory = new RecordingHttpClientFactory(new HttpClient(handler));
        var oauth = new StubFedExOAuthClient("token-1");
        var client = CreateClient(factory, oauth);
        var request = TransportRequest(FedExOptions.TrackByTrackingNumberPath, TrackingRequest(), correlationId: "corr-1");

        var result = await client.SendJsonAsync(request);

        Assert.True(result.Success);
        Assert.Equal("FedEx", factory.Names.Single());
        Assert.Equal("Bearer", handler.Authorization?.Scheme);
        Assert.Equal("token-1", handler.Authorization?.Parameter);
        Assert.Equal("application/json", handler.Accept);
        Assert.Equal("application/json", handler.ContentType);
        Assert.Equal("corr-1", handler.CorrelationId);
        Assert.Equal("https://apis-sandbox.fedex.com/track/v1/trackingnumbers", handler.RequestUri?.ToString());
        Assert.Equal("""{"includeDetailedScans":false,"trackingInfo":[{"trackingNumberInfo":{"trackingNumber":"123456789012"}}]}""", handler.RequestBody);
        Assert.Equal("""{"transactionId":"tx-1"}""", result.Response?.RawJson);
    }

    [Fact]
    public async Task SendJsonAsync_CapturesHttpStatusHeadersRawJsonAndCorrelationMetadataWithoutAdmission()
    {
        var handler = new RecordingHandler(
            """{"errors":[{"code":"BAD.REQUEST","message":"provider text"}]}""",
            HttpStatusCode.BadRequest,
            ("transaction-id", "fedex-transaction"));
        var client = CreateClient(new RecordingHttpClientFactory(new HttpClient(handler)), new StubFedExOAuthClient("token-1"));

        var result = await client.SendJsonAsync(TransportRequest(FedExOptions.TrackByTrackingNumberPath, TrackingRequest()));

        Assert.False(result.Success);
        Assert.Equal("FEDEX_TRANSPORT_HTTP_400", result.Error?.ErrorCode);
        Assert.False(result.Error?.IsTransient);
        Assert.Equal(HttpStatusCode.BadRequest, result.Response?.StatusCode);
        Assert.Equal("""{"errors":[{"code":"BAD.REQUEST","message":"provider text"}]}""", result.Response?.RawJson);
        Assert.Equal("fedex-transaction", result.Response?.Headers["transaction-id"].Single());
        Assert.False(string.IsNullOrWhiteSpace(result.Response?.CorrelationId));
    }

    [Fact]
    public async Task SendJsonAsync_RetriesOnlyTransientHttpFailures()
    {
        var transientHandler = new QueueingHandler(
            ("""{"errors":[{"code":"TEMP"}]}""", HttpStatusCode.ServiceUnavailable),
            ("""{"transactionId":"tx-1"}""", HttpStatusCode.OK));
        var transientClient = CreateClient(new RecordingHttpClientFactory(new HttpClient(transientHandler)), new StubFedExOAuthClient("token-1"));

        var transient = await transientClient.SendJsonAsync(TransportRequest(FedExOptions.TrackByTrackingNumberPath, TrackingRequest()));

        var badRequestHandler = new QueueingHandler(
            ("""{"errors":[{"code":"BAD"}]}""", HttpStatusCode.BadRequest),
            ("""{"transactionId":"should-not-run"}""", HttpStatusCode.OK));
        var badRequestClient = CreateClient(new RecordingHttpClientFactory(new HttpClient(badRequestHandler)), new StubFedExOAuthClient("token-1"));

        var badRequest = await badRequestClient.SendJsonAsync(TransportRequest(FedExOptions.TrackByTrackingNumberPath, TrackingRequest()));

        Assert.True(transient.Success);
        Assert.Equal(2, transientHandler.CallCount);
        Assert.Equal(1, transient.Response?.RetryCount);
        Assert.False(badRequest.Success);
        Assert.Equal(1, badRequestHandler.CallCount);
        Assert.Equal("FEDEX_TRANSPORT_HTTP_400", badRequest.Error?.ErrorCode);
    }

    [Fact]
    public async Task SendJsonAsync_RefreshesTokenOnceAfterUnauthorized()
    {
        var handler = new QueueingHandler(
            ("""{"errors":[{"code":"NOT.AUTHENTICATED"}]}""", HttpStatusCode.Unauthorized),
            ("""{"transactionId":"tx-1"}""", HttpStatusCode.OK));
        var oauth = new StubFedExOAuthClient("expired-token", "fresh-token");
        var client = CreateClient(new RecordingHttpClientFactory(new HttpClient(handler)), oauth);

        var result = await client.SendJsonAsync(TransportRequest(FedExOptions.TrackByTrackingNumberPath, TrackingRequest()));

        Assert.True(result.Success);
        Assert.Equal(2, handler.CallCount);
        Assert.Equal(1, oauth.AuthorizationCalls);
        Assert.Equal(1, oauth.RefreshCalls);
        Assert.Equal("fresh-token", handler.AuthorizationParameters.Last());
    }

    [Fact]
    public async Task SendJsonAsync_NormalizesTimeoutButHonorsCallerCancellation()
    {
        var timeoutHandler = new ThrowingHandler(new TaskCanceledException("simulated timeout"));
        var timeoutClient = CreateClient(new RecordingHttpClientFactory(new HttpClient(timeoutHandler)), new StubFedExOAuthClient("token-1"));

        var timeout = await timeoutClient.SendJsonAsync(TransportRequest(FedExOptions.TrackByTrackingNumberPath, TrackingRequest()));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancellationClient = CreateClient(
            new RecordingHttpClientFactory(new HttpClient(new RecordingHandler("""{}"""))),
            new StubFedExOAuthClient("token-1"));

        Assert.False(timeout.Success);
        Assert.Equal("FEDEX_TRANSPORT_TIMEOUT", timeout.Error?.ErrorCode);
        Assert.True(timeout.Error?.IsTransient);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancellationClient.SendJsonAsync(TransportRequest(FedExOptions.TrackByTrackingNumberPath, TrackingRequest()), cts.Token));
    }

    [Fact]
    public async Task SendJsonAsync_RejectsUncertifiedDtosBeforeHttpExecution()
    {
        var handler = new RecordingHandler("""{}""");
        var client = CreateClient(new RecordingHttpClientFactory(new HttpClient(handler)), new StubFedExOAuthClient("token-1"));
        var request = new FedExTransportRequest<object>(
            HttpMethod.Post,
            FedExOptions.TrackByTrackingNumberPath,
            new { invented = "field" },
            OAuthRequest(),
            UseSandbox: true);

        var result = await client.SendJsonAsync(request);

        Assert.False(result.Success);
        Assert.Equal("FEDEX_TRANSPORT_UNSUPPORTED_REQUEST", result.Error?.ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    private static FedExTransportClient CreateClient(
        IHttpClientFactory factory,
        IFedExOAuthClient oauth,
        FedExOptions? options = null) =>
        new(options ?? new FedExOptions(), factory, oauth, NullLogger<FedExTransportClient>.Instance);

    private static FedExTransportRequest<FedExTrackingNumbersRequest> TransportRequest(
        string path,
        FedExTrackingNumbersRequest body,
        string? correlationId = null) =>
        new(HttpMethod.Post, path, body, OAuthRequest(), UseSandbox: true, correlationId);

    private static FedExOAuthRequest OAuthRequest() =>
        new("client-id", "client-secret", "client_credentials", AccountNumber: "account-1");

    private static FedExTrackingNumbersRequest TrackingRequest() =>
        FedExRequestBuilders.TrackByTrackingNumbers(["123456789012"]);

    private sealed class StubFedExOAuthClient : IFedExOAuthClient
    {
        private readonly string _token;
        private readonly string _refreshToken;

        public StubFedExOAuthClient(string token, string? refreshToken = null)
        {
            _token = token;
            _refreshToken = refreshToken ?? token;
        }

        public FedExOptions Options { get; } = new();
        public int AuthorizationCalls { get; private set; }
        public int RefreshCalls { get; private set; }

        public Task<FedExOAuthTokenResult> GetAccessTokenAsync(
            FedExOAuthRequest request,
            bool useSandbox,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FedExOAuthTokenResult(true, _token, "bearer", 3600, "CXS", null, null));

        public Task<FedExAuthorizationHeaderResult> GetAuthorizationHeaderAsync(
            FedExOAuthRequest request,
            bool useSandbox,
            CancellationToken cancellationToken = default)
        {
            AuthorizationCalls++;
            return Task.FromResult(Header(_token));
        }

        public Task<FedExAuthorizationHeaderResult> RefreshAuthorizationHeaderAfterUnauthorizedAsync(
            FedExOAuthRequest request,
            bool useSandbox,
            CancellationToken cancellationToken = default)
        {
            RefreshCalls++;
            return Task.FromResult(Header(_refreshToken));
        }

        private static FedExAuthorizationHeaderResult Header(string token) =>
            new(true, new AuthenticationHeaderValue("Bearer", token), DateTimeOffset.UtcNow.AddHours(1), false, null, null);
    }

    private sealed class RecordingHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public List<string> Names { get; } = [];

        public HttpClient CreateClient(string name)
        {
            Names.Add(name);
            return client;
        }
    }

    private sealed class RecordingHandler(
        string responseBody,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        params (string Name, string Value)[] headers) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? Accept { get; private set; }
        public string? ContentType { get; private set; }
        public string? CorrelationId { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            Accept = request.Headers.Accept.Single().MediaType;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            CorrelationId = request.Headers.GetValues("x-customer-transaction-id").Single();
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
            foreach (var (name, value) in headers)
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }

            return response;
        }
    }

    private sealed class QueueingHandler(params (string Body, HttpStatusCode StatusCode)[] responses) : HttpMessageHandler
    {
        private readonly Queue<(string Body, HttpStatusCode StatusCode)> _responses = new(responses);

        public int CallCount { get; private set; }
        public List<string?> AuthorizationParameters { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            AuthorizationParameters.Add(request.Headers.Authorization?.Parameter);
            var (body, statusCode) = _responses.Count == 0
                ? ("{}", HttpStatusCode.InternalServerError)
                : _responses.Dequeue();

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }
}
