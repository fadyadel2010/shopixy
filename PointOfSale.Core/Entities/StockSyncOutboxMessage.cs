using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.Core.Entities;

/// <summary>
/// Durable transactional outbox record for stock sync events.
/// </summary>
public class StockSyncOutboxMessage : BaseEntity, ITenantOwned
{
    private const int MaxErrorLength = 2000;

    public Guid EventId { get; private set; }

    [Required]
    [MaxLength(200)]
    public string EventType { get; private set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    public string ClrType { get; private set; } = string.Empty;

    [Required]
    public string PayloadJson { get; private set; } = string.Empty;

    public Guid TenantId { get; set; }
    public Guid VariantId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid ProductId { get; private set; }
    public int EventVersion { get; private set; }

    [Required]
    [MaxLength(200)]
    public string CorrelationId { get; private set; } = string.Empty;

    public DateTime OccurredAtUtc { get; private set; }
    public DateTime StockUpdatedAtUtc { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public DateTime? ClaimedUntilUtc { get; private set; }
    public int AttemptCount { get; private set; }

    [MaxLength(MaxErrorLength)]
    public string? LastError { get; private set; }

    public DateTime? NextRetryAtUtc { get; private set; }
    public bool IsFailed { get; private set; }

    [Timestamp]
    public uint RowVersion { get; set; }

    private StockSyncOutboxMessage()
    {
    }

    public static StockSyncOutboxMessage Create(ProductVariantStockChangedEvent @event, string payloadJson)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);

        if (!@event.TenantId.HasValue)
        {
            throw new InvalidOperationException("Stock sync events require TenantId.");
        }

        if (string.IsNullOrWhiteSpace(@event.CorrelationId))
        {
            throw new InvalidOperationException("Stock sync events require CorrelationId.");
        }

        // Validate OccurredAtUtc is initialized and UTC
        if (@event.OccurredAtUtc == default(DateTime))
        {
            throw new InvalidOperationException(
                $"Stock sync events require a valid OccurredAtUtc timestamp. " +
                $"EventId: {@event.EventId}, VariantId: {@event.VariantId}");
        }

        if (@event.OccurredAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException(
                $"Stock sync events require OccurredAtUtc to be UTC. " +
                $"EventId: {@event.EventId}, VariantId: {@event.VariantId}, Kind: {@event.OccurredAtUtc.Kind}");
        }

        // Validate StockUpdatedAtUtc is initialized and UTC
        if (@event.StockUpdatedAtUtc == default(DateTime))
        {
            throw new InvalidOperationException(
                $"Stock sync events require a valid StockUpdatedAtUtc timestamp. " +
                $"EventId: {@event.EventId}, VariantId: {@event.VariantId}");
        }

        if (@event.StockUpdatedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidOperationException(
                $"Stock sync events require StockUpdatedAtUtc to be UTC. " +
                $"EventId: {@event.EventId}, VariantId: {@event.VariantId}, Kind: {@event.StockUpdatedAtUtc.Kind}");
        }

        if (@event.EventVersion <= 0)
        {
            throw new InvalidOperationException(
                $"Stock sync events require a positive EventVersion. " +
                $"EventId: {@event.EventId}, VariantId: {@event.VariantId}, EventVersion: {@event.EventVersion}");
        }

        if (@event.WarehouseId == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"Stock sync events require WarehouseId so event identity and ordering are warehouse-scoped. " +
                $"EventId: {@event.EventId}, VariantId: {@event.VariantId}");
        }

        return new StockSyncOutboxMessage
        {
            Id = @event.EventId,
            EventId = @event.EventId,
            EventType = nameof(ProductVariantStockChangedEvent),
            ClrType = typeof(ProductVariantStockChangedEvent).AssemblyQualifiedName
                ?? typeof(ProductVariantStockChangedEvent).FullName
                ?? nameof(ProductVariantStockChangedEvent),
            PayloadJson = payloadJson,
            TenantId = @event.TenantId.Value,
            VariantId = @event.VariantId,
            WarehouseId = @event.WarehouseId,
            ProductId = @event.ProductId,
            EventVersion = @event.EventVersion,
            CorrelationId = @event.CorrelationId!,
            OccurredAtUtc = @event.OccurredAtUtc,
            StockUpdatedAtUtc = @event.StockUpdatedAtUtc,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = false
        };
    }

    public void MarkPublished()
    {
        PublishedAtUtc = DateTime.UtcNow;
        UpdatedAt = PublishedAtUtc;
        LastError = null;
        NextRetryAtUtc = null;
        IsFailed = false; // Clear failed flag on successful publish
        ClaimedUntilUtc = null; // Clear claim on successful publish
    }

    public bool TryClaim(int claimDurationSeconds = 30)
    {
        var now = DateTime.UtcNow;
        
        // Can only claim if not already published, not failed, and not currently claimed
        if (PublishedAtUtc != null || IsFailed || (ClaimedUntilUtc != null && ClaimedUntilUtc > now))
        {
            return false;
        }

        ClaimedUntilUtc = now.AddSeconds(claimDurationSeconds);
        UpdatedAt = now;
        return true;
    }

    public void RecordFailure(string error, int maxRetries = 8)
    {
        AttemptCount++;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        UpdatedAt = DateTime.UtcNow;
        ClaimedUntilUtc = null; // Clear claim to allow retry at NextRetryAtUtc

        if (AttemptCount >= maxRetries)
        {
            IsFailed = true;
            NextRetryAtUtc = null;
            return;
        }

        var delaySeconds = Math.Min(300d, 5d * Math.Pow(2d, AttemptCount - 1));
        NextRetryAtUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
    }

    public void RecordNonTerminalProjectionFailure(string error)
    {
        AttemptCount++;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        UpdatedAt = DateTime.UtcNow;
        ClaimedUntilUtc = null;
        IsFailed = false;

        var delaySeconds = Math.Min(900d, 5d * Math.Pow(2d, Math.Min(AttemptCount - 1, 8)));
        NextRetryAtUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
    }

    public void ResetForRetry()
    {
        if (PublishedAtUtc != null)
        {
            return;
        }

        IsFailed = false;
        AttemptCount = 0;
        LastError = null;
        NextRetryAtUtc = null;
        ClaimedUntilUtc = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkCompensated(string error)
    {
        var now = DateTime.UtcNow;
        PublishedAtUtc = now;
        UpdatedAt = now;
        ClaimedUntilUtc = null;
        NextRetryAtUtc = null;
        IsFailed = true;
        
        // Null-safe error handling: treat null as empty string
        var sanitizedError = error ?? string.Empty;
        LastError = sanitizedError.Length > MaxErrorLength ? sanitizedError[..MaxErrorLength] : sanitizedError;
    }
}
