using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;

namespace PointOfSale.Tests.Checkout;

public sealed class AdminOrderCheckoutFieldPresentationTests
{
    [Fact]
    public async Task GetOrderDetails_Presents_Typed_System_And_Custom_Checkout_Snapshots()
    {
        var tenantId = Guid.NewGuid();
        await using var context = CreateContext(tenantId);
        var orderId = Guid.NewGuid();
        var selectFieldId = Guid.NewGuid();
        var sourceFieldId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        await SeedOrderAsync(context, tenantId, orderId);
        context.OrderCheckoutFieldSnapshots.AddRange(
            NewSnapshot(
                tenantId,
                orderId,
                CheckoutFieldKind.System,
                CheckoutFieldType.Phone,
                Guid.NewGuid(),
                "WhatsApp Number",
                "\"+20100111222\"",
                "{}",
                systemFieldKey: "phone",
                displayOrder: 1),
            NewSnapshot(
                tenantId,
                orderId,
                CheckoutFieldKind.Custom,
                CheckoutFieldType.Select,
                selectFieldId,
                "T-Shirt Size",
                "\"l\"",
                "{\"schemaVersion\":1,\"options\":[{\"value\":\"l\",\"label\":\"Large\"}],\"conditionalRules\":[{\"ruleId\":\"rule1\",\"conditionMode\":\"ALL\",\"conditions\":[{\"sourceFieldId\":\"" + sourceFieldId + "\",\"operator\":\"EQUALS\",\"comparisonValue\":\"yes\"}],\"actions\":[\"SHOW\",\"REQUIRE\"]}]}",
                displayOrder: 2),
            NewSnapshot(
                tenantId,
                orderId,
                CheckoutFieldKind.Custom,
                CheckoutFieldType.Checkbox,
                Guid.NewGuid(),
                "Gift wrap",
                "true",
                "{}",
                displayOrder: 3),
            NewSnapshot(
                tenantId,
                orderId,
                CheckoutFieldKind.Custom,
                CheckoutFieldType.Number,
                Guid.NewGuid(),
                "Quantity note",
                "42.5",
                "{}",
                displayOrder: 4),
            NewSnapshot(
                tenantId,
                orderId,
                CheckoutFieldKind.Custom,
                CheckoutFieldType.Date,
                Guid.NewGuid(),
                "Delivery date",
                "\"2026-08-10\"",
                "{}",
                displayOrder: 5),
            NewSnapshot(
                tenantId,
                orderId,
                CheckoutFieldKind.Custom,
                CheckoutFieldType.File,
                Guid.NewGuid(),
                "Proof",
                JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        attachmentId,
                        fileName = "proof.pdf",
                        contentType = "application/pdf",
                        sizeBytes = 2048,
                        mediaType = "file",
                        uploadedAtUtc = "2026-08-10T10:00:00Z",
                        finalizedAtUtc = "2026-08-10T10:01:00Z",
                        finalETag = "etag",
                        finalChecksumSha256 = "sha256",
                        objectKey = "tenants/private/staged-key",
                        finalObjectKey = "tenants/private/final-key"
                    }
                }),
                "{\"schemaVersion\":1,\"allowedMimeTypes\":[\"application/pdf\"],\"maxFileSize\":4096,\"maxFiles\":1}",
                displayOrder: 6));
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var details = await service.GetOrderDetailsAsync(tenantId, orderId, "test-correlation");

        Assert.NotNull(details);
        var phone = details!.CheckoutFieldSnapshots.Single(item => item.SystemFieldKey == "phone");
        Assert.Equal("SYSTEM", phone.FieldKind);
        Assert.Equal("WhatsApp Number", phone.Label);
        Assert.Equal("+20100111222", phone.SubmittedValue.TextValue);

        var select = details.CheckoutFieldSnapshots.Single(item => item.FieldId == selectFieldId);
        Assert.Equal("Large", select.SubmittedValue.DisplayValue);
        Assert.Equal("l", select.SubmittedValue.OptionValue);
        Assert.Equal("Large", select.SubmittedValue.OptionLabel);
        Assert.Single(select.Configuration.Options);
        Assert.Single(select.Configuration.ConditionalRules);
        Assert.Equal(sourceFieldId, select.Configuration.ConditionalRules[0].Conditions[0].SourceFieldId);

        Assert.True(details.CheckoutFieldSnapshots.Single(item => item.Label == "Gift wrap").SubmittedValue.BooleanValue);
        Assert.Equal(42.5m, details.CheckoutFieldSnapshots.Single(item => item.Label == "Quantity note").SubmittedValue.NumberValue);
        Assert.Equal("2026-08-10", details.CheckoutFieldSnapshots.Single(item => item.Label == "Delivery date").SubmittedValue.DateValue);

        var upload = details.CheckoutFieldSnapshots.Single(item => item.Label == "Proof");
        Assert.Equal("proof.pdf", upload.SubmittedValue.DisplayValue);
        Assert.Equal(4096, upload.Configuration.UploadPolicy?.MaxFileSize);
        Assert.Equal(attachmentId, upload.SubmittedValue.Attachments.Single().AttachmentId);

        var serialized = JsonSerializer.Serialize(details);
        Assert.DoesNotContain("objectKey", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("staged-key", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("final-key", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetOrderDetails_Remains_Tenant_Bound_For_Checkout_Snapshot_Presentation()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using var context = CreateContext(tenantA);
        var orderId = Guid.NewGuid();
        await SeedOrderAsync(context, tenantA, orderId);
        context.OrderCheckoutFieldSnapshots.Add(NewSnapshot(
            tenantA,
            orderId,
            CheckoutFieldKind.Custom,
            CheckoutFieldType.Text,
            Guid.NewGuid(),
            "Tenant A field",
            "\"secret\"",
            "{}"));
        await context.SaveChangesAsync();
        var service = CreateService(context);

        var details = await service.GetOrderDetailsAsync(tenantB, orderId, "test-correlation");

        Assert.Null(details);
    }

    private static ApplicationDbContext CreateContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options, new StaticTenantContext(tenantId));
    }

    private static AdminOrderService CreateService(ApplicationDbContext context) => new(
        context,
        Mock.Of<IEventPublisher>(),
        Mock.Of<IRefundEvidenceService>(),
        Mock.Of<ICouponValidationService>(),
        Mock.Of<IPaymentLifecycleService>(),
        Mock.Of<IProductVariantStockRepository>(),
        Mock.Of<IR2StorageService>(),
        Mock.Of<ILogger<AdminOrderService>>());

    private static async Task SeedOrderAsync(ApplicationDbContext context, Guid tenantId, Guid orderId)
    {
        context.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-P1",
            GuestName = "Guest",
            GuestEmail = "guest@example.test",
            GuestPhone = "+201000000000",
            IsGuestCheckout = true,
            ShippingAddress = "Street",
            ShippingCity = "Cairo",
            ShippingCountry = "EG",
            Subtotal = 100,
            Total = 100,
            Currency = "EGP",
            PaymentMethodCode = "COD",
            PaymentMethod = PaymentMethod.Cash
        });
        context.PaymentIntents.Add(new PaymentIntent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = orderId,
            Amount = 100,
            Currency = "EGP",
            PaymentMethod = PaymentMethod.Cash,
            Provider = PaymentProvider.Cod,
            PaymentReference = "PAY-P1",
            IdempotencyKey = "p1",
            Status = PaymentIntentStatus.Skipped
        });
        await context.SaveChangesAsync();
    }

    private static OrderCheckoutFieldSnapshot NewSnapshot(
        Guid tenantId,
        Guid orderId,
        CheckoutFieldKind fieldKind,
        CheckoutFieldType fieldType,
        Guid fieldId,
        string label,
        string valueJson,
        string configurationJson,
        string? systemFieldKey = null,
        int displayOrder = 10) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        OrderId = orderId,
        CheckoutFieldDefinitionId = fieldId,
        FieldKind = fieldKind,
        FieldType = fieldType,
        SystemFieldKey = systemFieldKey,
        Label = label,
        HelpText = "Historical help",
        IsRequired = true,
        IsEnabled = true,
        DisplayOrder = displayOrder,
        ConfigurationSchemaVersion = 1,
        ConfigurationJson = configurationJson,
        ValueJson = valueJson,
        CreatedAt = DateTime.UtcNow
    };

    private sealed class StaticTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
    }
}
