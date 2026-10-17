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

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(401)]
    public async Task CreationPost_NeverRetriesAnyHttpFailure(int status)
    {
        var handler = new QueueingHandler(("{}", (HttpStatusCode)status), (FedExEndpointRuntimeIntegrationTests.ValidShipmentJson(), HttpStatusCode.OK));
        var oauth = new StubFedExOAuthClient("token");
        var transport = CreateClient(new RecordingHttpClientFactory(new HttpClient(handler)), oauth);
        var adapter = new FedExAdapter(oauth, new FedExOptions(), transport);
        var result = await adapter.CreateShipmentAsync(FedExEndpointRuntimeIntegrationTests.ShipmentRequest(), FedExEndpointRuntimeIntegrationTests.Credentials());
        Assert.False(result.Success);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(0, oauth.RefreshCalls);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("network")]
    [InlineData("stream")]
    public async Task CreationPost_NeverRetriesAmbiguousTransportFailures(string kind)
    {
        Exception error = kind switch
        {
            "timeout" => new TaskCanceledException("untrusted provider diagnostic"),
            "stream" => new IOException("untrusted provider diagnostic"),
            _ => new HttpRequestException("untrusted provider diagnostic")
        };
        var handler = new ThrowingHandler(error);
        var oauth = new StubFedExOAuthClient("token");
        var transport = CreateClient(new RecordingHttpClientFactory(new HttpClient(handler)), oauth);
        var adapter = new FedExAdapter(oauth, new FedExOptions(), transport);
        var result = await adapter.CreateShipmentAsync(FedExEndpointRuntimeIntegrationTests.ShipmentRequest(), FedExEndpointRuntimeIntegrationTests.Credentials());
        Assert.False(result.Success);
        Assert.Equal(1, handler.CallCount);
        Assert.DoesNotContain("untrusted provider diagnostic", result.ErrorMessage);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"transactionId\":\"tx\",\"output\":{\"jobId\":\"job\"}}")]
    public async Task CreationPost_AmbiguousSuccessIsRejectedWithoutAnotherPost(string response)
    {
        var handler = new RecordingHandler(response);
        var oauth = new StubFedExOAuthClient("token");
        var adapter = new FedExAdapter(oauth, new FedExOptions(), CreateClient(new RecordingHttpClientFactory(new HttpClient(handler)), oauth));
        var result = await adapter.CreateShipmentAsync(FedExEndpointRuntimeIntegrationTests.ShipmentRequest(), FedExEndpointRuntimeIntegrationTests.Credentials());
        Assert.False(result.Success);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ReadPost_Refreshes401EvenWhenTransientRetryBudgetIsZero()
    {
        var handler = new QueueingHandler(("{}", HttpStatusCode.Unauthorized), ("{}", HttpStatusCode.OK));
        var oauth = new StubFedExOAuthClient("expired", "fresh");
        var transport = CreateClient(new RecordingHttpClientFactory(new HttpClient(handler)), oauth, new FedExOptions { TransportMaxRetryCount = 0 });
        Assert.True((await transport.SendJsonAsync(TransportRequest(FedExOptions.TrackByTrackingNumberPath, TrackingRequest()))).Success);
        Assert.Equal(2, handler.CallCount);
        Assert.Equal(1, oauth.RefreshCalls);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("86400", 1)]
    [InlineData("0", 2)]
    [InlineData("1", 2)]
    public async Task Throttling_RespectsRetryAfterAndDoesNotBlindlyRetryDailyQuota(string? retryAfter, int calls)
    {
        var handler = new ThrottlingHandler(retryAfter);
        var transport = CreateClient(new RecordingHttpClientFactory(new HttpClient(handler)), new StubFedExOAuthClient("token"));
        var started = System.Diagnostics.Stopwatch.StartNew();
        var result = await transport.SendJsonAsync(TransportRequest(FedExOptions.TrackByTrackingNumberPath, TrackingRequest()));
        Assert.Equal(calls, handler.CallCount);
        Assert.Equal(calls == 2, result.Success);
        if (retryAfter == "1") Assert.True(started.Elapsed >= TimeSpan.FromMilliseconds(900));
    }

    [Fact]
    public async Task Creation_StreamedBodyTimeoutIsBoundedWithoutASecondPost()
    {
        var handler = new StreamingHandler();
        var oauth = new StubFedExOAuthClient("token");
        var options = new FedExOptions { RequestTimeout = TimeSpan.FromMilliseconds(50) };
        var transport = CreateClient(new RecordingHttpClientFactory(new HttpClient(handler)), oauth, options);
        var adapter = new FedExAdapter(oauth, options, transport);
        var result = await adapter.CreateShipmentAsync(FedExEndpointRuntimeIntegrationTests.ShipmentRequest(), FedExEndpointRuntimeIntegrationTests.Credentials());
        Assert.False(result.Success);
        Assert.Equal(FedExTransportClient.TimeoutCode, result.ErrorCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ReadRetryAfter_WaitsAreCallerCancellable()
    {
        var handler = new ThrottlingHandler("10");
        var transport = CreateClient(new RecordingHttpClientFactory(new HttpClient(handler)), new StubFedExOAuthClient("token"));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transport.SendJsonAsync(TransportRequest(FedExOptions.TrackByTrackingNumberPath, TrackingRequest()), cts.Token));
        Assert.Equal(1, handler.CallCount);
    }

    private sealed class StreamingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new DelayedStream()) });
        }
    }

    private sealed class DelayedStream : MemoryStream
    {
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return 0;
        }
    }

    [Fact]
    public async Task Transport_RejectsWrongEndpointBeforeOAuthAndBoundsResponseBody()
    {
        var handler = new RecordingHandler(new string('x', 8 * 1024 * 1024 + 1));
        var oauth = new StubFedExOAuthClient("token");
        var transport = CreateClient(new RecordingHttpClientFactory(new HttpClient(handler)), oauth);
        var wrong = await transport.SendJsonAsync(TransportRequest("https://example.test/steal", TrackingRequest()));
        Assert.False(wrong.Success);
        Assert.Equal(0, oauth.AuthorizationCalls);
        Assert.Equal(0, handler.CallCount);
        var oversized = await transport.SendJsonAsync(TransportRequest(FedExOptions.TrackByTrackingNumberPath, TrackingRequest()));
        Assert.Equal("FEDEX_TRANSPORT_RESPONSE_TOO_LARGE", oversized.Error?.ErrorCode);
        Assert.Equal(1, handler.CallCount);
    }

    private sealed class ThrottlingHandler(string? retryAfter) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(++CallCount == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK) { Content = new StringContent("{}") };
            if (retryAfter != null) response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
            return Task.FromResult(response);
        }
    }

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
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromException<HttpResponseMessage>(exception);
        }
    }
}
