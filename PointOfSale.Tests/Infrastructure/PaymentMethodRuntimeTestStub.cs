using Moq;
using PointOfSale.API.Services.Payments;

namespace PointOfSale.Tests.Infrastructure;

internal static class PaymentMethodRuntimeTestStub
{
    public static IStorefrontPaymentRuntimeReadinessService Ready()
    {
        var runtime = new Mock<IStorefrontPaymentRuntimeReadinessService>();
        runtime.Setup(service => service.EvaluateAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StorefrontPaymentRuntimeReadiness.Ready);
        return runtime.Object;
    }
}
