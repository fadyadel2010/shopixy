using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Core.Financial;

namespace PointOfSale.Core.Entities.CommerceHubFinance;

public class CommerceHubFinancialTransaction
{
    private const int MaxCurrencyLength = 10;
    private const int MaxTraceKeyLength = 200;

    public Guid Id { get; private set; }
    public CommerceHubFinancialEntityType EntityType { get; private set; }
    public Guid? EntityId { get; private set; }
    public CommerceHubFinancialTransactionType TransactionType { get; private set; }
    public CommerceHubFinancialTransactionDirection Direction { get; private set; }
    public CommerceHubFinancialBucket FinancialBucket { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public CommerceHubFinancialReferenceType ReferenceType { get; private set; }
    public Guid ReferenceId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }

    private CommerceHubFinancialTransaction()
    {
    }

    public static CommerceHubFinancialTransaction Create(
        CommerceHubFinancialEntityType entityType,
        Guid? entityId,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialTransactionDirection direction,
        decimal amount,
        string? currency,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId,
        string description,
        string? idempotencyKey = null,
        string? correlationId = null,
        DateTime? createdAtUtc = null,
        Guid? createdBy = null,
        string? reason = null,
        CommerceHubFinancialBucket? financialBucketOverride = null)
    {
        if (!Enum.IsDefined(entityType))
        {
            throw new ArgumentOutOfRangeException(nameof(entityType), "EntityType is invalid.");
        }

        if (!Enum.IsDefined(transactionType))
        {
            throw new ArgumentOutOfRangeException(nameof(transactionType), "TransactionType is invalid.");
        }

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), "Direction is invalid.");
        }

        if (!Enum.IsDefined(referenceType))
        {
            throw new ArgumentOutOfRangeException(nameof(referenceType), "ReferenceType is invalid.");
        }

        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
        }

        if (referenceId == Guid.Empty)
        {
            throw new ArgumentException("ReferenceId is required.", nameof(referenceId));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Description is required.", nameof(description));
        }

        var normalizedCurrency = FinancialCurrencyGuard.RequireEgpCurrency(
            currency,
            "commercehub.financial_transaction.currency",
            $"referenceType:{referenceType};referenceId:{referenceId:D};entityType:{entityType};transactionType:{transactionType}");

        if (normalizedCurrency.Length > MaxCurrencyLength)
        {
            throw new ArgumentException("Currency must not exceed 10 characters.", nameof(currency));
        }

        if (entityType == CommerceHubFinancialEntityType.Platform && entityId.HasValue)
        {
            throw new ArgumentException("Platform transactions cannot have an EntityId.", nameof(entityId));
        }

        if (entityType == CommerceHubFinancialEntityType.Vendor && (!entityId.HasValue || entityId.Value == Guid.Empty))
        {
            throw new ArgumentException("Vendor transactions must include a vendor EntityId.", nameof(entityId));
        }

        if (createdBy == Guid.Empty)
        {
            throw new ArgumentException("CreatedBy cannot be empty.", nameof(createdBy));
        }

        var normalizedIdempotencyKey = NormalizeTraceKey(
            idempotencyKey,
            BuildDefaultIdempotencyKey(entityType, entityId, transactionType, direction, referenceType, referenceId),
            nameof(idempotencyKey));

        var normalizedCorrelationId = NormalizeTraceKey(
            correlationId,
            BuildDefaultCorrelationId(referenceType, referenceId),
            nameof(correlationId));

        var normalizedReason = string.IsNullOrWhiteSpace(reason)
            ? null
            : reason.Trim();
        var financialBucket = financialBucketOverride ?? CommerceHubFinancialClassification.ClassifyBucket(
            entityType,
            transactionType,
            direction);

        if (!Enum.IsDefined(financialBucket))
        {
            throw new ArgumentOutOfRangeException(nameof(financialBucketOverride), "FinancialBucket is invalid.");
        }

        return new CommerceHubFinancialTransaction
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityType == CommerceHubFinancialEntityType.Platform ? null : entityId,
            TransactionType = transactionType,
            Direction = direction,
            FinancialBucket = financialBucket,
            Amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero),
            Currency = normalizedCurrency,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Description = description.Trim(),
            Reason = normalizedReason,
            IdempotencyKey = normalizedIdempotencyKey,
            CorrelationId = normalizedCorrelationId,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }

    private static string NormalizeTraceKey(string? value, string fallbackValue, string parameterName)
    {
        var normalizedValue = string.IsNullOrWhiteSpace(value)
            ? fallbackValue
            : value.Trim();

        if (normalizedValue.Length > MaxTraceKeyLength)
        {
            throw new ArgumentException(
                $"{parameterName} must not exceed {MaxTraceKeyLength} characters.",
                parameterName);
        }

        return normalizedValue;
    }

    private static string BuildDefaultIdempotencyKey(
        CommerceHubFinancialEntityType entityType,
        Guid? entityId,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialTransactionDirection direction,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId)
    {
        var entityScope = entityType == CommerceHubFinancialEntityType.Platform
            ? "platform"
            : entityId?.ToString("N") ?? "missing";

        return $"ledger:{entityScope}:{transactionType}:{direction}:{referenceType}:{referenceId:N}";
    }

    private static string BuildDefaultCorrelationId(
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId)
    {
        return $"{referenceType}:{referenceId:N}";
    }
}
