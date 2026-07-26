using PointOfSale.Core.Enums.CommerceHubFinance;

namespace PointOfSale.Core.Entities.CommerceHubFinance;

public class CommerceHubVendorPaymentMethod
{
    private const int MaxNameLength = 120;
    private const int MaxDestinationValueLength = 200;

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string DestinationValue { get; private set; } = string.Empty;
    public string Instructions { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }

    private CommerceHubVendorPaymentMethod()
    {
    }

    public static CommerceHubVendorPaymentMethod Create(
        string name,
        string destinationValue,
        string instructions,
        bool isActive,
        Guid createdBy,
        DateTime? createdAtUtc = null)
    {
        if (createdBy == Guid.Empty)
        {
            throw new ArgumentException("CreatedBy is required.", nameof(createdBy));
        }

        var normalizedName = NormalizeRequired(name, MaxNameLength, nameof(name));
        var normalizedDestinationValue = NormalizeRequired(destinationValue, MaxDestinationValueLength, nameof(destinationValue));
        var normalizedInstructions = NormalizeRequired(instructions, maxLength: 4000, nameof(instructions));

        return new CommerceHubVendorPaymentMethod
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            DestinationValue = normalizedDestinationValue,
            Instructions = normalizedInstructions,
            IsActive = isActive,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }

    private static string NormalizeRequired(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        var normalizedValue = value.Trim();
        if (normalizedValue.Length > maxLength)
        {
            throw new ArgumentException($"{parameterName} must not exceed {maxLength} characters.", parameterName);
        }

        return normalizedValue;
    }
}
