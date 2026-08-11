using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Admin;

public sealed class CheckoutFieldDefinitionServiceTests
{
    [Fact]
    public async Task CreateCustomField_ThenReadFields_ReturnsTenantScopedCustomFieldWithoutImplicitInitialization()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var service = new CheckoutFieldDefinitionService(context);

        var created = await service.CreateCustomFieldAsync(
            tenantId,
            new CreateCheckoutFieldDefinitionRequest
            {
                FieldType = "TEXT",
                Label = "Company Name",
                HelpText = "Optional business name",
                IsEnabled = true,
                IsRequired = false,
                Configuration = new CheckoutFieldConfigurationDto { MaxLength = 120 }
            },
            actorUserId: Guid.NewGuid(),
            CancellationToken.None);

        var response = await service.GetFieldsAsync(tenantId, CancellationToken.None);

        Assert.Equal("CUSTOM", created.FieldKind);
        Assert.Equal("TEXT", created.FieldType);
        Assert.Null(created.SystemFieldKey);
        Assert.DoesNotContain(response.Fields, field => field.FieldKind == "SYSTEM");
        Assert.Contains(response.Fields, field => field.Id == created.Id && field.Label == "Company Name");
    }

    [Fact]
    public async Task GetFields_DoesNotMutateAnUninitializedTenant()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var service = new CheckoutFieldDefinitionService(context);

        var response = await service.GetFieldsAsync(tenantId, CancellationToken.None);

        Assert.Empty(response.Fields);
        Assert.Empty(await context.CheckoutFieldDefinitions.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task UpdateCustomField_CanToggleRequiredAndEnabledWithoutChangingIdentity()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var service = new CheckoutFieldDefinitionService(context);
        var created = await CreateTextFieldAsync(service, tenantId);

        var updated = await service.UpdateFieldAsync(
            tenantId,
            created.Id,
            new UpdateCheckoutFieldDefinitionRequest
            {
                Label = "Business Name",
                IsEnabled = true,
                IsRequired = true,
                DisplayOrder = 5
            },
            actorUserId: Guid.NewGuid(),
            CancellationToken.None);

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Business Name", updated.Label);
        Assert.True(updated.IsEnabled);
        Assert.True(updated.IsRequired);
        Assert.Equal(5, updated.DisplayOrder);
    }

    [Fact]
    public async Task DisabledField_CannotBeRequired()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var service = new CheckoutFieldDefinitionService(context);

        var ex = await Assert.ThrowsAsync<CheckoutFieldDefinitionException>(() =>
            service.CreateCustomFieldAsync(
                tenantId,
                new CreateCheckoutFieldDefinitionRequest
                {
                    FieldType = "TEXT",
                    Label = "Invalid",
                    IsEnabled = false,
                    IsRequired = true
                },
                null,
                CancellationToken.None));

        Assert.Equal("FIELD_REQUIRED_DISABLED_INVALID", ex.Code);
    }

    [Fact]
    public async Task CreateCustomField_RejectsPostgresIncompatibleNullCharacter()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var service = new CheckoutFieldDefinitionService(context);

        var ex = await Assert.ThrowsAsync<CheckoutFieldDefinitionException>(() =>
            service.CreateCustomFieldAsync(
                tenantId,
                new CreateCheckoutFieldDefinitionRequest
                {
                    FieldType = "TEXT",
                    Label = "Invalid\0Label",
                    IsEnabled = true
                },
                null,
                CancellationToken.None));

        Assert.Equal("FIELD_VALUE_INVALID_CHARACTER", ex.Code);
    }

    [Fact]
    public async Task UpdateCustomField_EmptyHelpTextClearsExistingValue()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var service = new CheckoutFieldDefinitionService(context);
        var created = await service.CreateCustomFieldAsync(
            tenantId,
            new CreateCheckoutFieldDefinitionRequest
            {
                FieldType = "TEXT",
                Label = "Company",
                HelpText = "Existing help",
                IsEnabled = true
            },
            null,
            CancellationToken.None);

        var updated = await service.UpdateFieldAsync(
            tenantId,
            created.Id,
            new UpdateCheckoutFieldDefinitionRequest { HelpText = string.Empty },
            null,
            CancellationToken.None);

        Assert.Null(updated.HelpText);
    }

    [Fact]
    public async Task ReorderFields_UsesExplicitDeterministicDisplayOrder()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var service = new CheckoutFieldDefinitionService(context);
        var first = await CreateTextFieldAsync(service, tenantId, "First");
        var second = await CreateTextFieldAsync(service, tenantId, "Second");

        var response = await service.ReorderFieldsAsync(
            tenantId,
            new ReorderCheckoutFieldDefinitionsRequest
            {
                Fields = new List<CheckoutFieldOrderDto>
                {
                    new() { Id = second.Id, DisplayOrder = 1 },
                    new() { Id = first.Id, DisplayOrder = 2 }
                }
            },
            null,
            CancellationToken.None);

        var customFields = response.Fields.Where(field => field.FieldKind == "CUSTOM").ToArray();
        Assert.Equal(second.Id, customFields[0].Id);
        Assert.Equal(first.Id, customFields[1].Id);
    }

    [Fact]
    public async Task TenantIsolation_PreventsReadUpdateDeleteAndReorderAcrossTenants()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var root = new InMemoryDatabaseRoot();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        Guid tenantAFieldId;
        await using (var contextA = CreateDbContext(tenantA, databaseName, root))
        {
            var serviceA = new CheckoutFieldDefinitionService(contextA);
            tenantAFieldId = (await CreateTextFieldAsync(serviceA, tenantA, "Tenant A Field")).Id;
        }

        await using var contextB = CreateDbContext(tenantB, databaseName, root);
        var serviceB = new CheckoutFieldDefinitionService(contextB);

        var tenantBFields = await serviceB.GetFieldsAsync(tenantB, CancellationToken.None);
        Assert.DoesNotContain(tenantBFields.Fields, field => field.Id == tenantAFieldId);

        await Assert.ThrowsAsync<CheckoutFieldDefinitionException>(() => serviceB.UpdateFieldAsync(
            tenantB,
            tenantAFieldId,
            new UpdateCheckoutFieldDefinitionRequest { Label = "Cross tenant" },
            null,
            CancellationToken.None));
        await Assert.ThrowsAsync<CheckoutFieldDefinitionException>(() => serviceB.ArchiveCustomFieldAsync(
            tenantB,
            tenantAFieldId,
            null,
            CancellationToken.None));
        await Assert.ThrowsAsync<CheckoutFieldDefinitionException>(() => serviceB.ReorderFieldsAsync(
            tenantB,
            new ReorderCheckoutFieldDefinitionsRequest
            {
                Fields = new List<CheckoutFieldOrderDto> { new() { Id = tenantAFieldId, DisplayOrder = 1 } }
            },
            null,
            CancellationToken.None));
    }

    [Fact]
    public async Task DuplicateActiveSystemField_PreventsInitialization()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        context.CheckoutFieldDefinitions.AddRange(
            SystemField(tenantId, "phone"),
            SystemField(tenantId, "phone"));
        await context.SaveChangesAsync();
        var service = new CheckoutFieldDefinitionService(context);

        var ex = await Assert.ThrowsAsync<CheckoutFieldDefinitionException>(() =>
            service.InitializeSystemFieldsAsync(tenantId, null, CancellationToken.None));

        Assert.Equal("DUPLICATE_SYSTEM_FIELD", ex.Code);
    }

    [Theory]
    [InlineData("SELECT")]
    [InlineData("RADIO")]
    public async Task SelectAndRadio_RejectMissingOptions(string fieldType)
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var service = new CheckoutFieldDefinitionService(context);

        var ex = await Assert.ThrowsAsync<CheckoutFieldDefinitionException>(() =>
            service.CreateCustomFieldAsync(
                tenantId,
                new CreateCheckoutFieldDefinitionRequest { FieldType = fieldType, Label = "Choice" },
                null,
                CancellationToken.None));

        Assert.Equal("FIELD_OPTIONS_REQUIRED", ex.Code);
    }

    [Fact]
    public async Task NumberField_RejectsInvalidMinMaxConfiguration()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var service = new CheckoutFieldDefinitionService(context);

        var ex = await Assert.ThrowsAsync<CheckoutFieldDefinitionException>(() =>
            service.CreateCustomFieldAsync(
                tenantId,
                new CreateCheckoutFieldDefinitionRequest
                {
                    FieldType = "NUMBER",
                    Label = "Age",
                    Configuration = new CheckoutFieldConfigurationDto { Min = 10, Max = 5 }
                },
                null,
                CancellationToken.None));

        Assert.Equal("FIELD_CONFIGURATION_INVALID", ex.Code);
    }

    [Fact]
    public async Task ConditionalRule_CreateReadUpdateDelete_RoundTripsThroughConfiguration()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var service = new CheckoutFieldDefinitionService(context);
        var source = await service.CreateCustomFieldAsync(
            tenantId,
            new CreateCheckoutFieldDefinitionRequest
            {
                FieldType = "RADIO",
                Label = "Need custom printing?",
                Configuration = new CheckoutFieldConfigurationDto
                {
                    Options = new List<CheckoutFieldOptionDto>
                    {
                        new() { Value = "yes", Label = "Yes" },
                        new() { Value = "no", Label = "No" }
                    }
                }
            },
            null,
            CancellationToken.None);

        var target = await service.CreateCustomFieldAsync(
            tenantId,
            new CreateCheckoutFieldDefinitionRequest
            {
                FieldType = "IMAGE",
                Label = "Upload design",
                Configuration = new CheckoutFieldConfigurationDto
                {
                    ConditionalRules = new List<CheckoutFieldRuleDto>
                    {
                        new()
                        {
                            Conditions = new List<CheckoutFieldRuleConditionDto>
                            {
                                new() { SourceFieldId = source.Id, Operator = "EQUALS", ComparisonValue = "yes" }
                            },
                            Actions = new List<string> { "SHOW", "REQUIRE" }
                        }
                    },
                    AllowedMimeTypes = new List<string> { "image/png" },
                    MaxFiles = 1
                }
            },
            null,
            CancellationToken.None);

        var response = await service.GetFieldsAsync(tenantId, CancellationToken.None);
        var roundTripped = response.Fields.Single(field => field.Id == target.Id);

        Assert.Single(roundTripped.Configuration.ConditionalRules!);
        Assert.Equal(source.Id, roundTripped.Configuration.ConditionalRules![0].Conditions[0].SourceFieldId);
        Assert.Contains("REQUIRE", roundTripped.Configuration.ConditionalRules![0].Actions);

        var updated = await service.UpdateFieldAsync(
            tenantId,
            target.Id,
            new UpdateCheckoutFieldDefinitionRequest { Configuration = new CheckoutFieldConfigurationDto { AllowedMimeTypes = new List<string> { "image/jpeg" }, MaxFiles = 1 } },
            null,
            CancellationToken.None);

        Assert.Null(updated.Configuration.ConditionalRules);
    }

    [Fact]
    public async Task ConditionalRule_RejectsInvalidSourceAndSelfReference()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var service = new CheckoutFieldDefinitionService(context);
        var target = await CreateTextFieldAsync(service, tenantId, "Target");

        var invalidSource = await Assert.ThrowsAsync<CheckoutFieldDefinitionException>(() => service.UpdateFieldAsync(
            tenantId,
            target.Id,
            new UpdateCheckoutFieldDefinitionRequest
            {
                Configuration = new CheckoutFieldConfigurationDto
                {
                    ConditionalRules = new List<CheckoutFieldRuleDto>
                    {
                        new()
                        {
                            Conditions = new List<CheckoutFieldRuleConditionDto> { new() { SourceFieldId = Guid.NewGuid(), Operator = "EQUALS", ComparisonValue = "yes" } },
                            Actions = new List<string> { "SHOW" }
                        }
                    }
                }
            },
            null,
            CancellationToken.None));
        Assert.Equal("FIELD_RULE_SOURCE_INVALID", invalidSource.Code);

        var selfReference = await Assert.ThrowsAsync<CheckoutFieldDefinitionException>(() => service.UpdateFieldAsync(
            tenantId,
            target.Id,
            new UpdateCheckoutFieldDefinitionRequest
            {
                Configuration = new CheckoutFieldConfigurationDto
                {
                    ConditionalRules = new List<CheckoutFieldRuleDto>
                    {
                        new()
                        {
                            Conditions = new List<CheckoutFieldRuleConditionDto> { new() { SourceFieldId = target.Id, Operator = "EQUALS", ComparisonValue = "yes" } },
                            Actions = new List<string> { "SHOW" }
                        }
                    }
                }
            },
            null,
            CancellationToken.None));
        Assert.Equal("FIELD_RULE_SELF_REFERENCE", selfReference.Code);
    }

    [Fact]
    public async Task DefaultSystemFieldInitialization_IsIdempotent()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var service = new CheckoutFieldDefinitionService(context);

        var first = await service.InitializeSystemFieldsAsync(tenantId, null, CancellationToken.None);
        var second = await service.InitializeSystemFieldsAsync(tenantId, null, CancellationToken.None);

        Assert.Equal(10, first.Fields.Count(field => field.FieldKind == "SYSTEM"));
        Assert.Equal(10, second.Fields.Count(field => field.FieldKind == "SYSTEM"));
        Assert.Equal(
            first.Fields.Where(field => field.FieldKind == "SYSTEM").Select(field => field.Id).OrderBy(id => id),
            second.Fields.Where(field => field.FieldKind == "SYSTEM").Select(field => field.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task CreateUploadField_RejectsMimeTypeUnsupportedByRuntime()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var service = new CheckoutFieldDefinitionService(context);

        var ex = await Assert.ThrowsAsync<CheckoutFieldDefinitionException>(() =>
            service.CreateCustomFieldAsync(
                tenantId,
                new CreateCheckoutFieldDefinitionRequest
                {
                    FieldType = "IMAGE",
                    Label = "Image",
                    IsEnabled = true,
                    Configuration = new CheckoutFieldConfigurationDto
                    {
                        AllowedMimeTypes = new List<string> { "image/gif" }
                    }
                },
                null,
                CancellationToken.None));

        Assert.Equal("FIELD_CONFIGURATION_INVALID", ex.Code);
    }

    [Fact]
    public async Task SystemAndCustomLifecycle_RetainsSystemIdentityAndArchivesCustomOnly()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var service = new CheckoutFieldDefinitionService(context);
        var fields = await service.InitializeSystemFieldsAsync(tenantId, null, CancellationToken.None);
        var phone = fields.Fields.Single(field => field.SystemFieldKey == "phone");
        var custom = await CreateTextFieldAsync(service, tenantId, "Company Name");

        var deleteSystem = await Assert.ThrowsAsync<CheckoutFieldDefinitionException>(() =>
            service.ArchiveCustomFieldAsync(tenantId, phone.Id, null, CancellationToken.None));
        Assert.Equal("SYSTEM_FIELD_DELETE_FORBIDDEN", deleteSystem.Code);

        var mutateSystemType = await Assert.ThrowsAsync<CheckoutFieldDefinitionException>(() =>
            service.UpdateFieldAsync(
                tenantId,
                phone.Id,
                new UpdateCheckoutFieldDefinitionRequest { FieldType = "EMAIL" },
                null,
                CancellationToken.None));
        Assert.Equal("SYSTEM_FIELD_TYPE_IMMUTABLE", mutateSystemType.Code);

        await service.ArchiveCustomFieldAsync(tenantId, custom.Id, null, CancellationToken.None);
        var afterArchive = await service.GetFieldsAsync(tenantId, CancellationToken.None);
        Assert.Contains(afterArchive.Fields, field => field.SystemFieldKey == "phone" && field.IsEnabled);
        Assert.DoesNotContain(afterArchive.Fields, field => field.Id == custom.Id);
    }

    [Fact]
    public void AdminCheckoutFieldController_UsesPosAdminAuthorizationOnly()
    {
        var authorize = typeof(AdminCheckoutFieldDefinitionsController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Owner,Admin", authorize!.Roles);
        Assert.DoesNotContain(
            typeof(AdminCheckoutFieldDefinitionsController).GetCustomAttributes().Select(attribute => attribute.GetType().Name),
            name => string.Equals(name, "AllowAnonymousAttribute", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StorefrontRead_WhenNoDefinitions_ReturnsEmptyWithoutInitializingSystemFields()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var service = new StorefrontCheckoutFieldDefinitionService(context);

        var response = await service.GetFieldsAsync(tenantId, CancellationToken.None);

        Assert.Equal("EMPTY", response.Status);
        Assert.Empty(response.Fields);
        Assert.Equal(0, await context.CheckoutFieldDefinitions.CountAsync(field => field.TenantId == tenantId));
    }

    [Fact]
    public async Task StorefrontRead_ReturnsEnabledRuntimeFieldsInDisplayOrderOnly()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateDbContext(tenantId);
        var textField = CustomField(tenantId, CheckoutFieldType.Text, "Gift Message", displayOrder: 20);
        var phoneField = SystemField(tenantId, "phone");
        phoneField.DisplayOrder = 10;
        var uploadField = CustomField(tenantId, CheckoutFieldType.File, "Upload", displayOrder: 30);
        var disabledPostalCode = SystemField(tenantId, "postal_code");
        disabledPostalCode.Id = Guid.NewGuid();
        disabledPostalCode.FieldType = CheckoutFieldType.Text;
        disabledPostalCode.Label = "Postal Code";
        disabledPostalCode.IsEnabled = false;
        disabledPostalCode.IsRequired = false;
        disabledPostalCode.DisplayOrder = 25;
        context.CheckoutFieldDefinitions.AddRange(textField, phoneField, disabledPostalCode, uploadField);
        await context.SaveChangesAsync();
        var service = new StorefrontCheckoutFieldDefinitionService(context);

        var response = await service.GetFieldsAsync(tenantId, CancellationToken.None);

        Assert.Equal("AVAILABLE", response.Status);
        Assert.Equal(new[] { phoneField.Id, textField.Id, uploadField.Id }, response.Fields.Select(field => field.Id).ToArray());
        Assert.Equal(4, response.TotalCount);
        Assert.Equal(3, response.EnabledCount);
        Assert.DoesNotContain(response.Fields, field => field.Id == disabledPostalCode.Id);
        Assert.Contains(response.Fields, field => field.Id == uploadField.Id && field.FieldType == "FILE");
        Assert.All(response.Fields, field => Assert.Null(field.GetType().GetProperty("CreatedAt")));
    }

    private static async Task<CheckoutFieldDefinitionDto> CreateTextFieldAsync(
        CheckoutFieldDefinitionService service,
        Guid tenantId,
        string label = "Company Name") =>
        await service.CreateCustomFieldAsync(
            tenantId,
            new CreateCheckoutFieldDefinitionRequest
            {
                FieldType = "TEXT",
                Label = label,
                IsEnabled = true,
                IsRequired = false,
                Configuration = new CheckoutFieldConfigurationDto { MaxLength = 120 }
            },
            null,
            CancellationToken.None);

    private static CheckoutFieldDefinition SystemField(Guid tenantId, string key) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        FieldKind = CheckoutFieldKind.System,
        FieldType = CheckoutFieldType.Phone,
        SystemFieldKey = key,
        Label = "Phone Number",
        IsEnabled = true,
        IsRequired = true,
        DisplayOrder = 10,
        ConfigurationSchemaVersion = 1,
        ConfigurationJson = "{\"schemaVersion\":1}",
        CreatedAt = DateTime.UtcNow
    };

    private static CheckoutFieldDefinition CustomField(
        Guid tenantId,
        CheckoutFieldType type,
        string label,
        int displayOrder) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        FieldKind = CheckoutFieldKind.Custom,
        FieldType = type,
        SystemFieldKey = null,
        Label = label,
        IsEnabled = true,
        IsRequired = false,
        DisplayOrder = displayOrder,
        ConfigurationSchemaVersion = 1,
        ConfigurationJson = "{\"schemaVersion\":1}",
        CreatedAt = DateTime.UtcNow
    };

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        return CreateDbContext(tenantId, Guid.NewGuid().ToString("N"), new InMemoryDatabaseRoot());
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId, string databaseName, InMemoryDatabaseRoot root)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, root)
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }
}
