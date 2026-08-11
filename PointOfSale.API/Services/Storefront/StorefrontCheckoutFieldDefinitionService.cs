using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.Storefront;

public sealed class StorefrontCheckoutFieldDefinitionService : IStorefrontCheckoutFieldDefinitionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<CheckoutFieldType> RuntimeSupportedFieldTypes = new()
    {
        CheckoutFieldType.Text,
        CheckoutFieldType.Textarea,
        CheckoutFieldType.Number,
        CheckoutFieldType.Email,
        CheckoutFieldType.Phone,
        CheckoutFieldType.Select,
        CheckoutFieldType.Radio,
        CheckoutFieldType.Checkbox,
        CheckoutFieldType.Date,
        CheckoutFieldType.Image,
        CheckoutFieldType.File,
        CheckoutFieldType.MultiImage
    };

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<StorefrontCheckoutFieldDefinitionService>? _logger;

    public StorefrontCheckoutFieldDefinitionService(
        ApplicationDbContext dbContext,
        ILogger<StorefrontCheckoutFieldDefinitionService>? logger = null)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<StorefrontCheckoutFieldDefinitionsResponse> GetFieldsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var configuredFields = await _dbContext.CheckoutFieldDefinitions
            .AsNoTracking()
            .Where(field => field.TenantId == tenantId)
            .OrderBy(field => field.DisplayOrder)
            .ThenBy(field => field.CreatedAt)
            .ThenBy(field => field.Id)
            .ToListAsync(cancellationToken);

        var runtimeFields = configuredFields
            .Where(field => field.IsEnabled && RuntimeSupportedFieldTypes.Contains(field.FieldType))
            .Select(Map)
            .ToArray();

        _logger?.LogInformation(
            "Storefront checkout field configuration loaded | TenantId: {TenantId} | TotalCount: {TotalCount} | EnabledCount: {EnabledCount} | UnsupportedEnabledCount: {UnsupportedEnabledCount} | Status: {Status}",
            tenantId,
            configuredFields.Count,
            runtimeFields.Length,
            configuredFields.Count(field => field.IsEnabled && !RuntimeSupportedFieldTypes.Contains(field.FieldType)),
            runtimeFields.Length > 0 ? "AVAILABLE" : "EMPTY");

        return new StorefrontCheckoutFieldDefinitionsResponse
        {
            Status = runtimeFields.Length > 0 ? "AVAILABLE" : "EMPTY",
            Fields = runtimeFields,
            TotalCount = configuredFields.Count,
            EnabledCount = runtimeFields.Length
        };
    }

    private static StorefrontCheckoutFieldDefinitionDto Map(CheckoutFieldDefinition field) => new()
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
        Configuration = DeserializeConfiguration(field.ConfigurationJson)
    };

    private static StorefrontCheckoutFieldConfigurationDto DeserializeConfiguration(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return new StorefrontCheckoutFieldConfigurationDto();
        }

        return JsonSerializer.Deserialize<StorefrontCheckoutFieldConfigurationDto>(configurationJson, JsonOptions)
            ?? throw new JsonException("Checkout field configuration cannot be null.");
    }

    private static string ToApiValue(CheckoutFieldKind kind) => kind switch
    {
        CheckoutFieldKind.System => "SYSTEM",
        CheckoutFieldKind.Custom => "CUSTOM",
        _ => kind.ToString().ToUpperInvariant()
    };

    private static string ToApiValue(CheckoutFieldType type) => type switch
    {
        CheckoutFieldType.Text => "TEXT",
        CheckoutFieldType.Textarea => "TEXTAREA",
        CheckoutFieldType.Number => "NUMBER",
        CheckoutFieldType.Email => "EMAIL",
        CheckoutFieldType.Phone => "PHONE",
        CheckoutFieldType.Select => "SELECT",
        CheckoutFieldType.Radio => "RADIO",
        CheckoutFieldType.Checkbox => "CHECKBOX",
        CheckoutFieldType.Date => "DATE",
        CheckoutFieldType.Image => "IMAGE",
        CheckoutFieldType.File => "FILE",
        CheckoutFieldType.MultiImage => "MULTI_IMAGE",
        _ => type.ToString().ToUpperInvariant()
    };
}
