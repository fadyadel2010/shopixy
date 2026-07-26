using System.Diagnostics.Metrics;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Events.WhatsApp;
using PointOfSale.Core.Interfaces.WhatsApp;
using PointOfSale.Infrastructure.Consumers.WhatsApp;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.WhatsApp.Interfaces;
using Xunit;

namespace PointOfSale.Tests.WhatsApp;

public sealed class WhatsAppOtpConsumerReliabilityTests : IDisposable
{
    private readonly CommerceReliabilityTelemetry _telemetry = new();
    private readonly MeterListener _listener;
    private readonly List<string> _recordedMetrics = new();

    public WhatsAppOtpConsumerReliabilityTests()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == CommerceReliabilityTelemetry.MeterName &&
                    (instrument.Name == "otp_channel_unavailable_total" ||
                     instrument.Name == "otp_dispatch_failed_total"))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            _recordedMetrics.Add(instrument.Name);
        });

        _listener.Start();
    }

    [Fact]
    public async Task Consume_WhenNoActiveAccount_ThrowsAndRecordsFailureMetrics()
    {
        var tenantId = Guid.NewGuid();
        var featureGate = new Mock<IWhatsAppFeatureGateService>();
        featureGate
            .Setup(x => x.IsOtpVerificationEnabledAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        featureGate
            .Setup(x => x.GetActiveAccountAsync(tenantId, Core.Enums.WhatsAppUseCase.OtpVerification, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Core.Entities.WhatsAppAccount?)null);

        var logService = new Mock<IWhatsAppMessageLogService>();
        var consumer = new WhatsAppOtpConsumer(
            featureGate.Object,
            Mock.Of<IWhatsAppMessageDispatcher>(),
            logService.Object,
            Mock.Of<IWhatsAppTemplateResolver>(),
            _telemetry,
            NullLogger<WhatsAppOtpConsumer>.Instance);

        var message = new WhatsAppOtpRequestedEvent
        {
            TenantId = tenantId,
            Phone = "+201022174803",
            OtpCode = "123456",
            Purpose = "CHECKOUT_VERIFICATION"
        };

        var context = new Mock<ConsumeContext<WhatsAppOtpRequestedEvent>>();
        context.SetupGet(x => x.Message).Returns(message);
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Consume(context.Object));

        Assert.Contains("otp_channel_unavailable_total", _recordedMetrics);
        Assert.Contains("otp_dispatch_failed_total", _recordedMetrics);
        logService.Verify(x => x.CreateLogAsync(It.IsAny<Infrastructure.MongoDB.Documents.WhatsAppMessageLogDocument>()), Times.Never);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _telemetry.Dispose();
    }
}
