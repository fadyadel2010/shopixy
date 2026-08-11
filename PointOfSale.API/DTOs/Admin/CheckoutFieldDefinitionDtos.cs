namespace PointOfSale.API.DTOs.Admin;

public sealed class CheckoutFieldDefinitionDto
{
    public Guid Id { get; set; }
    public string FieldKind { get; set; } = string.Empty;
    public string FieldType { get; set; } = string.Empty;
    public string? SystemFieldKey { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? HelpText { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsRequired { get; set; }
    public int DisplayOrder { get; set; }
    public CheckoutFieldConfigurationDto Configuration { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public sealed class CheckoutFieldDefinitionsResponse
{
    public IReadOnlyList<CheckoutFieldDefinitionDto> Fields { get; set; } = Array.Empty<CheckoutFieldDefinitionDto>();
}

public sealed class CreateCheckoutFieldDefinitionRequest
{
    public string FieldType { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? HelpText { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsRequired { get; set; }
    public int? DisplayOrder { get; set; }
    public CheckoutFieldConfigurationDto? Configuration { get; set; }
}

public sealed class UpdateCheckoutFieldDefinitionRequest
{
    public string? FieldType { get; set; }
    public string? Label { get; set; }
    public string? HelpText { get; set; }
    public bool? IsEnabled { get; set; }
    public bool? IsRequired { get; set; }
    public int? DisplayOrder { get; set; }
    public CheckoutFieldConfigurationDto? Configuration { get; set; }
}

public sealed class ReorderCheckoutFieldDefinitionsRequest
{
    public List<CheckoutFieldOrderDto> Fields { get; set; } = new();
}

public sealed class CheckoutFieldOrderDto
{
    public Guid Id { get; set; }
    public int DisplayOrder { get; set; }
}

public sealed class CheckoutFieldConfigurationDto
{
    public int SchemaVersion { get; set; } = 1;
    public List<CheckoutFieldOptionDto>? Options { get; set; }
    public List<CheckoutFieldRuleDto>? ConditionalRules { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public List<string>? AllowedMimeTypes { get; set; }
    public long? MaxFileSize { get; set; }
    public int? MaxFiles { get; set; }
}

public sealed class CheckoutFieldOptionDto
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class CheckoutFieldRuleDto
{
    public string? RuleId { get; set; }
    public string ConditionMode { get; set; } = "ALL";
    public List<CheckoutFieldRuleConditionDto> Conditions { get; set; } = new();
    public List<string> Actions { get; set; } = new();
}

public sealed class CheckoutFieldRuleConditionDto
{
    public Guid SourceFieldId { get; set; }
    public string Operator { get; set; } = string.Empty;
    public string? ComparisonValue { get; set; }
}
