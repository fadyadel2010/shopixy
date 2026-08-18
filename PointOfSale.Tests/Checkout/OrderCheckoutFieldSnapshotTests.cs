using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.Services.Checkout;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Checkout;

public class OrderCheckoutFieldSnapshotTests
{
    [Fact]
    public void SnapshotDraft_Preserves_CustomFieldDefinition_AsItExistedAtOrderCreation()
    {
        var fieldId = Guid.NewGuid();
        var definition = new CheckoutFieldDefinition
        {
            Id = fieldId,
            TenantId = Guid.NewGuid(),
            FieldKind = CheckoutFieldKind.Custom,
            FieldType = CheckoutFieldType.Select,
            Label = "T-Shirt Size",
            HelpText = "Choose your shirt size",
            IsEnabled = true,
            IsRequired = true,
            DisplayOrder = 10,
            ConfigurationSchemaVersion = 1,
            ConfigurationJson = "{\"schemaVersion\":1,\"options\":[{\"value\":\"small\",\"label\":\"Small\"},{\"value\":\"medium\",\"label\":\"Medium\"},{\"value\":\"large\",\"label\":\"Large\"}]}"
        };

        var draft = BuildSnapshotDraft(definition, "large");

        definition.Label = "Shirt Size";
        definition.ConfigurationJson = "{\"schemaVersion\":1,\"options\":[{\"value\":\"s\",\"label\":\"S\"},{\"value\":\"m\",\"label\":\"M\"},{\"value\":\"l\",\"label\":\"L\"},{\"value\":\"xl\",\"label\":\"XL\"}]}";

        Assert.Equal(fieldId, Get<Guid>(draft, "CheckoutFieldDefinitionId"));
        Assert.Equal("T-Shirt Size", Get<string>(draft, "Label"));
        Assert.Contains("\"value\":\"large\"", Get<string>(draft, "ConfigurationJson"));
        Assert.DoesNotContain("\"value\":\"xl\"", Get<string>(draft, "ConfigurationJson"));
        Assert.Equal("\"large\"", Get<string>(draft, "ValueJson"));
    }

    [Fact]
    public void SnapshotDraft_Preserves_SystemFieldPresentation_WithoutReplacingCanonicalOrderValue()
    {
        var definition = new CheckoutFieldDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            FieldKind = CheckoutFieldKind.System,
            FieldType = CheckoutFieldType.Phone,
            SystemFieldKey = "phone",
            Label = "WhatsApp Number",
            IsEnabled = true,
            IsRequired = true,
            DisplayOrder = 20,
            ConfigurationSchemaVersion = 1,
            ConfigurationJson = "{}"
        };

        var draft = BuildSnapshotDraft(definition, "+20100111222");
        definition.Label = "Phone Number";

