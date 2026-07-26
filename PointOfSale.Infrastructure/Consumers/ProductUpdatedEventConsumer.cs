using MassTransit;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for ProductUpdatedEvent.
/// Product read-model writes must always recompose from current SQL authority.
/// </summary>
public class ProductUpdatedEventConsumer : IConsumer<ProductUpdatedEvent>
{
    private readonly IProductReadRepository _repository;
    private readonly IProductSqlAuthorityProjectionWriter _projectionWriter;
    private readonly ILogger<ProductUpdatedEventConsumer> _logger;

    public ProductUpdatedEventConsumer(
        IProductReadRepository repository,
        IProductSqlAuthorityProjectionWriter projectionWriter,
        ILogger<ProductUpdatedEventConsumer> logger)
    {
        _repository = repository;
        _projectionWriter = projectionWriter;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ProductUpdatedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        _logger.LogInformation(
            "Processing ProductUpdatedEvent | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | ChangedFields: {ChangedFields} | CorrelationId: {CorrelationId}",
            @event.EventId,
            @event.ProductId,
            @event.TenantId,
            string.Join(", ", @event.ChangedFields),
            correlationId);

        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "ProductUpdatedEvent rejected: TenantId is missing | EventId: {EventId} | ProductId: {ProductId} | CorrelationId: {CorrelationId}",
                @event.EventId,
                @event.ProductId,
                correlationId);
            return;
        }

        if (@event.ProductId == Guid.Empty)
        {
            _logger.LogError(
                "ProductUpdatedEvent rejected: ProductId is empty | EventId: {EventId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                @event.EventId,
                @event.TenantId,
                correlationId);
            return;
        }

        try
        {
            var currentProjection = await _repository.FindByProductIdAsync(
                @event.TenantId.Value,
                @event.ProductId,
                context.CancellationToken) ?? new ProductDocument
                {
                    TenantId = @event.TenantId.Value,
                    ProductId = @event.ProductId,
                    CreatedAtUtc = @event.OccurredAtUtc
                };

            var writeResult = await _projectionWriter.ApplyAsync(
                new ProductSqlAuthorityProjectionWriteRequest(
                    @event.TenantId.Value,
                    @event.ProductId,
                    currentProjection,
                    @event.EventId,
                    @event.OccurredAtUtc,
                    @event.EventVersion),
                context.CancellationToken);

            if (writeResult.Disposition == ProductSqlAuthorityProjectionWriteDisposition.Upserted)
            {
                _logger.LogInformation(
                    "ProductUpdatedEvent recomposed from SQL authority | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                    @event.EventId,
                    @event.ProductId,
                    @event.TenantId,
                    correlationId);
                return;
            }

            if (writeResult.Tombstoned)
            {
                _logger.LogInformation(
                    "ProductUpdatedEvent collapsed to SQL-authoritative tombstone | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | Result: {Result} | CorrelationId: {CorrelationId}",
                    @event.EventId,
                    @event.ProductId,
                    @event.TenantId,
                    writeResult.Disposition,
                    correlationId);
                return;
            }

            _logger.LogWarning(
                "ProductUpdatedEvent ignored because SQL-recomposed projection already has same or newer product event | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | OccurredAtUtc: {OccurredAtUtc} | EventVersion: {EventVersion} | CorrelationId: {CorrelationId}",
                @event.EventId,
                @event.ProductId,
                @event.TenantId,
                @event.OccurredAtUtc,
                @event.EventVersion,
                correlationId);
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex,
                "MongoDB error processing ProductUpdatedEvent | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                @event.EventId,
                @event.ProductId,
                @event.TenantId,
                correlationId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error processing ProductUpdatedEvent | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                @event.EventId,
                @event.ProductId,
                @event.TenantId,
                correlationId);
            throw;
        }
    }
}
