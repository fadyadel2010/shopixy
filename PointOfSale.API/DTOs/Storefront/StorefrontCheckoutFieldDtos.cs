namespace PointOfSale.API.DTOs.Storefront;

public sealed class StorefrontCheckoutFieldDefinitionsResponse
{
    public string Status { get; set; } = "EMPTY";
    public IReadOnlyList<StorefrontCheckoutFieldDefinitionDto> Fields { get; set; } = Array.Empty<StorefrontCheckoutFieldDefinitionDto>();
    public int TotalCount { get; set; }
    public int EnabledCount { get; set; }
}

public sealed class StorefrontCheckoutFieldDefinitionDto
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
    public StorefrontCheckoutFieldConfigurationDto Configuration { get; set; } = new();
}

public sealed class StorefrontCheckoutFieldConfigurationDto
{
    public int SchemaVersion { get; set; } = 1;
    public IReadOnlyList<StorefrontCheckoutFieldOptionDto>? Options { get; set; }
    public IReadOnlyList<StorefrontCheckoutFieldRuleDto>? ConditionalRules { get; set; }
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public IReadOnlyList<string>? AllowedMimeTypes { get; set; }
    public long? MaxFileSize { get; set; }
    public int? MaxFiles { get; set; }
}

public sealed class StorefrontCheckoutFieldOptionDto
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

public sealed class StorefrontCheckoutFieldRuleDto
{
    public string RuleId { get; set; } = string.Empty;
    public string ConditionMode { get; set; } = "ALL";
    public IReadOnlyList<StorefrontCheckoutFieldRuleConditionDto> Conditions { get; set; } = Array.Empty<StorefrontCheckoutFieldRuleConditionDto>();
    public IReadOnlyList<string> Actions { get; set; } = Array.Empty<string>();
}

public sealed class StorefrontCheckoutFieldRuleConditionDto
{
    public Guid SourceFieldId { get; set; }
    public string Operator { get; set; } = string.Empty;
    public string? ComparisonValue { get; set; }
}
