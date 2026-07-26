using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Controllers.Admin;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.Infrastructure.RateLimiting;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Payments;

namespace PointOfSale.Tests.Security;

public sealed class WebhookRequestHardeningTests
{
    [Fact]
    public async Task BodyReader_WhenContentLengthExceedsLimit_RejectsBeforeReadingBody()
    {
        var payload = $$"""{"padding":"{{new string('x', (int)WebhookRequestLimits.PaymentProviderMaxBytes)}}"}""";
        var body = new MemoryStream(Encoding.UTF8.GetBytes(payload));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.ContentType = "application/json";
        httpContext.Request.ContentLength = WebhookRequestLimits.PaymentProviderMaxBytes + 1;
        httpContext.Request.Body = body;

        var ex = await Assert.ThrowsAsync<WebhookRequestRejectedException>(() =>
            WebhookRequestBodyReader.ReadJsonBodyAsync(
                httpContext,
                "stripe",
                WebhookRequestLimits.PaymentProviderMaxBytes,
                "payment"));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, ex.StatusCode);
        Assert.Equal("WEBHOOK_PAYLOAD_TOO_LARGE", ex.ErrorCode);
        Assert.Equal(0, body.Position);
    }

    [Fact]
    public async Task PaymentPipeline_WhenContentTypeIsNotJson_RejectsBeforeTenantResolution()
    {
        var resolver = new Mock<IPaymentWebhookTenantResolver>(MockBehavior.Strict);
        var pipeline = new PaymentWebhookRequestPipeline(
            resolver.Object,
            NullLogger<PaymentWebhookRequestPipeline>.Instance);

        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = new ServiceCollection().BuildServiceProvider();
        httpContext.Request.ContentType = "text/plain";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("""{"id":"evt"}"""));
        httpContext.Request.Headers["Stripe-Signature"] = "sig";

        var ex = await Assert.ThrowsAsync<PaymentWebhookRequestPreparationException>(() =>
            pipeline.PrepareAsync(
                httpContext,
                "stripe",
                "trace-content-type",
                request => request.Headers["Stripe-Signature"].FirstOrDefault(),
                _ => null));

        Assert.Equal(StatusCodes.Status415UnsupportedMediaType, ex.StatusCode);
        Assert.Equal("WEBHOOK_UNSUPPORTED_CONTENT_TYPE", ex.ErrorCode);
        resolver.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task WebhookRateLimiter_WhenPermitLimitExceeded_HandlerWrites429()
    {
        using var limiter = new SlidingWindowRateLimiter(WebhookRateLimiterPolicy.CreateOptions());

        for (var i = 0; i < WebhookRateLimiterPolicy.PermitLimitPerMinute; i++)
        {
            using var lease = limiter.AttemptAcquire(1);
            Assert.True(lease.IsAcquired);
        }

        using var rejectedLease = limiter.AttemptAcquire(1);
        Assert.False(rejectedLease.IsAcquired);

        var httpContext = new DefaultHttpContext
        {
            Response =
            {
                Body = new MemoryStream()
            }
        };

        await RateLimitRejectionHandlers.WriteApiErrorAsync(httpContext, CancellationToken.None);

        Assert.Equal(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(WebhookPostActions))]
    public void WebhookPostActions_MustHaveSizeLimitAndWebhookRateLimit(
        Type controllerType,
        string actionName,
        long expectedMaxBytes)
    {
        var method = controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method => method.Name == actionName);

        var sizeLimit = method.GetCustomAttribute<RequestSizeLimitAttribute>(inherit: true);
        Assert.NotNull(sizeLimit);
        var sizeLimitMetadata = Assert.IsAssignableFrom<IRequestSizeLimitMetadata>(sizeLimit);
        Assert.Equal(expectedMaxBytes, sizeLimitMetadata.MaxRequestBodySize);

        var rateLimit = method.GetCustomAttribute<EnableRateLimitingAttribute>(inherit: true);
        Assert.NotNull(rateLimit);
        Assert.Equal(WebhookRateLimiterPolicy.PolicyName, rateLimit!.PolicyName);
    }

    public static TheoryData<Type, string, long> WebhookPostActions() => new()
    {
        { typeof(StripeWebhooksController), nameof(StripeWebhooksController.HandleWebhook), WebhookRequestLimits.PaymentProviderMaxBytes },
        { typeof(PaymobWebhooksController), nameof(PaymobWebhooksController.HandleWebhook), WebhookRequestLimits.PaymentProviderMaxBytes },
        { typeof(FawryWebhookController), nameof(FawryWebhookController.Handle), WebhookRequestLimits.PaymentProviderMaxBytes },
        { typeof(PaymentWebhooksController), nameof(PaymentWebhooksController.HandleWebhook), WebhookRequestLimits.PaymentProviderMaxBytes },
        { typeof(CommerceHubPaymentsController), nameof(CommerceHubPaymentsController.HandleWebhook), WebhookRequestLimits.PaymentProviderMaxBytes },
        { typeof(CommerceHubPaymentsController), nameof(CommerceHubPaymentsController.HandleLegacyPaymobWebhook), WebhookRequestLimits.PaymentProviderMaxBytes },
        { typeof(ShippingWebhooksController), nameof(ShippingWebhooksController.ReceiveWebhook), WebhookRequestLimits.ShippingProviderMaxBytes },
        { typeof(ShippingWebhooksController), nameof(ShippingWebhooksController.ReceiveBostaWebhook), WebhookRequestLimits.ShippingProviderMaxBytes },
        { typeof(ShippingWebhooksController), nameof(ShippingWebhooksController.ReceiveAramexWebhook), WebhookRequestLimits.ShippingProviderMaxBytes },
        { typeof(ShippingWebhooksController), nameof(ShippingWebhooksController.ReceiveMylerzWebhook), WebhookRequestLimits.ShippingProviderMaxBytes },
        { typeof(ShippingWebhooksController), nameof(ShippingWebhooksController.ReceiveDhlWebhook), WebhookRequestLimits.ShippingProviderMaxBytes },
        { typeof(AdminAbandonedCartRecoveryController), nameof(AdminAbandonedCartRecoveryController.LogRecoveryResult), WebhookRequestLimits.ShippingProviderMaxBytes }
    };
}
