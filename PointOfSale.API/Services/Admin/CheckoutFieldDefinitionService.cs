using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Checkout;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Admin;

public sealed class CheckoutFieldDefinitionService : ICheckoutFieldDefinitionService
{
    private const int ConfigurationSchemaVersion = 1;

    private static readonly HashSet<string> ImageMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    private static readonly HashSet<string> FileMimeTypes = new(ImageMimeTypes, StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/msword"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly IReadOnlyDictionary<string, SystemFieldContract> SystemFieldContracts =
        new Dictionary<string, SystemFieldContract>(StringComparer.OrdinalIgnoreCase)
        {
            ["full_name"] = new("full_name", CheckoutFieldType.Text, "Full Name", true, true, 10),
            ["phone"] = new("phone", CheckoutFieldType.Phone, "Phone Number", true, true, 20),
            ["email"] = new("email", CheckoutFieldType.Email, "Email Address", true, false, 30),
            ["city"] = new("city", CheckoutFieldType.Text, "City", true, true, 40),
            ["area"] = new("area", CheckoutFieldType.Text, "Area / District", true, false, 50),
            ["street_address"] = new("street_address", CheckoutFieldType.Textarea, "Street Address", true, true, 60),
            ["building"] = new("building", CheckoutFieldType.Text, "Building", false, false, 70),
            ["apartment"] = new("apartment", CheckoutFieldType.Text, "Apartment", false, false, 80),
            ["postal_code"] = new("postal_code", CheckoutFieldType.Text, "Postal Code", false, false, 90),
            ["order_notes"] = new("order_notes", CheckoutFieldType.Textarea, "Order Notes", true, false, 100),
        };

    private readonly ApplicationDbContext _dbContext;

    public CheckoutFieldDefinitionService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CheckoutFieldDefinitionsResponse> GetFieldsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return await BuildResponseAsync(tenantId, cancellationToken);
    }

    public async Task<CheckoutFieldDefinitionsResponse> InitializeSystemFieldsAsync(
        Guid tenantId,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        await EnsureDefaultSystemFieldsAsync(tenantId, actorUserId, cancellationToken);
        return await BuildResponseAsync(tenantId, cancellationToken);
    }

