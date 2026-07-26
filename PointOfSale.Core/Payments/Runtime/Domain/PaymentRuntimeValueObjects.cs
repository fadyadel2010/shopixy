using System.Collections.ObjectModel;

namespace PointOfSale.Core.Payments.Runtime.Domain;

public sealed record Currency
{
    public string Code { get; init; }

    public Currency(string code)
    {
        Code = RequiredUpperInvariant(code, nameof(code), 3);
    }

    private static string RequiredUpperInvariant(string value, string parameterName, int expectedLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length != expectedLength || normalized.Any(character => character < 'A' || character > 'Z'))
        {
            throw new ArgumentException("Currency must be a three-letter ISO-style code.", parameterName);
        }

        return normalized;
    }
}

public sealed record Money
{
    public decimal Amount { get; init; }

    public Currency Currency { get; init; }

    public Money(decimal amount, Currency currency)
    {
        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount cannot be negative.");
        }

        Amount = amount;
        Currency = currency ?? throw new ArgumentNullException(nameof(currency));
    }
}

public sealed record PaymentMetadata
{
    public IReadOnlyDictionary<string, string> Values { get; init; }

    public PaymentMetadata(IReadOnlyDictionary<string, string>? values = null)
    {
        Values = Normalize(values);
    }

    private static IReadOnlyDictionary<string, string> Normalize(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values.OrderBy(pair => pair.Key.Trim(), StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Metadata keys are required.", nameof(values));
            }

            normalized[key.Trim()] = value ?? string.Empty;
        }

        return new ReadOnlyDictionary<string, string>(normalized);
    }
}

public sealed record PaymentActionMetadata
{
    public IReadOnlyDictionary<string, string> Values { get; init; }

    public PaymentActionMetadata(IReadOnlyDictionary<string, string>? values = null)
    {
        Values = Normalize(values);
    }

    private static IReadOnlyDictionary<string, string> Normalize(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values.OrderBy(pair => pair.Key.Trim(), StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Metadata keys are required.", nameof(values));
            }

            normalized[key.Trim()] = value ?? string.Empty;
        }

        return new ReadOnlyDictionary<string, string>(normalized);
    }
}

public sealed record EvidenceMetadata
{
    public IReadOnlyDictionary<string, string> Values { get; init; }

    public EvidenceMetadata(IReadOnlyDictionary<string, string>? values = null)
    {
        Values = Normalize(values);
    }

    private static IReadOnlyDictionary<string, string> Normalize(IReadOnlyDictionary<string, string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
        }

        var normalized = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in values.OrderBy(pair => pair.Key.Trim(), StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Metadata keys are required.", nameof(values));
            }

            normalized[key.Trim()] = value ?? string.Empty;
        }

        return new ReadOnlyDictionary<string, string>(normalized);
    }
}

public sealed record AuditReferences
{
    public IReadOnlyCollection<CorrelationId> Values { get; init; }

    public AuditReferences(IReadOnlyCollection<CorrelationId>? values = null)
    {
        Values = Array.AsReadOnly(values?.ToArray() ?? Array.Empty<CorrelationId>());
    }
}

public sealed record PaymentActionResult
{
    public PaymentActionStatus Status { get; init; }

    public ReasonCode ReasonCode { get; init; }

    public PaymentActionMetadata Metadata { get; init; }

    public PaymentActionResult()
    {
        ReasonCode = new ReasonCode(string.Empty);
        Metadata = new PaymentActionMetadata();
    }

    public PaymentActionResult(
        PaymentActionStatus status,
        ReasonCode reasonCode,
        PaymentActionMetadata? metadata = null)
    {
        if (status == PaymentActionStatus.Unknown)
        {
            throw new ArgumentException("Action result status is required.", nameof(status));
        }

        Status = status;
        ReasonCode = reasonCode;
        Metadata = metadata ?? new PaymentActionMetadata();
    }
}
