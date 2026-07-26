using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Services.AI.Marketplace;
using PointOfSale.API.Services.AI.Models;
using PointOfSale.API.Services.AI.Router;

namespace PointOfSale.Tests.Infrastructure;

public sealed class AiTranslationServiceTests
{
    [Fact]
    public async Task TranslateAsync_Throws_WhenRateLimiterDeniesRequest()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"unused\"}}]}", Encoding.UTF8, "application/json")
            });

        var rateLimiter = new Mock<IAIRateLimiter>();
        rateLimiter
            .Setup(x => x.CheckAsync(Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RateLimitResult.CreateDenied(1, 1, 30));

        var service = CreateService(handler, rateLimiter.Object, enableCaching: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TranslateAsync("Runner Pro", "en", "ar"));
        Assert.Equal(0, handler.CallCount);
        rateLimiter.Verify(
            x => x.RecordRequestAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TranslateAsync_UsesMemoryCache_WhenCachingIsEnabled()
    {
        var handler = new RecordingHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"choices\":[{\"message\":{\"content\":\"Runner Pro AR\"}}]}",
                    Encoding.UTF8,
                    "application/json")
            });

        var rateLimiter = new Mock<IAIRateLimiter>();
        rateLimiter
            .Setup(x => x.CheckAsync(Guid.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(RateLimitResult.CreateAllowed(0, 60));
        rateLimiter
            .Setup(x => x.RecordRequestAsync(Guid.Empty, It.IsAny<int>(), "deepseek/deepseek-v3.2", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = CreateService(handler, rateLimiter.Object, enableCaching: true);

        var first = await service.TranslateAsync("Runner Pro", "en", "ar");
        var second = await service.TranslateAsync("Runner Pro", "en", "ar");

        Assert.Equal("Runner Pro AR", first);
        Assert.Equal(first, second);
        Assert.Equal(1, handler.CallCount);
        rateLimiter.Verify(x => x.CheckAsync(Guid.Empty, It.IsAny<CancellationToken>()), Times.Once);
        rateLimiter.Verify(
            x => x.RecordRequestAsync(Guid.Empty, It.IsAny<int>(), "deepseek/deepseek-v3.2", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static AiTranslationService CreateService(
        HttpMessageHandler handler,
        IAIRateLimiter rateLimiter,
        bool enableCaching)
    {
        return new AiTranslationService(
            new HttpClient(handler),
            Options.Create(new CommerceAIOptions
            {
                Enabled = true,
                Provider = "openrouter",
                Model = "deepseek/deepseek-v3.2",
                Endpoint = "https://example.test/v1/chat/completions",
                ApiKey = "test-key",
                TimeoutSeconds = 30,
                MaxTokens = 256,
                EnableCaching = enableCaching,
                CacheDurationSeconds = 300
            }),
            rateLimiter,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AiTranslationService>.Instance);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_responseFactory(request));
        }
    }
}