        Assert.Equal(CheckoutFieldKind.System, Get<CheckoutFieldKind>(draft, "FieldKind"));
        Assert.Equal("phone", Get<string>(draft, "SystemFieldKey"));
        Assert.Equal("WhatsApp Number", Get<string>(draft, "Label"));
        using var value = JsonDocument.Parse(Get<string>(draft, "ValueJson"));
        Assert.Equal("+20100111222", value.RootElement.GetString());
    }

    [Fact]
    public void SnapshotValueJson_Preserves_TypedValueSemantics()
    {
        Assert.Equal(JsonValueKind.String, ParseValue(CheckoutFieldType.Select, "large").ValueKind);
        Assert.Equal("large", ParseValue(CheckoutFieldType.Radio, "large").GetString());
        Assert.Equal(42.5m, ParseValue(CheckoutFieldType.Number, "42.50").GetDecimal());
        Assert.True(ParseValue(CheckoutFieldType.Checkbox, "true").GetBoolean());
        Assert.False(ParseValue(CheckoutFieldType.Checkbox, "0").GetBoolean());
        Assert.Equal("2026-08-09", ParseValue(CheckoutFieldType.Date, "2026-08-09").GetString());
        Assert.Equal(JsonValueKind.Null, ParseValue(CheckoutFieldType.Text, " ").ValueKind);
    }

    [Fact]
    public void SnapshotDraft_Preserves_UploadFieldAttachmentMetadata_AsHistoricalRecord()
    {
        var fieldId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var definition = new CheckoutFieldDefinition
        {
            Id = fieldId,
            TenantId = Guid.NewGuid(),
            FieldKind = CheckoutFieldKind.Custom,
            FieldType = CheckoutFieldType.Image,
            Label = "Upload your design",
            HelpText = "Upload the final artwork",
            IsEnabled = true,
            IsRequired = true,
            DisplayOrder = 10,
            ConfigurationSchemaVersion = 1,
            ConfigurationJson = "{\"schemaVersion\":1,\"allowedMimeTypes\":[\"image/jpeg\"],\"maxFileSize\":5242880,\"maxFiles\":1}"
        };
        var attachment = new CheckoutAttachment
        {
            Id = attachmentId,
            TenantId = definition.TenantId,
            CartId = Guid.NewGuid(),
            CheckoutFieldDefinitionId = fieldId,
            ObjectKey = "tenants/store/checkout/cart/fields/design/attachments/server.jpg",
            OriginalFileName = "design.jpg",
            ContentType = "image/jpeg",
            MediaType = "image",
            SizeBytes = 1024,
            UploadedAtUtc = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc),
            FinalObjectKey = "tenants/store/checkout-final/cart/fields/design/attachments/server.jpg",
            FinalizedAtUtc = new DateTime(2026, 8, 9, 12, 1, 0, DateTimeKind.Utc),
            FinalETag = "final-etag",
            FinalChecksumSha256 = "final-sha256"
        };

        var valueJson = BuildAttachmentSnapshotValueJson(new[] { attachment });
        var draft = BuildSnapshotDraftFromJson(definition, valueJson);

        definition.Label = "Archived design upload";
        definition.IsEnabled = false;
        definition.ConfigurationJson = "{\"schemaVersion\":1,\"allowedMimeTypes\":[\"image/png\"],\"maxFileSize\":128,\"maxFiles\":1}";

        Assert.Equal(fieldId, Get<Guid>(draft, "CheckoutFieldDefinitionId"));
        Assert.Equal("Upload your design", Get<string>(draft, "Label"));
        Assert.Equal(CheckoutFieldType.Image, Get<CheckoutFieldType>(draft, "FieldType"));
        Assert.Contains("\"allowedMimeTypes\":[\"image/jpeg\"]", Get<string>(draft, "ConfigurationJson"));
        Assert.DoesNotContain("image/png", Get<string>(draft, "ConfigurationJson"));
        using var value = JsonDocument.Parse(Get<string>(draft, "ValueJson"));
        var item = value.RootElement.EnumerateArray().Single();
        Assert.Equal(attachmentId, item.GetProperty("attachmentId").GetGuid());
        Assert.Equal("design.jpg", item.GetProperty("fileName").GetString());
        Assert.Equal("image/jpeg", item.GetProperty("contentType").GetString());
        Assert.Equal(1024, item.GetProperty("sizeBytes").GetInt64());
        Assert.Equal("image", item.GetProperty("mediaType").GetString());
        Assert.Equal("final-etag", item.GetProperty("finalETag").GetString());
        Assert.Equal("final-sha256", item.GetProperty("finalChecksumSha256").GetString());
        Assert.False(item.TryGetProperty("objectKey", out _));
    }

    [Fact]
    public void SnapshotDraft_Preserves_ConditionalRuleConfiguration_AsHistoricalContext()
    {
        var sourceId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var definition = new CheckoutFieldDefinition
        {
            Id = targetId,
            TenantId = Guid.NewGuid(),
            FieldKind = CheckoutFieldKind.Custom,
            FieldType = CheckoutFieldType.Image,
            Label = "Upload your design",
            IsEnabled = true,
            IsRequired = false,
            DisplayOrder = 20,
            ConfigurationSchemaVersion = 1,
            ConfigurationJson = $"{{\"schemaVersion\":1,\"conditionalRules\":[{{\"ruleId\":\"rule1\",\"conditionMode\":\"ALL\",\"conditions\":[{{\"sourceFieldId\":\"{sourceId}\",\"operator\":\"EQUALS\",\"comparisonValue\":\"yes\"}}],\"actions\":[\"SHOW\",\"REQUIRE\"]}}]}}"
        };

        var draft = BuildSnapshotDraftFromJson(definition, "[]");
        definition.ConfigurationJson = "{\"schemaVersion\":1}";

        var snapshotConfiguration = Get<string>(draft, "ConfigurationJson");
        Assert.Contains("conditionalRules", snapshotConfiguration);
        Assert.Contains(sourceId.ToString(), snapshotConfiguration);
        Assert.Contains("REQUIRE", snapshotConfiguration);
    }

    [Fact]
    public void SnapshotDraft_Preserves_EffectiveConditionalState()
    {
        var definition = new CheckoutFieldDefinition
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            FieldKind = CheckoutFieldKind.Custom,
            FieldType = CheckoutFieldType.Text,
            Label = "Tax number",
            IsEnabled = true,
            IsRequired = false,
            DisplayOrder = 10,
            ConfigurationSchemaVersion = 1,
            ConfigurationJson = "{\"schemaVersion\":1}"
        };

        var draft = BuildSnapshotDraft(definition, "123", required: true);

        Assert.True(Get<bool>(draft, "IsRequired"));
        Assert.True(Get<bool>(draft, "IsEnabled"));
    }

    [Fact]
    public void SnapshotEntity_DoesNot_ForeignKey_ToMutableCheckoutFieldDefinition()
    {
        var properties = typeof(OrderCheckoutFieldSnapshot).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Assert.Contains(properties, property => property.Name == nameof(OrderCheckoutFieldSnapshot.CheckoutFieldDefinitionId));
        Assert.DoesNotContain(properties, property => property.PropertyType == typeof(CheckoutFieldDefinition));
    }

    [Fact]
    public async Task SnapshotPersistence_Allows_Insert()
    {
        await using var context = CreateContext();
        var snapshot = NewSnapshot(Guid.NewGuid(), Guid.NewGuid(), "Original label", "\"original\"");

        context.OrderCheckoutFieldSnapshots.Add(snapshot);
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        var persisted = await context.OrderCheckoutFieldSnapshots
            .IgnoreQueryFilters()
            .SingleAsync(item => item.Id == snapshot.Id);
        Assert.Equal("Original label", persisted.Label);
        Assert.Equal("\"original\"", persisted.ValueJson);
    }

    [Fact]
    public async Task SnapshotPersistence_Rejects_Update_After_Insert()
    {
        await using var context = CreateContext();
        var snapshot = NewSnapshot(Guid.NewGuid(), Guid.NewGuid(), "Original label", "\"original\"");
        context.OrderCheckoutFieldSnapshots.Add(snapshot);
        await context.SaveChangesAsync();

        snapshot.Label = "Mutated label";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains(nameof(OrderCheckoutFieldSnapshot), ex.Message);
        Assert.Contains("cannot be updated", ex.Message);
    }

    [Fact]
    public async Task SnapshotPersistence_Rejects_Delete_After_Insert()
    {
        await using var context = CreateContext();
        var snapshot = NewSnapshot(Guid.NewGuid(), Guid.NewGuid(), "Original label", "\"original\"");
        context.OrderCheckoutFieldSnapshots.Add(snapshot);
        await context.SaveChangesAsync();

        context.OrderCheckoutFieldSnapshots.Remove(snapshot);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains(nameof(OrderCheckoutFieldSnapshot), ex.Message);
        Assert.Contains("cannot be deleted", ex.Message);
    }

    [Fact]
    public async Task SnapshotPersistence_RejectedMutation_Leaves_Historical_Values_Unchanged()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantId = Guid.NewGuid();
        var snapshotId = Guid.NewGuid();
        await using (var context = CreateContext(databaseName))
        {
            context.OrderCheckoutFieldSnapshots.Add(NewSnapshot(tenantId, Guid.NewGuid(), "Original label", "\"original\"", snapshotId));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(databaseName))
        {
            var snapshot = await context.OrderCheckoutFieldSnapshots
                .IgnoreQueryFilters()
                .SingleAsync(item => item.Id == snapshotId);
            snapshot.ConfigurationJson = "{\"schemaVersion\":1,\"options\":[{\"value\":\"changed\",\"label\":\"Changed\"}]}";
            snapshot.ValueJson = "\"changed\"";
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        await using (var context = CreateContext(databaseName))
        {
            var persisted = await context.OrderCheckoutFieldSnapshots
                .IgnoreQueryFilters()
                .SingleAsync(item => item.Id == snapshotId);
            Assert.Equal("Original label", persisted.Label);
            Assert.Equal("\"original\"", persisted.ValueJson);
            Assert.Contains("\"value\":\"original\"", persisted.ConfigurationJson);
            Assert.DoesNotContain("changed", persisted.ConfigurationJson);
        }
    }

    [Fact]
    public async Task SnapshotPersistence_Tenant_Filter_Remains_Intact()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using (var context = CreateContext(databaseName, tenantA))
        {
            context.OrderCheckoutFieldSnapshots.Add(NewSnapshot(tenantA, Guid.NewGuid(), "Tenant A", "\"a\""));
            context.OrderCheckoutFieldSnapshots.Add(NewSnapshot(tenantB, Guid.NewGuid(), "Tenant B", "\"b\""));
            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext(databaseName, tenantA))
        {
            var visible = await context.OrderCheckoutFieldSnapshots.ToListAsync();
            Assert.Single(visible);
            Assert.Equal(tenantA, visible[0].TenantId);
        }
    }

    [Fact]
    public void CheckoutService_Revalidates_And_Snapshots_Inside_OrderCreationTransaction()
    {
        var source = ReadRepositoryFile("PointOfSale.API", "Services", "Checkout", "CheckoutService.cs");
        var transactionStart = source.IndexOf("IsolationLevel.Serializable", StringComparison.Ordinal);
        var revalidationCall = source.IndexOf("var transactionScopedAuthority = await RevalidateCheckoutAuthorityInsideTransactionAsync", transactionStart, StringComparison.Ordinal);
        var revalidationMethodStart = source.IndexOf("private async Task<TransactionScopedCheckoutAuthoritySnapshot> RevalidateCheckoutAuthorityInsideTransactionAsync", StringComparison.Ordinal);
        var transactionScopedValidation = source.IndexOf("var checkoutFieldSnapshots = await ValidateCheckoutFieldDefinitionsAsync", revalidationMethodStart, StringComparison.Ordinal);
        var returnsAdmission = source.IndexOf("pricing,\r\n            checkoutFieldSnapshots", transactionScopedValidation, StringComparison.Ordinal);
        if (returnsAdmission < 0)
        {
            returnsAdmission = source.IndexOf("pricing,\n            checkoutFieldSnapshots", transactionScopedValidation, StringComparison.Ordinal);
        }
        var orderAdd = source.IndexOf("_dbContext.Orders.Add(order);", transactionStart, StringComparison.Ordinal);
        var firstOrderSave = source.IndexOf("await _dbContext.SaveChangesAsync(cancellationToken);", orderAdd, StringComparison.Ordinal);
        var postOrderAttachmentValidation = source.IndexOf("var checkoutFieldSnapshots = await ValidateCheckoutFieldDefinitionsAsync", firstOrderSave, StringComparison.Ordinal);
        var postOrderSnapshotRefresh = source.IndexOf("RefreshOrderCheckoutFieldSnapshots(order, checkoutFieldSnapshots);", postOrderAttachmentValidation, StringComparison.Ordinal);
        var bindReservations = source.IndexOf("_variantStockRepository.BindOrderReservationsAsync", postOrderSnapshotRefresh, StringComparison.Ordinal);
        var prematureSnapshotRefresh = source.IndexOf("RefreshOrderCheckoutFieldSnapshots", revalidationCall, orderAdd - revalidationCall, StringComparison.Ordinal);

        Assert.True(transactionStart >= 0);
        Assert.True(revalidationCall > transactionStart);
        Assert.True(transactionScopedValidation >= 0);
        Assert.True(returnsAdmission > transactionScopedValidation);
        Assert.True(orderAdd > revalidationCall);
        Assert.True(firstOrderSave > orderAdd);
        Assert.True(postOrderAttachmentValidation > firstOrderSave);
        Assert.True(postOrderSnapshotRefresh > postOrderAttachmentValidation);
        Assert.True(bindReservations > postOrderSnapshotRefresh);
        Assert.True(prematureSnapshotRefresh < 0);
    }

    [Fact]
    public void CheckoutService_DoesNot_Attach_Uploads_Before_OrderRowExists()
    {
        var source = ReadRepositoryFile("PointOfSale.API", "Services", "Checkout", "CheckoutService.cs");
        var revalidationStart = source.IndexOf("private async Task<TransactionScopedCheckoutAuthoritySnapshot> RevalidateCheckoutAuthorityInsideTransactionAsync", StringComparison.Ordinal);
        var revalidationEnd = source.IndexOf("private void RefreshOrderAuthoritySnapshot", revalidationStart, StringComparison.Ordinal);
        var revalidationMethod = source[revalidationStart..revalidationEnd];

        Assert.Contains("orderId: null", revalidationMethod);

        var orderAdd = source.IndexOf("_dbContext.Orders.Add(order);", StringComparison.Ordinal);
        var firstOrderSave = source.IndexOf("await _dbContext.SaveChangesAsync(cancellationToken);", orderAdd, StringComparison.Ordinal);
        var attachmentClaim = source.IndexOf("order.Id,\r\n                cancellationToken", firstOrderSave, StringComparison.Ordinal);
        if (attachmentClaim < 0)
        {
            attachmentClaim = source.IndexOf("order.Id,\n                cancellationToken", firstOrderSave, StringComparison.Ordinal);
        }

        Assert.True(orderAdd >= 0);
        Assert.True(firstOrderSave > orderAdd);
        Assert.True(attachmentClaim > firstOrderSave);
    }

    [Fact]
    public void CheckoutService_Loads_FieldDefinitions_ForTenant_InOneQuery()
    {
        var source = ReadRepositoryFile("PointOfSale.API", "Services", "Checkout", "CheckoutService.cs");
        var methodStart = source.IndexOf("private async Task<CheckoutFieldSnapshotAdmission> ValidateCheckoutFieldDefinitionsAsync", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("private static CheckoutFieldSnapshotDraft BuildCheckoutFieldSnapshotDraft", StringComparison.Ordinal);
        var method = source[methodStart..methodEnd];

        Assert.Contains(".IgnoreQueryFilters()", method);
        Assert.Contains("field.TenantId == tenantId && !field.IsDeleted", method);
        Assert.Contains(".ToListAsync(cancellationToken)", method);
        Assert.DoesNotContain("FirstOrDefaultAsync", method);
        Assert.DoesNotContain("SingleOrDefaultAsync", method);
    }

    private static object BuildSnapshotDraft(CheckoutFieldDefinition definition, string? submittedValue, bool? required = null)
    {
        var method = typeof(CheckoutService).GetMethod(
            "BuildCheckoutFieldSnapshotDraft",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var state = new CheckoutFieldEffectiveState(
            definition.Id,
            definition.IsEnabled,
            required ?? definition.IsRequired,
            definition.IsEnabled);
        return method!.Invoke(null, new object?[] { definition, submittedValue, state })!;
    }

    private static object BuildSnapshotDraftFromJson(CheckoutFieldDefinition definition, string valueJson)
    {
        var method = typeof(CheckoutService).GetMethod(
            "BuildCheckoutFieldSnapshotDraftFromJson",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var state = new CheckoutFieldEffectiveState(
            definition.Id,
            definition.IsEnabled,
            definition.IsRequired,
            definition.IsEnabled);
        return method!.Invoke(null, new object?[] { definition, valueJson, state })!;
    }

    private static string BuildAttachmentSnapshotValueJson(IReadOnlyList<CheckoutAttachment> attachments)
    {
        var method = typeof(CheckoutService).GetMethod(
            "BuildAttachmentSnapshotValueJson",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return (string)method!.Invoke(null, new object?[] { attachments })!;
    }

    private static JsonElement ParseValue(CheckoutFieldType fieldType, string? value)
    {
        var method = typeof(CheckoutService).GetMethod(
            "BuildCheckoutFieldValueJson",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var json = (string)method!.Invoke(null, new object?[] { fieldType, value })!;
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static T Get<T>(object source, string propertyName)
    {
        var property = source.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return (T)property!.GetValue(source)!;
    }

    private static string ReadRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PointOfSale.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(pathParts).ToArray()));
    }

    private static ApplicationDbContext CreateContext(string? databaseName = null, Guid? tenantId = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString("N"))
            .Options;

        return tenantId.HasValue
            ? new ApplicationDbContext(options, new StaticTenantContext(tenantId.Value))
            : new ApplicationDbContext(options);
    }

    private static OrderCheckoutFieldSnapshot NewSnapshot(
        Guid tenantId,
        Guid orderId,
        string label,
        string valueJson,
        Guid? snapshotId = null) => new()
    {
        Id = snapshotId ?? Guid.NewGuid(),
        TenantId = tenantId,
        OrderId = orderId,
        CheckoutFieldDefinitionId = Guid.NewGuid(),
        FieldKind = CheckoutFieldKind.Custom,
        FieldType = CheckoutFieldType.Select,
        Label = label,
        HelpText = "Historical help",
        IsRequired = true,
        IsEnabled = true,
        DisplayOrder = 10,
        ConfigurationSchemaVersion = 1,
        ConfigurationJson = "{\"schemaVersion\":1,\"options\":[{\"value\":\"original\",\"label\":\"Original\"}]}",
        ValueJson = valueJson,
        CreatedAt = DateTime.UtcNow
    };

    private sealed class StaticTenantContext(Guid tenantId) : PointOfSale.Core.Interfaces.ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
    }
}