    public async Task<CheckoutFieldDefinitionDto> CreateCustomFieldAsync(
        Guid tenantId,
        CreateCheckoutFieldDefinitionRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var fieldType = ParseFieldType(request.FieldType);
        var label = NormalizeRequiredText(request.Label, "FIELD_LABEL_REQUIRED", "Label is required.", 120);
        var helpText = NormalizeOptionalText(request.HelpText, 500);
        var isEnabled = request.IsEnabled;
        var isRequired = request.IsRequired;
        ValidateRequiredEnabled(isEnabled, isRequired);
        var fieldId = Guid.NewGuid();
        var configuration = ValidateAndSerializeConfiguration(fieldType, request.Configuration);
        var displayOrder = request.DisplayOrder ?? await NextDisplayOrderAsync(tenantId, cancellationToken);

        var now = DateTime.UtcNow;
        var field = new CheckoutFieldDefinition
        {
            Id = fieldId,
            TenantId = tenantId,
            FieldKind = CheckoutFieldKind.Custom,
            FieldType = fieldType,
            SystemFieldKey = null,
            Label = label,
            HelpText = helpText,
            IsEnabled = isEnabled,
            IsRequired = isRequired,
            DisplayOrder = displayOrder,
            ConfigurationSchemaVersion = ConfigurationSchemaVersion,
            ConfigurationJson = configuration,
            CreatedAt = now,
            CreatedBy = actorUserId
        };

        _dbContext.CheckoutFieldDefinitions.Add(field);
        await ValidateConditionalRulesAsync(tenantId, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Map(field);
    }

    public async Task<CheckoutFieldDefinitionDto> UpdateFieldAsync(
        Guid tenantId,
        Guid fieldId,
        UpdateCheckoutFieldDefinitionRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var field = await FindActiveFieldAsync(tenantId, fieldId, cancellationToken);

        var originalFieldType = field.FieldType;
        var fieldType = field.FieldType;
        if (!string.IsNullOrWhiteSpace(request.FieldType))
        {
            if (field.FieldKind == CheckoutFieldKind.System)
            {
                throw new CheckoutFieldDefinitionException(
                    "SYSTEM_FIELD_TYPE_IMMUTABLE",
                    "System field type cannot be changed.");
            }

            fieldType = ParseFieldType(request.FieldType);
        }

        var label = request.Label == null
            ? field.Label
            : NormalizeRequiredText(request.Label, "FIELD_LABEL_REQUIRED", "Label is required.", 120);
        var helpText = request.HelpText == null ? field.HelpText : NormalizeOptionalText(request.HelpText, 500);
        var isEnabled = request.IsEnabled ?? field.IsEnabled;
        var isRequired = request.IsRequired ?? field.IsRequired;
        ValidateRequiredEnabled(isEnabled, isRequired);

        if (field.FieldKind == CheckoutFieldKind.System)
        {
            var contract = GetSystemFieldContract(field.SystemFieldKey);
            if (fieldType != contract.FieldType)
            {
                throw new CheckoutFieldDefinitionException(
                    "SYSTEM_FIELD_TYPE_INVALID",
                    "System field type must match the canonical system field contract.");
            }
        }

        var typeChanged = fieldType != originalFieldType;
        field.FieldType = fieldType;
        field.Label = label;
        field.HelpText = helpText;
        field.IsEnabled = isEnabled;
        field.IsRequired = isRequired;
        if (request.DisplayOrder.HasValue)
        {
            field.DisplayOrder = request.DisplayOrder.Value;
        }

        if (request.Configuration != null || typeChanged)
        {
            field.ConfigurationJson = ValidateAndSerializeConfiguration(fieldType, request.Configuration);
            field.ConfigurationSchemaVersion = ConfigurationSchemaVersion;
        }

        field.UpdatedAt = DateTime.UtcNow;
        field.UpdatedBy = actorUserId;

        await ValidateConditionalRulesAsync(tenantId, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Map(field);
    }

    public async Task ArchiveCustomFieldAsync(
        Guid tenantId,
        Guid fieldId,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var field = await FindActiveFieldAsync(tenantId, fieldId, cancellationToken);
        if (field.FieldKind == CheckoutFieldKind.System)
        {
            throw new CheckoutFieldDefinitionException(
                "SYSTEM_FIELD_DELETE_FORBIDDEN",
                "System fields cannot be archived. Disable the field instead.");
        }

        field.IsEnabled = false;
        field.IsRequired = false;
        field.IsDeleted = true;
        field.UpdatedAt = DateTime.UtcNow;
        field.UpdatedBy = actorUserId;

        await ValidateConditionalRulesAsync(tenantId, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<CheckoutFieldDefinitionsResponse> ReorderFieldsAsync(
        Guid tenantId,
        ReorderCheckoutFieldDefinitionsRequest request,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Fields.Count == 0)
        {
            throw new CheckoutFieldDefinitionException("FIELD_ORDER_REQUIRED", "At least one field order entry is required.");
        }

        if (request.Fields.Any(item => item.Id == Guid.Empty))
        {
            throw new CheckoutFieldDefinitionException("FIELD_ID_INVALID", "Field identifiers must be non-empty GUIDs.");
        }

        if (request.Fields.Select(item => item.Id).Distinct().Count() != request.Fields.Count)
        {
            throw new CheckoutFieldDefinitionException("FIELD_ORDER_DUPLICATE_ID", "Field order entries must not contain duplicate field identifiers.");
        }

        if (request.Fields.Select(item => item.DisplayOrder).Distinct().Count() != request.Fields.Count)
        {
            throw new CheckoutFieldDefinitionException("FIELD_ORDER_DUPLICATE_VALUE", "Display order values must be unique.");
        }

        var ids = request.Fields.Select(item => item.Id).ToHashSet();
        var fields = await _dbContext.CheckoutFieldDefinitions
            .Where(field => field.TenantId == tenantId && ids.Contains(field.Id))
            .ToListAsync(cancellationToken);

        if (fields.Count != request.Fields.Count)
        {
            throw new CheckoutFieldDefinitionException(
                "FIELD_NOT_FOUND",
                "One or more checkout fields were not found for the current tenant.",
                StatusCodes.Status404NotFound);
        }

        var orderById = request.Fields.ToDictionary(item => item.Id, item => item.DisplayOrder);
        var now = DateTime.UtcNow;
        foreach (var field in fields)
        {
            field.DisplayOrder = orderById[field.Id];
            field.UpdatedAt = now;
            field.UpdatedBy = actorUserId;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await BuildResponseAsync(tenantId, cancellationToken);
    }

    private async Task EnsureDefaultSystemFieldsAsync(Guid tenantId, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.CheckoutFieldDefinitions
            .IgnoreQueryFilters()
            .Where(field => field.TenantId == tenantId && field.FieldKind == CheckoutFieldKind.System)
            .ToListAsync(cancellationToken);

        var duplicateKey = existing
            .Where(field => !string.IsNullOrWhiteSpace(field.SystemFieldKey) && !field.IsDeleted)
            .GroupBy(field => field.SystemFieldKey!, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateKey != null)
        {
            throw new CheckoutFieldDefinitionException(
                "DUPLICATE_SYSTEM_FIELD",
                $"Duplicate active system field definition found for '{duplicateKey.Key}'.");
        }

        var existingByKey = existing
            .Where(field => !string.IsNullOrWhiteSpace(field.SystemFieldKey))
            .GroupBy(field => field.SystemFieldKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.OrderBy(field => field.CreatedAt).First(), StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        foreach (var contract in SystemFieldContracts.Values.OrderBy(item => item.DisplayOrder))
        {
            if (existingByKey.TryGetValue(contract.Key, out var field))
            {
                if (field.IsDeleted)
                {
                    field.IsDeleted = false;
                    field.IsEnabled = contract.IsEnabledByDefault;
                    field.IsRequired = contract.IsRequiredByDefault;
                    field.DisplayOrder = contract.DisplayOrder;
                    field.UpdatedAt = now;
                    field.UpdatedBy = actorUserId;
                }

                continue;
            }

            _dbContext.CheckoutFieldDefinitions.Add(new CheckoutFieldDefinition
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                FieldKind = CheckoutFieldKind.System,
                FieldType = contract.FieldType,
                SystemFieldKey = contract.Key,
                Label = contract.DefaultLabel,
                IsEnabled = contract.IsEnabledByDefault,
                IsRequired = contract.IsRequiredByDefault,
                DisplayOrder = contract.DisplayOrder,
                ConfigurationSchemaVersion = ConfigurationSchemaVersion,
                ConfigurationJson = ValidateAndSerializeConfiguration(contract.FieldType, null),
                CreatedAt = now,
                CreatedBy = actorUserId
            });
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
            var canonicalKeys = await _dbContext.CheckoutFieldDefinitions
                .IgnoreQueryFilters()
                .Where(field => field.TenantId == tenantId &&
                                field.FieldKind == CheckoutFieldKind.System &&
                                !field.IsDeleted)
                .Select(field => field.SystemFieldKey)
                .ToListAsync(cancellationToken);
            if (!SystemFieldContracts.Keys.All(key => canonicalKeys.Contains(key, StringComparer.OrdinalIgnoreCase)))
            {
                throw;
            }
        }
    }

    private async Task<CheckoutFieldDefinitionsResponse> BuildResponseAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var fields = await _dbContext.CheckoutFieldDefinitions
            .AsNoTracking()
            .Where(field => field.TenantId == tenantId)
            .OrderBy(field => field.DisplayOrder)
            .ThenBy(field => field.CreatedAt)
            .ThenBy(field => field.Id)
            .ToListAsync(cancellationToken);

        return new CheckoutFieldDefinitionsResponse
        {
            Fields = fields.Select(Map).ToArray()
        };
    }

    private async Task<CheckoutFieldDefinition> FindActiveFieldAsync(Guid tenantId, Guid fieldId, CancellationToken cancellationToken)
    {
        var field = await _dbContext.CheckoutFieldDefinitions
            .FirstOrDefaultAsync(item => item.TenantId == tenantId && item.Id == fieldId, cancellationToken);

        if (field == null)
        {
            throw new CheckoutFieldDefinitionException(
                "FIELD_NOT_FOUND",
                "Checkout field was not found for the current tenant.",
                StatusCodes.Status404NotFound);
        }

        return field;
    }

    private async Task<int> NextDisplayOrderAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var max = await _dbContext.CheckoutFieldDefinitions
            .Where(field => field.TenantId == tenantId)
            .Select(field => (int?)field.DisplayOrder)
            .MaxAsync(cancellationToken);
        return (max ?? 0) + 10;
    }

    private static CheckoutFieldDefinitionDto Map(CheckoutFieldDefinition field) => new()
    {
        Id = field.Id,
        FieldKind = ToApiValue(field.FieldKind),
        FieldType = ToApiValue(field.FieldType),
        SystemFieldKey = field.SystemFieldKey,
        Label = field.Label,
        HelpText = field.HelpText,
        IsEnabled = field.IsEnabled,
        IsRequired = field.IsRequired,
        DisplayOrder = field.DisplayOrder,
        Configuration = DeserializeConfiguration(field.ConfigurationJson),
        CreatedAt = field.CreatedAt,
        UpdatedAt = field.UpdatedAt
    };

    private static string ValidateAndSerializeConfiguration(CheckoutFieldType fieldType, CheckoutFieldConfigurationDto? configuration)
    {
        var source = configuration ?? new CheckoutFieldConfigurationDto();
        if (source.SchemaVersion is not 0 and not ConfigurationSchemaVersion)
        {
            throw new CheckoutFieldDefinitionException(
                "FIELD_CONFIGURATION_SCHEMA_UNSUPPORTED",
                "Checkout field configuration schema version is not supported.");
        }

        var canonical = new CheckoutFieldConfigurationDto { SchemaVersion = ConfigurationSchemaVersion };
        canonical.ConditionalRules = NormalizeConditionalRules(source.ConditionalRules);

        switch (fieldType)
        {
            case CheckoutFieldType.Select:
            case CheckoutFieldType.Radio:
                canonical.Options = NormalizeOptions(source.Options);
                break;

            case CheckoutFieldType.Number:
                if (source.Min.HasValue && source.Max.HasValue && source.Min.Value > source.Max.Value)
                {
                    throw new CheckoutFieldDefinitionException("FIELD_CONFIGURATION_INVALID", "Number min cannot be greater than max.");
                }
                canonical.Min = source.Min;
                canonical.Max = source.Max;
                break;

            case CheckoutFieldType.Text:
            case CheckoutFieldType.Textarea:
            case CheckoutFieldType.Email:
            case CheckoutFieldType.Phone:
                ValidateLengthConfiguration(source.MinLength, source.MaxLength, fieldType == CheckoutFieldType.Textarea ? 5000 : 1000);
                canonical.MinLength = source.MinLength;
                canonical.MaxLength = source.MaxLength;
                break;

            case CheckoutFieldType.Image:
            case CheckoutFieldType.File:
            case CheckoutFieldType.MultiImage:
                canonical.AllowedMimeTypes = NormalizeMimeTypes(fieldType, source.AllowedMimeTypes);
                canonical.MaxFileSize = ValidateMaxFileSize(source.MaxFileSize);
                canonical.MaxFiles = ValidateMaxFiles(fieldType, source.MaxFiles);
                break;

            case CheckoutFieldType.Checkbox:
            case CheckoutFieldType.Date:
                break;
        }

        return JsonSerializer.Serialize(canonical, JsonOptions);
    }

    private async Task ValidateConditionalRulesAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var fields = await _dbContext.CheckoutFieldDefinitions
            .IgnoreQueryFilters()
            .Where(field => field.TenantId == tenantId && !field.IsDeleted)
            .ToListAsync(cancellationToken);

        var fieldsById = fields.ToDictionary(field => field.Id);
        foreach (var entry in _dbContext.ChangeTracker.Entries<CheckoutFieldDefinition>())
        {
            var field = entry.Entity;
            if (field.TenantId != tenantId)
            {
                continue;
            }

            if (entry.State == EntityState.Deleted || field.IsDeleted)
            {
                fieldsById.Remove(field.Id);
                continue;
            }

            fieldsById[field.Id] = field;
        }

        CheckoutFieldRuleEngine.ValidateRules(
            fieldsById.Values.ToArray(),
            (code, message) => new CheckoutFieldDefinitionException(code, message));
    }

    private static List<CheckoutFieldRuleDto>? NormalizeConditionalRules(List<CheckoutFieldRuleDto>? rules)
    {
        if (rules == null || rules.Count == 0)
        {
            return null;
        }

        if (rules.Count > 25)
        {
            throw new CheckoutFieldDefinitionException("FIELD_RULES_TOO_MANY", "A checkout field can define at most 25 conditional rules.");
        }

        var result = new List<CheckoutFieldRuleDto>(rules.Count);
        var ruleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            var ruleId = string.IsNullOrWhiteSpace(rule.RuleId)
                ? Guid.NewGuid().ToString("N")
                : NormalizeRequiredText(rule.RuleId, "FIELD_RULE_ID_INVALID", "Conditional rule id is invalid.", 64);
            if (!ruleIds.Add(ruleId))
            {
                throw new CheckoutFieldDefinitionException("FIELD_RULE_ID_DUPLICATE", "Conditional rule identifiers must be unique per field.");
            }

            var conditionMode = NormalizeRuleToken(rule.ConditionMode, CheckoutFieldRuleEngine.ConditionModeAll);
            var actions = (rule.Actions ?? new List<string>())
                .Select(action => NormalizeRuleToken(action, string.Empty))
                .Where(action => !string.IsNullOrWhiteSpace(action))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var conditions = (rule.Conditions ?? new List<CheckoutFieldRuleConditionDto>())
                .Select(condition => new CheckoutFieldRuleConditionDto
                {
                    SourceFieldId = condition.SourceFieldId,
                    Operator = NormalizeRuleToken(condition.Operator, string.Empty),
                    ComparisonValue = NormalizeOptionalText(condition.ComparisonValue, 1000)
                })
                .ToList();

            result.Add(new CheckoutFieldRuleDto
            {
                RuleId = ruleId,
                ConditionMode = conditionMode,
                Actions = actions,
                Conditions = conditions
            });
        }

        return result;
    }

