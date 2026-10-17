using MassTransit;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces.Payments;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// Converts authoritative carrier COD collection evidence into the canonical
/// payment lifecycle transition. The lifecycle service remains the only owner
/// of payment status and collection accounting.
/// </summary>
public sealed class ShipmentCodCollectedConsumer : IConsumer<ShipmentCodCollectedEvent>
{
    private readonly IPaymentLifecycleService _paymentLifecycleService;
    private readonly ILogger<ShipmentCodCollectedConsumer> _logger;

    public ShipmentCodCollectedConsumer(
        IPaymentLifecycleService paymentLifecycleService,
        ILogger<ShipmentCodCollectedConsumer> logger)
    {
        _paymentLifecycleService = paymentLifecycleService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ShipmentCodCollectedEvent> context)
    {
        var message = context.Message;
        if (!message.TenantId.HasValue || message.TenantId.Value == Guid.Empty)
        {
            _logger.LogError(
                "[ShipmentCOD] Rejected COD collection event without tenant authority | OrderId: {OrderId} | ShipmentId: {ShipmentId}",
                message.OrderId,
                message.ShipmentId);
            return;
        }

        _logger.LogInformation(
            "[ShipmentCOD] Processing COD collection | TenantId: {TenantId} | OrderId: {OrderId} | ShipmentId: {ShipmentId} | Amount: {Amount} {Currency}",
            message.TenantId,
            message.OrderId,
            message.ShipmentId,
            message.CodAmount,
            message.Currency);

        var result = await _paymentLifecycleService.RecordCarrierCodCollectionAsync(
            message.TenantId.Value,
            message.OrderId,
            message.ShipmentId,
            message.CollectionEvidenceId ?? Guid.Empty,
            context.CancellationToken);

        if (result.IsSuccess || result.RequiresManualReview)
        {
            _logger.LogInformation(
                "[ShipmentCOD] Collection evidence processed | OrderId: {OrderId} | NewStatus: {PaymentStatus} | Idempotent: {WasIdempotent} | ReconciliationRequired: {RequiresManualReview} | Code: {FailureCode}",
                message.OrderId,
                result.NewStatus ?? PaymentStatus.Collected,
                result.WasIdempotent,
                result.RequiresManualReview,
                result.FailureCode);
            return;
        }

        _logger.LogCritical(
            "[ShipmentCOD] COD collection payment lifecycle transition failed | OrderId: {OrderId} | ShipmentId: {ShipmentId} | FailureCode: {FailureCode} | Error: {Error}",
            message.OrderId,
            message.ShipmentId,
            result.FailureCode,
            result.ErrorMessage);

        throw new InvalidOperationException(
            $"COD collection payment lifecycle transition failed for order {message.OrderId}: {result.ErrorMessage ?? result.FailureCode ?? "Unknown failure"}");
    }

}

public sealed class ShipmentCodCollectedConsumerDefinition
    : ConsumerDefinition<ShipmentCodCollectedConsumer>
{
    public ShipmentCodCollectedConsumerDefinition()
    {
        EndpointName = "shipment-cod-collected";
        ConcurrentMessageLimit = 8;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ShipmentCodCollectedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(retry => retry.Intervals(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15)));
    }
}
