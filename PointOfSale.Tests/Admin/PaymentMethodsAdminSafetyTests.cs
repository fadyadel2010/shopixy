using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Admin;

public sealed class PaymentMethodsAdminSafetyTests
{
    [Fact]
    public async Task UpsertPaymentMethod_WhenConfigurationPayloadIsEmpty_PreservesExistingSecrets()
    {
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        await using var dbContext = CreateDbContext(tenantId);

        var existingConfig = ProtectConfiguration(dataProtectionProvider, new Dictionary<string, string>
        {
            ["publishableKey"] = "pk_live_existing",
            ["secretKey"] = "sk_live_existing",
            ["webhookSecret"] = "whsec_existing"
        });

        dbContext.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "STRIPE",
            DisplayName = "Stripe",
            IsEnabled = false,
            SupportsOnline = true,
            SupportsOffline = false,
            ConfigEncrypted = existingConfig,
            FeeType = "fixed",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var publisher = new Mock<IEventPublisher>();
        PaymentMethodUpdatedEvent? capturedEvent = null;
        publisher.Setup(p => p.PublishAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((evt, _) => capturedEvent = evt as PaymentMethodUpdatedEvent)
            .Returns(Task.CompletedTask);

        var controller = CreateControllerWithPublisher(dbContext, tenantId, dataProtectionProvider, publisher.Object);

        var result = await controller.UpsertPaymentMethod(
            "STRIPE",
            new UpsertPaymentMethodDto
            {
                DisplayName = "Stripe Checkout",
                Configuration = new Dictionary<string, string>()
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PaymentMethodResponseDto>(ok.Value);

        var paymentMethod = await dbContext.TenantPaymentMethods.SingleAsync(method => method.Code == "STRIPE");
        Assert.Equal(existingConfig, paymentMethod.ConfigEncrypted);
        Assert.Equal("Stripe Checkout", paymentMethod.DisplayName);
        Assert.True(response.IsConfigurationValid);

        // Verify event payload
        Assert.NotNull(capturedEvent);
        Assert.True(capturedEvent.IsConfigurationValid);
        Assert.Equal("Stripe Checkout", capturedEvent.DisplayName);
        publisher.Verify(p => p.PublishAsync(
            It.Is<object>(e => IsUpdatedEvent(e, true)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateConfiguration_WhenPayloadIsEmpty_ReturnsBadRequestAndPreservesExistingSecrets()
    {
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        await using var dbContext = CreateDbContext(tenantId);

        var existingConfig = ProtectConfiguration(dataProtectionProvider, new Dictionary<string, string>
        {
            ["publishableKey"] = "pk_live_existing",
            ["secretKey"] = "sk_live_existing",
            ["webhookSecret"] = "whsec_existing"
        });

        dbContext.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "STRIPE",
            DisplayName = "Stripe",
            IsEnabled = false,
            SupportsOnline = true,
            SupportsOffline = false,
            ConfigEncrypted = existingConfig,
            FeeType = "fixed",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, tenantId, dataProtectionProvider);

        var result = await controller.UpdateConfiguration(
            "STRIPE",
            new UpsertPaymentMethodDto
            {
                Configuration = new Dictionary<string, string>()
            },
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = SerializeToJson(badRequest.Value);

        Assert.Equal("EMPTY_CONFIGURATION_NO_CHANGE", payload.RootElement.GetProperty("error").GetString());

        var paymentMethod = await dbContext.TenantPaymentMethods.SingleAsync(method => method.Code == "STRIPE");
        Assert.Equal(existingConfig, paymentMethod.ConfigEncrypted);
    }

    [Fact]
    public async Task UpdateConfiguration_ResetRequiresExplicitConfirmation_AndClearsConfigurationWhenConfirmed()
    {
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        await using var dbContext = CreateDbContext(tenantId);

        var existingConfig = ProtectConfiguration(dataProtectionProvider, new Dictionary<string, string>
        {
            ["publishableKey"] = "pk_live_existing",
            ["secretKey"] = "sk_live_existing",
            ["webhookSecret"] = "whsec_existing"
        });

        var paymentMethodId = Guid.NewGuid();
        dbContext.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = paymentMethodId,
            TenantId = tenantId,
            Code = "STRIPE",
            DisplayName = "Stripe",
            IsEnabled = true,
            SupportsOnline = true,
            SupportsOffline = false,
            ConfigEncrypted = existingConfig,
            FeeType = "fixed",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, tenantId, dataProtectionProvider);

        var rejectedReset = await controller.UpdateConfiguration(
            "STRIPE",
            new UpsertPaymentMethodDto
            {
                Reset = true
            },
            CancellationToken.None);

        var rejectedResult = Assert.IsType<BadRequestObjectResult>(rejectedReset);
        var rejectedPayload = SerializeToJson(rejectedResult.Value);
        Assert.Equal("CONFIGURATION_RESET_CONFIRMATION_REQUIRED", rejectedPayload.RootElement.GetProperty("error").GetString());

        var unchangedMethod = await dbContext.TenantPaymentMethods.SingleAsync(method => method.Id == paymentMethodId);
        Assert.Equal(existingConfig, unchangedMethod.ConfigEncrypted);
        Assert.True(unchangedMethod.IsEnabled);

        var publisher = new Mock<IEventPublisher>();
        PaymentMethodUpdatedEvent? capturedUpdatedEvent = null;
        PaymentMethodDisabledEvent? capturedDisabledEvent = null;
        publisher.Setup(p => p.PublishAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((evt, _) =>
            {
                if (evt is PaymentMethodUpdatedEvent updated)
                {
                    capturedUpdatedEvent = updated;
                }
                else if (evt is PaymentMethodDisabledEvent disabled)
                {
                    capturedDisabledEvent = disabled;
                }
            })
            .Returns(Task.CompletedTask);

        var controllerWithPublisher = CreateControllerWithPublisher(dbContext, tenantId, dataProtectionProvider, publisher.Object);

        var confirmedReset = await controllerWithPublisher.UpdateConfiguration(
            "STRIPE",
            new UpsertPaymentMethodDto
            {
                Reset = true,
                ConfirmReset = true
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(confirmedReset);
        var response = Assert.IsType<PaymentMethodResponseDto>(ok.Value);

        var clearedMethod = await dbContext.TenantPaymentMethods.SingleAsync(method => method.Id == paymentMethodId);
        Assert.Null(clearedMethod.ConfigEncrypted);
        Assert.False(clearedMethod.IsEnabled);
        Assert.False(response.IsConfigurationValid);

        // Verify event payloads
        Assert.NotNull(capturedUpdatedEvent);
        Assert.False(capturedUpdatedEvent.IsConfigurationValid);
        Assert.NotNull(capturedDisabledEvent);
        Assert.Equal("Stripe", capturedDisabledEvent.DisplayName);
        publisher.Verify(p => p.PublishAsync(
            It.Is<object>(e => IsUpdatedEvent(e, false)),
            It.IsAny<CancellationToken>()), Times.Once);
        publisher.Verify(p => p.PublishAsync(
            It.Is<object>(e => IsDisabledEvent(e, "STRIPE")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnablePaymentMethod_WhenConfigurationIsInvalid_ReturnsValidationErrorAndLeavesMethodDisabled()
    {
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        await using var dbContext = CreateDbContext(tenantId);

        dbContext.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "PAYMOB",
            DisplayName = "Paymob",
            IsEnabled = false,
            SupportsOnline = true,
            SupportsOffline = false,
            ConfigEncrypted = ProtectConfiguration(dataProtectionProvider, new Dictionary<string, string>
            {
                ["apiKey"] = "paymob-api",
                ["integrationId"] = "123456"
            }),
            FeeType = "fixed",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, tenantId, dataProtectionProvider);

        var result = await controller.EnablePaymentMethod("PAYMOB", CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = SerializeToJson(badRequest.Value);

        Assert.Equal("INVALID_PAYMENT_CONFIGURATION", payload.RootElement.GetProperty("error").GetString());
        Assert.Contains(
            payload.RootElement.GetProperty("validationErrors").EnumerateArray().Select(item => item.GetString()),
            value => value == "Paymob iframe ID is required.");

        var paymentMethod = await dbContext.TenantPaymentMethods.SingleAsync(method => method.Code == "PAYMOB");
        Assert.False(paymentMethod.IsEnabled);
    }

    [Fact]
    public async Task EnablePaymentMethod_WhenConfigurationIsValid_EnablesMethod()
    {
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        await using var dbContext = CreateDbContext(tenantId);

        dbContext.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "STRIPE",
            DisplayName = "Stripe",
            IsEnabled = false,
            SupportsOnline = true,
            SupportsOffline = false,
            ConfigEncrypted = ProtectConfiguration(dataProtectionProvider, new Dictionary<string, string>
            {
                ["publishableKey"] = "pk_live_valid",
                ["secretKey"] = "sk_live_valid",
                ["webhookSecret"] = "whsec_valid"
            }),
            FeeType = "fixed",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var publisher = new Mock<IEventPublisher>();
        PaymentMethodEnabledEvent? capturedEvent = null;
        publisher.Setup(p => p.PublishAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Callback<object, CancellationToken>((evt, _) => capturedEvent = evt as PaymentMethodEnabledEvent)
            .Returns(Task.CompletedTask);

        var controller = CreateControllerWithPublisher(dbContext, tenantId, dataProtectionProvider, publisher.Object);

        var result = await controller.EnablePaymentMethod("STRIPE", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PaymentMethodResponseDto>(ok.Value);

        var paymentMethod = await dbContext.TenantPaymentMethods.SingleAsync(method => method.Code == "STRIPE");
        Assert.True(paymentMethod.IsEnabled);
        Assert.True(response.IsEnabled);
        Assert.True(response.IsConfigurationValid);

        // Verify event payload
        Assert.NotNull(capturedEvent);
        Assert.Equal("Stripe", capturedEvent.DisplayName);
        Assert.Equal("STRIPE", capturedEvent.Code);
        publisher.Verify(p => p.PublishAsync(
            It.Is<object>(e => IsEnabledEvent(e, "STRIPE", "Stripe")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        return new ApplicationDbContext(options, tenantContext);
    }

    private static PaymentMethodsAdminController CreateController(
        ApplicationDbContext dbContext,
        Guid tenantId,
        IDataProtectionProvider dataProtectionProvider)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        var publisher = new Mock<IEventPublisher>();
        publisher.Setup(endpoint => endpoint.PublishAsync(It.IsAny<PaymentMethodCreatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();
        publisher.Setup(endpoint => endpoint.PublishAsync(It.IsAny<PaymentMethodUpdatedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();
        publisher.Setup(endpoint => endpoint.PublishAsync(It.IsAny<PaymentMethodEnabledEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();
        publisher.Setup(endpoint => endpoint.PublishAsync(It.IsAny<PaymentMethodDisabledEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        return CreateControllerWithPublisher(dbContext, tenantId, dataProtectionProvider, publisher.Object);
    }

    private static PaymentMethodsAdminController CreateControllerWithPublisher(
        ApplicationDbContext dbContext,
        Guid tenantId,
        IDataProtectionProvider dataProtectionProvider,
        IEventPublisher publisher)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        return new PaymentMethodsAdminController(
            dbContext,
            publisher,
            dataProtectionProvider,
            new CommerceReliabilityTelemetry(),
            NullLogger<PaymentMethodsAdminController>.Instance,
            tenantContext)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static string ProtectConfiguration(
        IDataProtectionProvider dataProtectionProvider,
        Dictionary<string, string> configuration)
    {
        var protector = dataProtectionProvider.CreateProtector("PaymentMethodSecrets");
        return protector.Protect(JsonSerializer.Serialize(configuration));
    }

    private static JsonDocument SerializeToJson(object? value) =>
        JsonDocument.Parse(JsonSerializer.Serialize(value));

    private static bool IsUpdatedEvent(object value, bool expectedConfigurationValid) =>
        value is PaymentMethodUpdatedEvent updated &&
        updated.IsConfigurationValid == expectedConfigurationValid;

    private static bool IsDisabledEvent(object value, string code) =>
        value is PaymentMethodDisabledEvent disabled &&
        disabled.Code == code;

    private static bool IsEnabledEvent(object value, string code, string displayName) =>
        value is PaymentMethodEnabledEvent enabled &&
        enabled.Code == code &&
        enabled.DisplayName == displayName;
}
