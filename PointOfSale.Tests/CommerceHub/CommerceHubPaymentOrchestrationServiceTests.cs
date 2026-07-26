using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Services.CommerceHub;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubPaymentOrchestrationServiceTests
{
    [Fact]
    public async Task ExecuteAsync_DelegatesPaymentExecutionToCanonicalPaymentService()
    {
        var request = new CommerceHubPaymentInitiateRequestDto
        {
            OrderId = Guid.NewGuid().ToString(),
            Provider = "CONFIGURED_PROVIDER",
            OrderAccessToken = "order-access-token"
        };
        var customerId = Guid.NewGuid();
        var expected = new CommerceHubPaymentInitiateResponseDto
        {
            PaymentId = Guid.NewGuid(),
            MarketplaceOrderId = Guid.NewGuid(),
            Provider = request.Provider,
            Status = "Pending"
        };
        var paymentRuntime = new Mock<IPaymentRuntime>(MockBehavior.Strict);
        paymentRuntime
            .Setup(runtime => runtime.InitiateAsync(
                request,
                customerId,
                "trace-payment-orchestration",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        using var telemetry = new CommerceHubPaymentOrchestrationTelemetry();
        var orchestration = new CommerceHubPaymentOrchestrationService(
            paymentRuntime.Object,
            telemetry,
            NullLogger<CommerceHubPaymentOrchestrationService>.Instance);

        var actual = await orchestration.ExecuteAsync(
            request,
            customerId,
            "trace-payment-orchestration");

        Assert.Same(expected, actual);
        paymentRuntime.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotTranslateCanonicalBusinessExceptions()
    {
        var request = new CommerceHubPaymentInitiateRequestDto
        {
            OrderId = Guid.NewGuid().ToString(),
            Provider = "CONFIGURED_PROVIDER"
        };
        var expected = new CommerceHubCheckoutException(
            "PAYMENT_NOT_READY",
            "Payment is not ready for execution.",
            StatusCodes.Status409Conflict);
        var paymentRuntime = new Mock<IPaymentRuntime>(MockBehavior.Strict);
        paymentRuntime
            .Setup(runtime => runtime.InitiateAsync(
                request,
                null,
                "trace-business-failure",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        using var telemetry = new CommerceHubPaymentOrchestrationTelemetry();
        var orchestration = new CommerceHubPaymentOrchestrationService(
            paymentRuntime.Object,
            telemetry,
            NullLogger<CommerceHubPaymentOrchestrationService>.Instance);

        var actual = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            orchestration.ExecuteAsync(request, null, "trace-business-failure"));

        Assert.Same(expected, actual);
        paymentRuntime.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_WrapsUnexpectedInfrastructureFailures()
    {
        var request = new CommerceHubPaymentInitiateRequestDto
        {
            OrderId = Guid.NewGuid().ToString(),
            Provider = "CONFIGURED_PROVIDER"
        };
        var expected = new TimeoutException("payment runtime timeout");
        var paymentRuntime = new Mock<IPaymentRuntime>(MockBehavior.Strict);
        paymentRuntime
            .Setup(runtime => runtime.InitiateAsync(
                request,
                null,
                "trace-infrastructure-failure",
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        using var telemetry = new CommerceHubPaymentOrchestrationTelemetry();
        var orchestration = new CommerceHubPaymentOrchestrationService(
            paymentRuntime.Object,
            telemetry,
            NullLogger<CommerceHubPaymentOrchestrationService>.Instance);

        var actual = await Assert.ThrowsAsync<CommerceHubPaymentOrchestrationException>(() =>
            orchestration.ExecuteAsync(request, null, "trace-infrastructure-failure"));

        Assert.Same(expected, actual.InnerException);
        paymentRuntime.VerifyAll();
    }

    [Fact]
    public async Task ProcessWebhookAsync_DelegatesToCanonicalPaymentService()
    {
        var expected = new CommerceHubPaymentWebhookResultDto
        {
            Processed = true,
            Provider = "CONFIGURED_PROVIDER",
            Message = "Processed"
        };
        var paymentRuntime = new Mock<IPaymentRuntime>(MockBehavior.Strict);
        paymentRuntime
            .Setup(runtime => runtime.ProcessWebhookAsync(
                "CONFIGURED_PROVIDER",
                "signature",
                "{}",
                "trace-webhook",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        using var telemetry = new CommerceHubPaymentOrchestrationTelemetry();
        var orchestration = new CommerceHubPaymentOrchestrationService(
            paymentRuntime.Object,
            telemetry,
            NullLogger<CommerceHubPaymentOrchestrationService>.Instance);

        var actual = await orchestration.ProcessWebhookAsync(
            "CONFIGURED_PROVIDER",
            "signature",
            "{}",
            "trace-webhook");

        Assert.Same(expected, actual);
        paymentRuntime.VerifyAll();
    }
}
