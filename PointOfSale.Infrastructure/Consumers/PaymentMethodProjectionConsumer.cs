using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.Reliability;

namespace PointOfSale.Infrastructure.Consumers;

/// <summary>
/// MassTransit consumer for payment method events.
/// Projects payment method data to MongoDB for storefront reads.
/// 
/// PHASE 10.4.D1: Payment Methods Management
/// 
/// ARCHITECTURE:
/// - Consumes events from PostgreSQL changes
/// - Projects to MongoDB read model
/// - ❌ NO SECRETS are ever stored in MongoDB
/// - Idempotent (upsert by TenantId + PaymentMethodId)
/// </summary>
public class PaymentMethodProjectionConsumer :
    IConsumer<PaymentMethodCreatedEvent>,
    IConsumer<PaymentMethodUpdatedEvent>,
    IConsumer<PaymentMethodEnabledEvent>,
    IConsumer<PaymentMethodDisabledEvent>,
    IConsumer<PaymentMethodDeletedEvent>
{
    private const string ConsumerName = nameof(PaymentMethodProjectionConsumer);
    private const string DataProtectionPurpose = "PaymentMethodSecrets";

    private readonly IMongoCollection<PaymentMethodDocument> _collection;
    private readonly ApplicationDbContext _dbContext;
    private readonly IDataProtector _dataProtector;
    private readonly ILogger<PaymentMethodProjectionConsumer> _logger;

    public PaymentMethodProjectionConsumer(
        MongoDbContext mongoDbContext,
        ApplicationDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<PaymentMethodProjectionConsumer> logger)
    {
        _collection = mongoDbContext.PaymentMethods;
        _dbContext = dbContext;
        _dataProtector = dataProtectionProvider.CreateProtector(DataProtectionPurpose);
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PaymentMethodCreatedEvent> context)
    {
        var @event = context.Message;
        var applied = await ProjectFromSqlAuthorityAsync(
            @event,
            nameof(PaymentMethodCreatedEvent),
            ResolveCorrelationId(context, @event.EventId),
            allowMissingSqlAuthority: false,
            context.CancellationToken);

        _logger.LogInformation(
            "PaymentMethodCreatedEvent consumed | TenantId: {TenantId} | Code: {Code} | EventId: {EventId} | Applied: {Applied}",
            @event.TenantId, @event.Code, @event.EventId, applied);
    }

    public async Task Consume(ConsumeContext<PaymentMethodUpdatedEvent> context)
    {
        var @event = context.Message;
        var applied = await ProjectFromSqlAuthorityAsync(
            @event,
            nameof(PaymentMethodUpdatedEvent),
            ResolveCorrelationId(context, @event.EventId),
            allowMissingSqlAuthority: false,
            context.CancellationToken);

        _logger.LogInformation(
            "PaymentMethodUpdatedEvent consumed | TenantId: {TenantId} | Code: {Code} | ConfigUpdated: {ConfigUpdated} | EventId: {EventId} | Applied: {Applied}",
            @event.TenantId, @event.Code, @event.ConfigurationUpdated, @event.EventId, applied);
    }

    public async Task Consume(ConsumeContext<PaymentMethodEnabledEvent> context)
    {
        var @event = context.Message;
        var applied = await ProjectFromSqlAuthorityAsync(
            @event,
            nameof(PaymentMethodEnabledEvent),
            ResolveCorrelationId(context, @event.EventId),
            allowMissingSqlAuthority: false,
            context.CancellationToken);

        _logger.LogInformation(
            "PaymentMethodEnabledEvent consumed | TenantId: {TenantId} | Code: {Code} | EventId: {EventId} | Applied: {Applied}",
            @event.TenantId, @event.Code, @event.EventId, applied);
    }

    public async Task Consume(ConsumeContext<PaymentMethodDisabledEvent> context)
    {
        var @event = context.Message;
        var applied = await ProjectFromSqlAuthorityAsync(
            @event,
            nameof(PaymentMethodDisabledEvent),
            ResolveCorrelationId(context, @event.EventId),
            allowMissingSqlAuthority: false,
            context.CancellationToken);

        _logger.LogInformation(
            "PaymentMethodDisabledEvent consumed | TenantId: {TenantId} | Code: {Code} | EventId: {EventId} | Applied: {Applied}",
            @event.TenantId, @event.Code, @event.EventId, applied);
    }

    public async Task Consume(ConsumeContext<PaymentMethodDeletedEvent> context)
    {
        var @event = context.Message;
        var applied = await ProjectFromSqlAuthorityAsync(
            @event,
            nameof(PaymentMethodDeletedEvent),
            ResolveCorrelationId(context, @event.EventId),
            allowMissingSqlAuthority: true,
            context.CancellationToken);

        _logger.LogInformation(
            "PaymentMethodDeletedEvent consumed | TenantId: {TenantId} | Code: {Code} | EventId: {EventId} | Applied: {Applied}",
            @event.TenantId, @event.Code, @event.EventId, applied);
    }

    private async Task<bool> ProjectFromSqlAuthorityAsync(
        PaymentMethodEventBase @event,
        string eventType,
        string correlationId,
        bool allowMissingSqlAuthority,
        CancellationToken cancellationToken)
    {
        var key = new ProjectionEventFenceKey(ConsumerName, eventType, @event.PaymentMethodId);
        await using var fence = await ProjectionEventFence.TryAcquireAsync(
            _dbContext,
            @event.EventId,
            key,
            _logger,
            cancellationToken);

        if (fence == null)
        {
            _logger.LogInformation(
                "Payment method projection duplicate or active replay collapsed before MongoDB writes | EventType: {EventType} | TenantId: {TenantId} | PaymentMethodId: {PaymentMethodId} | EventId: {EventId}",
                eventType,
                @event.TenantId,
                @event.PaymentMethodId,
                @event.EventId);
            return false;
        }

        var sqlMethod = await LoadSqlPaymentMethodAuthorityAsync(
            @event.TenantId,
            @event.PaymentMethodId,
            @event.Version,
            cancellationToken);

        PaymentMethodDocument? document;
        if (sqlMethod == null)
        {
            if (!allowMissingSqlAuthority)
            {
                _logger.LogWarning(
                    "Payment method projection rejected by SQL authority | EventType: {EventType} | TenantId: {TenantId} | PaymentMethodId: {PaymentMethodId} | EventVersion: {EventVersion} | EventId: {EventId}",
                    eventType,
                    @event.TenantId,
                    @event.PaymentMethodId,
                    @event.Version,
                    @event.EventId);

                await fence.CompleteAsync(@event.EventId, @event.TenantId, correlationId, key, cancellationToken);
                return false;
            }

            document = BuildTombstoneDocument(@event, null);
        }
        else
        {
            document = sqlMethod.IsDeleted
                ? BuildTombstoneDocument(@event, sqlMethod)
                : MapSqlAuthorityToDocument(sqlMethod, @event);
        }

        var applied = await UpsertDocument(
            document,
            @event.EventId,
            @event.OccurredAtUtc,
            @event.Version,
            cancellationToken);

        if (!applied)
        {
            _logger.LogWarning(
                "Payment method projection write ignored as stale | EventType: {EventType} | TenantId: {TenantId} | PaymentMethodId: {PaymentMethodId} | EventVersion: {EventVersion} | EventId: {EventId}",
                eventType,
                @event.TenantId,
                @event.PaymentMethodId,
                @event.Version,
                @event.EventId);
        }

        await fence.CompleteAsync(@event.EventId, @event.TenantId, correlationId, key, cancellationToken);
        return applied;
    }

    private async Task<TenantPaymentMethod?> LoadSqlPaymentMethodAuthorityAsync(
        Guid tenantId,
        Guid paymentMethodId,
        int eventVersion,
        CancellationToken cancellationToken)
    {
        var method = await _dbContext.TenantPaymentMethods
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.Id == paymentMethodId,
                cancellationToken);

        if (method == null)
        {
            return null;
        }

        var validation = ProjectionAuthorityValidator.ValidateSqlVersion(
            method.Version,
            eventVersion,
            requireExactVersion: true);

        return validation.IsAccepted ? method : null;
    }

    private PaymentMethodDocument MapSqlAuthorityToDocument(
        TenantPaymentMethod method,
        PaymentMethodEventBase @event)
    {
        var configuration = EvaluateConfiguration(method);

        return new PaymentMethodDocument
        {
            TenantId = method.TenantId,
            PaymentMethodId = method.Id,
            Code = NormalizeCode(method.Code),
            DisplayName = method.DisplayName,
            DisplayNameAr = method.DisplayNameAr,
            Description = method.Description,
            DescriptionAr = method.DescriptionAr,
            IsEnabled = method.IsEnabled,
            SortOrder = method.SortOrder,
            SupportsOnline = method.SupportsOnline,
            SupportsOffline = method.SupportsOffline,
            IsSandbox = method.IsSandbox,
            Icon = method.Icon,
            MinOrderAmount = method.MinOrderAmount,
            MaxOrderAmount = method.MaxOrderAmount,
            Fee = method.Fee,
            FeeType = method.FeeType,
            PublicConfiguration = new Dictionary<string, string>(
                configuration.PublicConfiguration,
                StringComparer.OrdinalIgnoreCase),
            IsConfigurationValid = configuration.IsConfigurationValid,
            HasConfiguration = !string.IsNullOrWhiteSpace(method.ConfigEncrypted),
            IsDeleted = false,
            SyncedAtUtc = DateTime.UtcNow,
            LastEventId = @event.EventId,
            LastEventOccurredAtUtc = @event.OccurredAtUtc,
            LastEventVersion = @event.Version
        };
    }

    private static PaymentMethodDocument BuildTombstoneDocument(
        PaymentMethodEventBase @event,
        TenantPaymentMethod? sqlMethod)
    {
        return new PaymentMethodDocument
        {
            TenantId = @event.TenantId,
            PaymentMethodId = @event.PaymentMethodId,
            Code = NormalizeCode(sqlMethod?.Code ?? @event.Code),
            DisplayName = sqlMethod?.DisplayName ?? string.Empty,
            DisplayNameAr = sqlMethod?.DisplayNameAr,
            Description = sqlMethod?.Description,
            DescriptionAr = sqlMethod?.DescriptionAr,
            IsEnabled = false,
            SortOrder = sqlMethod?.SortOrder ?? 0,
            SupportsOnline = sqlMethod?.SupportsOnline ?? false,
            SupportsOffline = sqlMethod?.SupportsOffline ?? false,
            IsSandbox = sqlMethod?.IsSandbox ?? false,
            Icon = sqlMethod?.Icon,
            MinOrderAmount = sqlMethod?.MinOrderAmount,
            MaxOrderAmount = sqlMethod?.MaxOrderAmount,
            Fee = sqlMethod?.Fee ?? 0m,
            FeeType = sqlMethod?.FeeType ?? "fixed",
            IsConfigurationValid = false,
            HasConfiguration = false,
            PublicConfiguration = new Dictionary<string, string>(),
            IsDeleted = true,
            SyncedAtUtc = DateTime.UtcNow,
            LastEventId = @event.EventId,
            LastEventOccurredAtUtc = @event.OccurredAtUtc,
            LastEventVersion = @event.Version
        };
    }

    private PaymentMethodConfigurationValidationResult EvaluateConfiguration(TenantPaymentMethod method)
    {
        if (string.IsNullOrWhiteSpace(method.ConfigEncrypted))
        {
            return PaymentMethodConfigurationValidator.Validate(method.Code, null);
        }

        try
        {
            var decrypted = _dataProtector.Unprotect(method.ConfigEncrypted);
            var config = JsonSerializer.Deserialize<Dictionary<string, string>>(decrypted);
            return PaymentMethodConfigurationValidator.Validate(method.Code, config);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to decrypt payment-method configuration during projection; method will not be exposed as configured | TenantId: {TenantId} | PaymentMethodId: {PaymentMethodId} | Code: {Code}",
                method.TenantId,
                method.Id,
                method.Code);

            return new PaymentMethodConfigurationValidationResult(
                NormalizeCode(method.Code),
                PaymentMethodConfigurationValidator.RequiresConfiguration(method.Code),
                false,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                new[] { "Stored payment configuration could not be decrypted." },
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private async Task<bool> UpsertDocument(
        PaymentMethodDocument document,
        Guid eventId,
        DateTime occurredAtUtc,
        int version,
        CancellationToken cancellationToken)
    {
        var filter = Builders<PaymentMethodDocument>.Filter.And(
            Builders<PaymentMethodDocument>.Filter.Eq(d => d.TenantId, document.TenantId),
            Builders<PaymentMethodDocument>.Filter.Eq(d => d.PaymentMethodId, document.PaymentMethodId),
            BuildFreshnessFilter(eventId, occurredAtUtc, version)
        );

        try
        {
            var result = await _collection.ReplaceOneAsync(
                filter,
                document,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);

            return result.ModifiedCount > 0 || result.UpsertedId != null;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var retry = await _collection.ReplaceOneAsync(
                filter,
                document,
                new ReplaceOptions { IsUpsert = false },
                cancellationToken);

            return retry.ModifiedCount > 0;
        }
    }

    private async Task<bool> UpdateDocument(
        FilterDefinition<PaymentMethodDocument> filter,
        UpdateDefinition<PaymentMethodDocument> update,
        bool isUpsert,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _collection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = isUpsert },
                cancellationToken);

            return result.ModifiedCount > 0 || result.UpsertedId != null;
        }
        catch (MongoWriteException ex) when (isUpsert && ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            var retry = await _collection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = false },
                cancellationToken);

            return retry.ModifiedCount > 0;
        }
    }

    private static string ResolveCorrelationId<T>(ConsumeContext<T> context, Guid eventId)
        where T : class =>
        context.CorrelationId?.ToString() ?? context.MessageId?.ToString() ?? eventId.ToString();

    private static string NormalizeCode(string? code) =>
        code?.Trim().ToUpperInvariant() ?? string.Empty;

    private static FilterDefinition<PaymentMethodDocument> BuildFreshnessFilter(Guid eventId, DateTime occurredAtUtc, int version)
    {
        return Builders<PaymentMethodDocument>.Filter.And(
            Builders<PaymentMethodDocument>.Filter.Ne(d => d.LastEventId, eventId),
            Builders<PaymentMethodDocument>.Filter.Or(
                Builders<PaymentMethodDocument>.Filter.Exists(d => d.LastEventOccurredAtUtc, false),
                Builders<PaymentMethodDocument>.Filter.Eq(d => d.LastEventOccurredAtUtc, null),
                Builders<PaymentMethodDocument>.Filter.Lt(d => d.LastEventOccurredAtUtc, occurredAtUtc),
                Builders<PaymentMethodDocument>.Filter.And(
                    Builders<PaymentMethodDocument>.Filter.Eq(d => d.LastEventOccurredAtUtc, occurredAtUtc),
                    Builders<PaymentMethodDocument>.Filter.Lt(d => d.LastEventVersion, version))));
    }
}

/// <summary>
/// MassTransit consumer definition for PaymentMethodProjectionConsumer.
/// 
/// CRITICAL: This ensures the consumer is registered with a specific endpoint name
/// so that events are correctly routed to this consumer.
/// 
/// Without this, MassTransit uses default naming which may not match the queue bindings,
/// causing events to not be consumed (silent failure).
/// </summary>
public class PaymentMethodProjectionConsumerDefinition : ConsumerDefinition<PaymentMethodProjectionConsumer>
{
    public PaymentMethodProjectionConsumerDefinition()
    {
        // Explicit endpoint name for predictable queue binding
        EndpointName = "payment-method-projection";
        
        // Configure concurrency limit for projection consumers
        ConcurrentMessageLimit = 4;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<PaymentMethodProjectionConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // Use default retry policy (from global configuration)
        // Projection consumers should be idempotent, so retries are safe
    }
}
