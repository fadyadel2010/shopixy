using System.Collections.ObjectModel;

namespace PointOfSale.API.Services.Payments.ProviderContracts;

public sealed record ExecutionReference(string Value)
{
    public ExecutionReference() : this(string.Empty)
    {
    }
}

public sealed record BridgeTraceId(string Value)
{
    public BridgeTraceId() : this(string.Empty)
    {
    }
}

public sealed record ExecutionCorrelation(string Value)
{
    public ExecutionCorrelation() : this(string.Empty)
    {
    }
}

public sealed record ExecutionMetadata
{
    public IReadOnlyDictionary<string, string> Values { get; init; }

    public ExecutionMetadata(IReadOnlyDictionary<string, string>? values = null)
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
                throw new ArgumentException("Execution metadata keys are required.", nameof(values));
            }

            normalized[key.Trim()] = value ?? string.Empty;
        }

        return new ReadOnlyDictionary<string, string>(normalized);
    }
}

public sealed record ExecutionTrace
{
    public BridgeTraceId Id { get; init; }

    public ExecutionReference Reference { get; init; }

    public ExecutionCorrelation Correlation { get; init; }

    public IReadOnlyCollection<string> Entries { get; init; }

    public ExecutionTrace()
    {
        Id = new BridgeTraceId(string.Empty);
        Reference = new ExecutionReference(string.Empty);
        Correlation = new ExecutionCorrelation(string.Empty);
        Entries = Array.AsReadOnly(Array.Empty<string>());
    }

    public ExecutionTrace(
        BridgeTraceId id,
        ExecutionReference reference,
        ExecutionCorrelation correlation,
        IReadOnlyCollection<string>? entries = null)
    {
        RequireText(id.Value, nameof(id));
        RequireText(reference.Value, nameof(reference));
        RequireText(correlation.Value, nameof(correlation));

        Id = new BridgeTraceId(id.Value.Trim());
        Reference = new ExecutionReference(reference.Value.Trim());
        Correlation = new ExecutionCorrelation(correlation.Value.Trim());
        Entries = Array.AsReadOnly(entries?.Where(entry => !string.IsNullOrWhiteSpace(entry)).Select(entry => entry.Trim()).ToArray()
            ?? Array.Empty<string>());
    }

    private static void RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }
    }
}
