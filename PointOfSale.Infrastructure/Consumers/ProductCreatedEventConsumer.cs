using MassTransit;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for ProductCreatedEvent.
/// 
/// Responsibilities:
/// - Validate incoming event (TenantId is mandatory)
/// - Transform event to MongoDB ProductDocument
/// - Perform idempotent upsert to MongoDB
/// - Log all actions with structured data
/// 
/// Error Handling Strategy:
/// - Missing TenantId: Log error, acknowledge message (no retry)
/// - MongoDB transient errors: Let MassTransit handle retries
/// - Business validation errors: Log and acknowledge (no retry storm)
/// 
/// IMPORTANT:
/// - This consumer does NOT contain business logic
/// - MongoDB is NOT the source of truth
/// - All writes are idempotent by (TenantId, ProductId)
/// </summary>
public class ProductCreatedEventConsumer : IConsumer<ProductCreatedEvent>
{
    private readonly IProductSqlAuthorityProjectionWriter _projectionWriter;
    private readonly ILogger<ProductCreatedEventConsumer> _logger;

    public ProductCreatedEventConsumer(
        IProductSqlAuthorityProjectionWriter projectionWriter,
        ILogger<ProductCreatedEventConsumer> logger)
    {
        _projectionWriter = projectionWriter;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ProductCreatedEvent> context)
    {
        var @event = context.Message;
        var correlationId = @event.CorrelationId ?? context.CorrelationId?.ToString() ?? "N/A";

        // Start processing log
        _logger.LogInformation(
            "Processing ProductCreatedEvent | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
            @event.EventId,
            @event.ProductId,
            @event.TenantId,
            correlationId);

        // ===========================================
        // CRITICAL: TenantId Validation
        // Events without TenantId MUST NOT be written to MongoDB
        // This prevents data corruption in multi-tenant environment
        // ===========================================
        if (!@event.TenantId.HasValue)
        {
            _logger.LogError(
                "ProductCreatedEvent rejected: TenantId is missing | EventId: {EventId} | ProductId: {ProductId} | CorrelationId: {CorrelationId}. " +
                "Message will be acknowledged to prevent retry storm.",
                @event.EventId,
                @event.ProductId,
                correlationId);

            // Acknowledge without processing - do NOT throw
            // This prevents infinite retries for permanently invalid messages
            return;
        }

        // Validate ProductId
        if (@event.ProductId == Guid.Empty)
        {
            _logger.LogError(
                "ProductCreatedEvent rejected: ProductId is empty | EventId: {EventId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                @event.EventId,
                @event.TenantId,
                correlationId);
            return;
        }

        try
        {
            var writeResult = await _projectionWriter.ApplyAsync(
                new ProductSqlAuthorityProjectionWriteRequest(
                    @event.TenantId.Value,
                    @event.ProductId,
                    MapToDocument(@event),
                    @event.EventId,
                    @event.OccurredAtUtc,
                    @event.EventVersion),
                context.CancellationToken);

            if (writeResult.Disposition == ProductSqlAuthorityProjectionWriteDisposition.Upserted)
            {
                _logger.LogInformation(
                    "ProductCreatedEvent processed successfully | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | SKU: {SKU} | CorrelationId: {CorrelationId}",
                    @event.EventId,
                    @event.ProductId,
                    @event.TenantId,
                    @event.SKU ?? "N/A",
                    correlationId);
            }
            else if (writeResult.Tombstoned)
            {
                _logger.LogInformation(
                    "ProductCreatedEvent collapsed to SQL-authoritative tombstone | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | Result: {Result} | CorrelationId: {CorrelationId}",
                    @event.EventId,
                    @event.ProductId,
                    @event.TenantId,
                    writeResult.Disposition,
                    correlationId);
            }
            else
            {
                _logger.LogWarning(
                    "ProductCreatedEvent ignored because projection already has same or newer product event | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | OccurredAtUtc: {OccurredAtUtc} | EventVersion: {EventVersion} | CorrelationId: {CorrelationId}",
                    @event.EventId,
                    @event.ProductId,
                    @event.TenantId,
                    @event.OccurredAtUtc,
                    @event.EventVersion,
                    correlationId);
            }
        }
        catch (MongoException ex) when (IsTransientError(ex))
        {
            // Transient MongoDB errors - let MassTransit retry
            _logger.LogWarning(ex,
                "Transient MongoDB error processing ProductCreatedEvent - will retry | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                @event.EventId,
                @event.ProductId,
                @event.TenantId,
                correlationId);

            throw; // Re-throw to trigger MassTransit retry
        }
        catch (MongoException ex)
        {
            // Non-transient MongoDB errors
            _logger.LogError(ex,
                "MongoDB error processing ProductCreatedEvent | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                @event.EventId,
                @event.ProductId,
                @event.TenantId,
                correlationId);

            throw; // Let MassTransit handle based on retry policy
        }
        catch (Exception ex)
        {
            // Unexpected errors
            _logger.LogError(ex,
                "Unexpected error processing ProductCreatedEvent | EventId: {EventId} | ProductId: {ProductId} | TenantId: {TenantId} | CorrelationId: {CorrelationId}",
                @event.EventId,
                @event.ProductId,
                @event.TenantId,
                correlationId);

            throw;
        }
    }