    private static string NormalizeRuleToken(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim().Replace('-', '_').ToUpperInvariant();
    }

    private static List<CheckoutFieldOptionDto> NormalizeOptions(List<CheckoutFieldOptionDto>? options)
    {
        if (options == null || options.Count == 0)
        {
            throw new CheckoutFieldDefinitionException("FIELD_OPTIONS_REQUIRED", "Select and radio fields require at least one option.");
        }

        if (options.Count > 50)
        {
            throw new CheckoutFieldDefinitionException("FIELD_OPTIONS_TOO_MANY", "A field can define at most 50 options.");
        }

        var result = new List<CheckoutFieldOptionDto>(options.Count);
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in options)
        {
            var value = NormalizeRequiredText(option.Value, "FIELD_OPTION_VALUE_REQUIRED", "Option value is required.", 100);
            var label = NormalizeRequiredText(option.Label, "FIELD_OPTION_LABEL_REQUIRED", "Option label is required.", 120);
            if (!values.Add(value))
            {
                throw new CheckoutFieldDefinitionException("FIELD_OPTION_DUPLICATE", "Option values must be unique.");
            }

            result.Add(new CheckoutFieldOptionDto { Value = value, Label = label });
        }

        return result;
    }

    private static void ValidateLengthConfiguration(int? minLength, int? maxLength, int absoluteMax)
    {
        if (minLength is < 0 || maxLength is < 0)
        {
            throw new CheckoutFieldDefinitionException("FIELD_CONFIGURATION_INVALID", "Length constraints cannot be negative.");
        }

        if (maxLength > absoluteMax || minLength > absoluteMax)
        {
            throw new CheckoutFieldDefinitionException("FIELD_CONFIGURATION_INVALID", $"Length constraints cannot exceed {absoluteMax}.");
        }

        if (minLength.HasValue && maxLength.HasValue && minLength.Value > maxLength.Value)
        {
            throw new CheckoutFieldDefinitionException("FIELD_CONFIGURATION_INVALID", "Minimum length cannot be greater than maximum length.");
        }
    }

    private static List<string>? NormalizeMimeTypes(CheckoutFieldType fieldType, List<string>? mimeTypes)
    {
        if (mimeTypes == null || mimeTypes.Count == 0)
        {
            return null;
        }

        if (mimeTypes.Count > 50)
        {
            throw new CheckoutFieldDefinitionException("FIELD_CONFIGURATION_INVALID", "At most 50 MIME types are allowed.");
        }

        var result = new List<string>(mimeTypes.Count);
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mimeType in mimeTypes)
        {
            var normalized = NormalizeRequiredText(mimeType, "FIELD_CONFIGURATION_INVALID", "MIME type values are required.", 100).ToLowerInvariant();
            if (normalized == "image/jpg")
            {
                normalized = "image/jpeg";
            }
            var allowedMimeTypes = fieldType == CheckoutFieldType.File ? FileMimeTypes : ImageMimeTypes;
            if (!allowedMimeTypes.Contains(normalized))
            {
                throw new CheckoutFieldDefinitionException(
                    "FIELD_CONFIGURATION_INVALID",
                    $"MIME type '{normalized}' is not supported for {fieldType} checkout fields.");
            }
            if (!unique.Add(normalized))
            {
                throw new CheckoutFieldDefinitionException("FIELD_CONFIGURATION_INVALID", "MIME type values must be unique.");
            }

            result.Add(normalized);
        }

        return result;
    }

    private static long? ValidateMaxFileSize(long? maxFileSize)
    {
        if (!maxFileSize.HasValue)
        {
            return null;
        }

        if (maxFileSize.Value <= 0 || maxFileSize.Value > 104_857_600)
        {
            throw new CheckoutFieldDefinitionException("FIELD_CONFIGURATION_INVALID", "Max file size must be between 1 byte and 100 MB.");
        }

        return maxFileSize.Value;
    }

    private static int? ValidateMaxFiles(CheckoutFieldType fieldType, int? maxFiles)
    {
        if (fieldType != CheckoutFieldType.MultiImage)
        {
            if (maxFiles.HasValue && maxFiles.Value != 1)
            {
                throw new CheckoutFieldDefinitionException("FIELD_CONFIGURATION_INVALID", "Single upload fields can only allow one file.");
            }

            return maxFiles;
        }

        if (!maxFiles.HasValue)
        {
            return null;
        }

        if (maxFiles.Value <= 0 || maxFiles.Value > 50)
        {
            throw new CheckoutFieldDefinitionException("FIELD_CONFIGURATION_INVALID", "Max files must be between 1 and 50.");
        }

        return maxFiles.Value;
    }

    private static CheckoutFieldConfigurationDto DeserializeConfiguration(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<CheckoutFieldConfigurationDto>(json, JsonOptions)
                ?? new CheckoutFieldConfigurationDto();
        }
        catch (JsonException)
        {
            return new CheckoutFieldConfigurationDto();
        }
    }

    private static void ValidateRequiredEnabled(bool isEnabled, bool isRequired)
    {
        if (!isEnabled && isRequired)
        {
            throw new CheckoutFieldDefinitionException(
                "FIELD_REQUIRED_DISABLED_INVALID",
                "A disabled checkout field cannot be required.");
        }
    }

    private static SystemFieldContract GetSystemFieldContract(string? systemFieldKey)
    {
        if (string.IsNullOrWhiteSpace(systemFieldKey) ||
            !SystemFieldContracts.TryGetValue(systemFieldKey, out var contract))
        {
            throw new CheckoutFieldDefinitionException("SYSTEM_FIELD_KEY_INVALID", "System field key is not recognized.");
        }

        return contract;
    }

    private static CheckoutFieldType ParseFieldType(string value)
    {
        var normalized = NormalizeRequiredText(value, "FIELD_TYPE_REQUIRED", "Field type is required.", 32)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);

        foreach (var candidate in Enum.GetValues<CheckoutFieldType>())
        {
            if (string.Equals(candidate.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        throw new CheckoutFieldDefinitionException("FIELD_TYPE_INVALID", "Checkout field type is not supported.");
    }

    private static string NormalizeRequiredText(string value, string code, string message, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CheckoutFieldDefinitionException(code, message);
        }

        var normalized = value.Trim();
        RejectNullCharacter(normalized);
        if (normalized.Length > maxLength)
        {
            throw new CheckoutFieldDefinitionException("FIELD_VALUE_TOO_LONG", $"Value cannot exceed {maxLength} characters.");
        }

        return normalized;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        RejectNullCharacter(normalized);
        if (normalized.Length > maxLength)
        {
            throw new CheckoutFieldDefinitionException("FIELD_VALUE_TOO_LONG", $"Value cannot exceed {maxLength} characters.");
        }

        return normalized;
    }

    private static void RejectNullCharacter(string value)
    {
        if (value.Contains('\0'))
        {
            throw new CheckoutFieldDefinitionException(
                "FIELD_VALUE_INVALID_CHARACTER",
                "Checkout field text cannot contain the null character.");
        }
    }

    private static string ToApiValue(CheckoutFieldKind kind) => kind.ToString().ToUpperInvariant();

    private static string ToApiValue(CheckoutFieldType type) => type switch
    {
        CheckoutFieldType.MultiImage => "MULTI_IMAGE",
        _ => type.ToString().ToUpperInvariant()
    };

    private sealed record SystemFieldContract(
        string Key,
        CheckoutFieldType FieldType,
        string DefaultLabel,
        bool IsEnabledByDefault,
        bool IsRequiredByDefault,
        int DisplayOrder);
}
