using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Services.AI;
using PointOfSale.API.Services.AI.Models;
using PointOfSale.API.Services.AI.Router;
using PointOfSale.API.Services.AI.Router.Adapters;
using PointOfSale.API.Services.Storefront;

namespace PointOfSale.Tests.Security;

public sealed class Phase7ExecutionTests
{
    [Fact]
    public async Task Ai_MissingCredentialNeverContactsProvider()
    {
        var calls = 0;
        var handler = new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)); });
        var limiter = new Mock<IAIRateLimiter>(); limiter.Setup(x => x.CheckAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(PointOfSale.API.Services.AI.Router.RateLimitResult.CreateAllowed(1, 10));
        var router = new AIRouterService(new HttpClient(handler), new AIPromptManager(), new AIResponseValidator(NullLogger<AIResponseValidator>.Instance), limiter.Object, new SafeResponseBuilder(),
            Options.Create(new CommerceAIOptions { Enabled = true, Endpoint = "http://qa.invalid/completions", ApiKey = "", Model = "qa" }), NullLogger<AIRouterService>.Instance);
        Assert.True((await router.ProcessAsync(new() { TenantId = Guid.NewGuid(), Message = "QA" })).IsFallback); Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Ai_ContextBudgetPreventsUnboundedProviderInput()
    {
        var calls = 0;
        var handler = new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)); });
        var context = new CommerceContext { TenantId = Guid.NewGuid(), Message = "QA", Products = [new() { Description = new string('x', 65536) }] };
        Assert.True((await Router(handler).ProcessAsync(context)).IsFallback); Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Ai_MultiModelAdapterHonorsConfiguredPriorityTemperatureAndTokenLimit()
    {
        var options = Options.Create(new CommerceAIOptions { Temperature = 0.7, Models = new() { ["DeepSeek"] = new() { Priority = 7, ApiKey = "synthetic", Endpoint = "https://qa.invalid/completions", ModelId = "qa-configured-model", MaxTokens = 77 } } });
        var handler = new Handler(async (request, token) => {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal(0.7, json.RootElement.GetProperty("temperature").GetDouble()); Assert.Equal(77, json.RootElement.GetProperty("max_tokens").GetInt32());
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"CONTROLLED\"}}]}") };
        });
        var adapter = new DeepSeekAdapter(new HttpClient(handler), options, NullLogger<DeepSeekAdapter>.Instance);
        Assert.Equal(7, adapter.Priority); Assert.True((await adapter.SendAsync("system", "user")).Success);
    }

    [Fact]
    public async Task Ai_MultiModelAttemptsAreBoundedAndEachAttemptReservesBudget()
    {
        var adapters = Enumerable.Range(1, 3).Select(priority => {
            var adapter = new Mock<IModelAdapter>(); adapter.SetupGet(x => x.IsEnabled).Returns(true); adapter.SetupGet(x => x.Priority).Returns(priority);
            adapter.Setup(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(ModelResponse.CreateFailure("HTTP 503", 1, "qa")); return adapter;
        }).ToArray();
        var limiter = new Mock<IAIRateLimiter>(); limiter.Setup(x => x.CheckAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(PointOfSale.API.Services.AI.Router.RateLimitResult.CreateAllowed(1, 10));
        var router = new ModelRouter(adapters.Select(x => x.Object), new AIPromptManager(), new AIResponseValidator(NullLogger<AIResponseValidator>.Instance), limiter.Object, new SafeResponseBuilder(), Options.Create(new CommerceAIOptions { MaxRetries = 2, EnableFallback = true }), NullLogger<ModelRouter>.Instance);
        Assert.True((await router.RouteAsync(new() { TenantId = Guid.NewGuid(), Message = "QA" })).IsFallback);
        adapters[0].Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        adapters[1].Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        adapters[2].Verify(x => x.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        limiter.Verify(x => x.CheckAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
    [Theory]
    [InlineData(401)] [InlineData(429)] [InlineData(503)]
    public async Task Ai_ProviderFailuresAreSafeAndNeverLoggedAsProviderBody(int status)
    {
        var logs = new CaptureLogger<AIRouterService>();
        var calls = 0;
        var handler = new Handler(async (request, ct) => {
            calls++;
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("qa-model", body.RootElement.GetProperty("model").GetString());
            Assert.Equal(128, body.RootElement.GetProperty("max_tokens").GetInt32());
            return new((HttpStatusCode)status) { Content = new StringContent("SYNTHETIC_SECRET_ECHO_AND_PERSONAL_PROMPT") };
        });
        var response = await Router(handler, logs).ProcessAsync(new() { TenantId = Guid.NewGuid(), Message = "P7QA" });
        Assert.True(response.IsFallback); Assert.Equal(1, calls);
        Assert.DoesNotContain(logs.Messages, x => x.Contains("SYNTHETIC_SECRET_ECHO"));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":\"\"}}]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"{}\"}}]}")]
    public async Task Ai_EmptyMalformedAndIncompleteResponsesAreFailures(string response)
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) }));
        Assert.True((await Router(handler).ProcessAsync(new() { TenantId = Guid.NewGuid(), Message = "QA" })).IsFallback);
    }

    [Fact]
    public async Task Ai_ValidCompletionExecutesRealRequestAndValidatorWithoutAnExternalModel()
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = "stop", message = new { content = "{\"type\":\"message\",\"message\":\"CONTROLLED TRANSPORT ONLY\"}" } } }, usage = new { total_tokens = 12 } }))
        }));
        var response = await Router(handler).ProcessAsync(new() { TenantId = Guid.NewGuid(), Message = "QA" });
        Assert.False(response.IsFallback); Assert.Equal("CONTROLLED TRANSPORT ONLY", response.Message);
    }

    [Fact]
    public async Task Ai_CallerCancellationPropagatesAndDoesNotStartFallbackAdapter()
    {
        using var cts = new CancellationTokenSource();
        var started = new TaskCompletionSource();
        var handler = new Handler(async (_, ct) => { started.SetResult(); await Task.Delay(Timeout.Infinite, ct); return new(HttpStatusCode.OK); });
        var task = Router(handler).ProcessAsync(new() { TenantId = Guid.NewGuid(), Message = "QA" }, cts.Token);
        await started.Task; cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task Ai_OversizedProviderResponseIsRejected()
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 131073)) }));
        Assert.True((await Router(handler).ProcessAsync(new() { TenantId = Guid.NewGuid(), Message = "QA" })).IsFallback);
    }

    [Fact]
    public void Ai_HallucinatedVariantAndExcessiveQuantityAreRejected()
    {
        var id = Guid.NewGuid(); var context = new CommerceContext { Products = [new() { Id = id }] };
        var validator = new AIResponseValidator(NullLogger<AIResponseValidator>.Instance);
        foreach (var item in new[] { new { productId = id, variantId = (Guid?)Guid.NewGuid(), quantity = 1 }, new { productId = id, variantId = (Guid?)null, quantity = 100000 } })
            Assert.False(validator.TryValidate(JsonSerializer.Serialize(new { type = "order_draft", message = "QA", orderDraft = new { intent = "create", items = new[] { item } } }), context, out _, out _));
    }

    [Fact]
    public async Task Correlation_ValidOnceMismatchedForeignAndTamperedStateFailClosed()
    {
        var replay = new MemoryReplay(); var protector = new EphemeralDataProtectionProvider();
        var service = new SocialLoginChallengeService(protector, replay); var tenant = Guid.NewGuid();
        var issuer = new DefaultHttpContext(); var challenge = service.Issue(issuer, tenant, "google");
        var cookie = issuer.Response.Headers.SetCookie.Single()!.Split(';')[0];
        DefaultHttpContext Context(string value) { var context = new DefaultHttpContext(); context.Request.Headers.Cookie = value; return context; }
        Assert.Null(await service.ConsumeAsync(Context(cookie), tenant, "google", new string('0', 64)));
        Assert.Null(await service.ConsumeAsync(Context(cookie), Guid.NewGuid(), "google", challenge.State));
        Assert.Null(await service.ConsumeAsync(Context(cookie), tenant, "facebook", challenge.State));
        Assert.Null(await service.ConsumeAsync(Context(cookie + "tampered"), tenant, "google", challenge.State));
        Assert.Equal(challenge.Nonce, await service.ConsumeAsync(Context(cookie), tenant, "google", challenge.State));
        Assert.Null(await service.ConsumeAsync(Context(cookie), tenant, "google", challenge.State));
        var parallelIssuer = new DefaultHttpContext(); var second = service.Issue(parallelIssuer, tenant, "google");
        var secondCookie = parallelIssuer.Response.Headers.SetCookie.Single()!.Split(';')[0];
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.ConsumeAsync(Context(secondCookie), tenant, "google", second.State)));
        Assert.Single(results.Where(x => x != null));
        var expired = protector.CreateProtector("Storefront.SocialLogin.Challenge.v1").Protect(JsonSerializer.Serialize(new { Tenant = tenant, Provider = "google", challenge.State, challenge.Nonce, Expires = DateTime.UtcNow.AddMinutes(-1) }));
        Assert.Null(await service.ConsumeAsync(Context("sf_social_google=" + expired), tenant, "google", challenge.State));
    }

    private static AIRouterService Router(HttpMessageHandler handler, ILogger<AIRouterService>? logger = null)
    {
        var limiter = new Mock<IAIRateLimiter>(); limiter.Setup(x => x.CheckAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(PointOfSale.API.Services.AI.Router.RateLimitResult.CreateAllowed(1, 10));
        return new(new HttpClient(handler), new AIPromptManager(), new AIResponseValidator(NullLogger<AIResponseValidator>.Instance), limiter.Object, new SafeResponseBuilder(),
            Options.Create(new CommerceAIOptions { Enabled = true, Endpoint = "http://qa.invalid/completions", ApiKey = "synthetic-key", Model = "qa-model", MaxTokens = 128 }), logger ?? NullLogger<AIRouterService>.Instance);
    }
    internal sealed class MemoryReplay : ISocialLoginReplayStore
    {
        private readonly ConcurrentDictionary<string, bool> _used = new();
        public Task<bool> ConsumeAsync(string key, TimeSpan lifetime) => Task.FromResult(_used.TryAdd(key, true));
    }
    internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => callback(request, cancellationToken);
    }
    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