    /// <summary>
    /// Maps ProductCreatedEvent to ProductDocument for MongoDB storage.
    /// This is a simple mapping - no business logic here.
    /// 
    /// CRITICAL: OnlinePrice is the source of truth for storefront pricing.
    /// If OnlinePrice is null/0, falls back to SalePrice.
    /// </summary>
    private static ProductDocument MapToDocument(ProductCreatedEvent @event)
    {
        // Map default unit if available (for POS multi-unit support)
        ProductUnitDocument? defaultUnit = null;
        if (@event.DefaultUnit != null)
        {
            defaultUnit = new ProductUnitDocument
            {
                UnitId = @event.DefaultUnit.UnitId,
                Name = @event.DefaultUnit.Name,
                Symbol = @event.DefaultUnit.Symbol,
                SalePrice = @event.DefaultUnit.SalePrice,
                ConversionFactor = @event.DefaultUnit.ConversionFactor,
                IsBaseUnit = @event.DefaultUnit.IsBaseUnit
            };
        }
        
        // Map all units
        var units = @event.Units?.Select(u => new ProductUnitDocument
        {
            UnitId = u.UnitId,
            Name = u.Name,
            Symbol = u.Symbol,
            SalePrice = u.SalePrice,
            ConversionFactor = u.ConversionFactor,
            IsBaseUnit = u.IsBaseUnit
        }).ToList() ?? new List<ProductUnitDocument>();
        
        return new ProductDocument
        {
            TenantId = @event.TenantId!.Value,
            ProductId = @event.ProductId,
            Name = @event.Name,
            NameAr = @event.NameAr,
            SKU = @event.SKU,
            Barcode = @event.Barcode,
            Price = @event.SalePrice, // POS price (fallback)
            OnlinePrice = @event.OnlinePrice, // Storefront price (source of truth)
            PurchasePrice = @event.PurchasePrice,
            CategoryId = @event.CategoryId,
            CategoryName = @event.CategoryName,
            Description = @event.Description,
            DescriptionAr = @event.DescriptionAr,
            MetaTitle = @event.MetaTitle,
            MetaDescription = @event.MetaDescription,
            Tags = (@event.Tags ?? new List<string>()).ToList(),
            Features = (@event.Features ?? new List<string>()).ToList(),
            Faqs = (@event.Faqs ?? new List<ProductFaqData>())
                .Select(item => new ProductFaqDocument
                {
                    Question = item.Question,
                    Answer = item.Answer
                })
                .ToList(),
            AiGenerated = @event.AiGenerated,
            AiMetadata = ParseAiMetadata(@event.AiMetadataJson),
            Brand = @event.Brand,
            Images = @event.Images,
            Media = new List<ProductMediaDocument>(),
            Slug = @event.Slug,
            ProductTemplate = @event.ProductTemplate,
            IsOnlineVisible = @event.IsOnlineVisible,
            IsSearchable = @event.IsSearchable,
            IsFeatured = @event.IsFeatured,
            IsActive = true, // New products are active by default
            DefaultUnit = defaultUnit,
            Units = units,
            // CQRS COMPLIANCE: Inventory settings projected to MongoDB for checkout reads
            TrackStock = @event.TrackStock,
            AllowBackorder = @event.AllowBackorder,
            // PHASE 11.6: HasVariants controls cart variant enforcement
            HasVariants = @event.HasVariants,
            // Shipping weight for accurate shipping quotes
            Weight = @event.Weight,
            CreatedAtUtc = @event.OccurredAtUtc,
            SyncedAtUtc = DateTime.UtcNow,
            LastEventId = @event.EventId,
            LastEventOccurredAtUtc = @event.OccurredAtUtc,
            LastEventVersion = @event.EventVersion
        };
    }

    private static BsonDocument? ParseAiMetadata(string? aiMetadataJson)
    {
        if (string.IsNullOrWhiteSpace(aiMetadataJson))
        {
            return null;
        }

        try
        {
            return BsonDocument.Parse(aiMetadataJson);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Determines if a MongoDB error is transient and should trigger a retry.
    /// </summary>
    private static bool IsTransientError(MongoException ex)
    {
        // Network errors, connection issues, timeout errors
        return ex is MongoConnectionException ||
               ex is MongoCommandException commandEx && 
               (commandEx.Code == 11600 || // InterruptedAtShutdown
                commandEx.Code == 11601 || // Interrupted
                commandEx.Code == 13435 || // NotPrimaryNoSecondaryOk
                commandEx.Code == 13436 || // NotPrimaryOrSecondary
                commandEx.Code == 189 ||   // PrimarySteppedDown
                commandEx.Code == 91) ||   // ShutdownInProgress
               ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
    }
}
