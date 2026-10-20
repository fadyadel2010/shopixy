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
    [Theory]
    [InlineData(true, true)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(false, false)]
    public async Task ManualWallet_LegacyModeInputIgnoredAndOmittedFromApi(bool storedSandbox, bool upsert)
    {
        var tenant = Guid.NewGuid();
        var protection = new EphemeralDataProtectionProvider();
        await using var db = CreateDbContext(tenant);
        db.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(), TenantId = tenant, Code = "MANUALWALLET", DisplayName = "QA wallet",
            IsSandbox = storedSandbox, SupportsOffline = true,
            ConfigEncrypted = ProtectConfiguration(protection, new() { ["walletNumber"] = "01000000000" })
        });
        await db.SaveChangesAsync();
        var controller = CreateController(db, tenant, protection);
        var request = new UpsertPaymentMethodDto
        {
            IsSandbox = !storedSandbox, Configuration = new() { ["walletNumber"] = "01000000000" }
        };
        var result = upsert ? await controller.UpsertPaymentMethod("MANUALWALLET", request, default)
            : await controller.UpdateConfiguration("MANUALWALLET", request, default);
        var dto = Assert.IsType<PaymentMethodResponseDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Null(dto.IsSandbox);
        Assert.DoesNotContain("IsSandbox", JsonSerializer.Serialize(dto));
        Assert.Equal(storedSandbox, (await db.TenantPaymentMethods.SingleAsync()).IsSandbox);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ManualWallet_MetadataUpdateCannotPersistLegacyEnabledInvalidConfiguration(bool emptyObject)
    {
        var tenant=Guid.NewGuid();await using var db=CreateDbContext(tenant);
        db.TenantPaymentMethods.Add(new TenantPaymentMethod {Id=Guid.NewGuid(),TenantId=tenant,Code="MANUALWALLET",DisplayName="Legacy invalid wallet",IsEnabled=true,ConfigEncrypted=null,FeeType="fixed"});
        await db.SaveChangesAsync();
        var controller=CreateController(db,tenant,new EphemeralDataProtectionProvider());
        Assert.IsType<BadRequestObjectResult>(await controller.UpsertPaymentMethod("MANUALWALLET",new(){Configuration=emptyObject?new():null},default));
    }
    [Theory]
    [InlineData("", false)] [InlineData("   ", false)] [InlineData("bad-wallet", false)]
    [InlineData("", true)] [InlineData("   ", true)] [InlineData("bad-wallet", true)]
    public async Task ManualWallet_UnsafeRecipientMutationRejectedAndSavedPublicRecipientRemainsTruthful(string value, bool upsert)
    {
        var tenant=Guid.NewGuid(); var protector=new EphemeralDataProtectionProvider();
        await using var db=CreateDbContext(tenant);
        var encrypted=ProtectConfiguration(protector,new Dictionary<string,string> { ["walletNumber"]="01000000000" });
        db.TenantPaymentMethods.Add(new TenantPaymentMethod {Id=Guid.NewGuid(),TenantId=tenant,Code="MANUALWALLET",DisplayName="Wallet",IsEnabled=true,IsSandbox=true,ConfigEncrypted=encrypted,FeeType="fixed"});
        await db.SaveChangesAsync();
        var controller=CreateController(db,tenant,protector);
        var request=new UpsertPaymentMethodDto {Configuration=new Dictionary<string,string>{["walletNumber"]=value}};
        var result=upsert?await controller.UpsertPaymentMethod("MANUALWALLET",request,CancellationToken.None):await controller.UpdateConfiguration("MANUALWALLET",request,CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
        db.ChangeTracker.Clear();
        var stored=await db.TenantPaymentMethods.SingleAsync();Assert.True(stored.IsEnabled);Assert.Equal(encrypted,stored.ConfigEncrypted);
        var read=Assert.IsType<PaymentMethodResponseDto>(Assert.IsType<OkObjectResult>(await controller.GetPaymentMethod("MANUALWALLET",CancellationToken.None)).Value);
        Assert.True(read.IsConfigurationValid); Assert.Equal("01000000000",read.PublicConfiguration["walletNumber"]);
    }
    [Fact]
    public async Task GetProviderCatalog_WhenTenantHasNoPaymentMethods_ReturnsSupportedProvidersWithoutSeedingRows()
    {
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        await using var dbContext = CreateDbContext(tenantId);
        var controller = CreateController(dbContext, tenantId, dataProtectionProvider);

        var result = controller.GetProviderCatalog();

        var ok = Assert.IsType<OkObjectResult>(result);
        var catalog = Assert.IsAssignableFrom<IEnumerable<PaymentMethodCatalogProviderDto>>(ok.Value);
        var codes = catalog.Select(provider => provider.Code).ToArray();

        Assert.Equal(new[] { "COD", "STRIPE", "PAYMOB", "MANUALWALLET" }, codes);
        Assert.Empty(await dbContext.TenantPaymentMethods.ToListAsync());
    }

    [Fact]
    public async Task GetPaymentMethods_HidesFawryWithoutRewritingHistoricalRow()
    {
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        await using var dbContext = CreateDbContext(tenantId);
        dbContext.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "FAWRY",
            DisplayName = "Fawry Pay",
            IsEnabled = true,
            SupportsOnline = true,
            FeeType = "fixed",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        var controller = CreateController(dbContext, tenantId, dataProtectionProvider);
        var result = await controller.GetPaymentMethods(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<List<PaymentMethodResponseDto>>(ok.Value));
        Assert.True((await dbContext.TenantPaymentMethods.SingleAsync()).IsEnabled);
    }

    [Fact]
    public async Task UpdateConfiguration_WhenProviderDoesNotExist_CreatesDisabledTenantRowWithValidConfiguration()
    {
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        await using var dbContext = CreateDbContext(tenantId);
        var controller = CreateController(dbContext, tenantId, dataProtectionProvider);

        var result = await controller.UpdateConfiguration(
            "STRIPE",
            new UpsertPaymentMethodDto
            {
                Configuration = new Dictionary<string, string>
                {
                    ["publishableKey"] = "pk_test_valid",
                    ["apiKey"] = "sk_test_valid",
                    ["webhookSecret"] = "whsec_test_valid"
                }
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PaymentMethodResponseDto>(ok.Value);
        var paymentMethod = await dbContext.TenantPaymentMethods.SingleAsync(method => method.Code == "STRIPE");

        Assert.Equal(tenantId, paymentMethod.TenantId);
        Assert.False(paymentMethod.IsEnabled);
        Assert.True(response.IsConfigurationValid);
        Assert.False(response.IsEnabled);
    }

    [Fact]
    public async Task EnablePaymentMethod_WhenCodDoesNotExist_CreatesAndEnablesWithoutCredentials()
    {
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        await using var dbContext = CreateDbContext(tenantId);
        var controller = CreateController(dbContext, tenantId, dataProtectionProvider);

        var result = await controller.EnablePaymentMethod("COD", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PaymentMethodResponseDto>(ok.Value);
        var paymentMethod = await dbContext.TenantPaymentMethods.SingleAsync(method => method.Code == "COD");

        Assert.Equal(tenantId, paymentMethod.TenantId);
        Assert.True(paymentMethod.IsEnabled);
        Assert.True(response.IsConfigurationValid);
        Assert.False(response.RequiresConfiguration);
    }

    [Fact]
    public async Task EnablePaymentMethod_WhenManualWalletConfigurationIsMissing_ReturnsValidationErrorAndDoesNotPersistEnabledRow()
    {
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        await using var dbContext = CreateDbContext(tenantId);
        var controller = CreateController(dbContext, tenantId, dataProtectionProvider);

        var result = await controller.EnablePaymentMethod("MANUALWALLET", CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = SerializeToJson(badRequest.Value);

        Assert.Equal("INVALID_PAYMENT_CONFIGURATION", payload.RootElement.GetProperty("error").GetString());
        Assert.Contains(
            payload.RootElement.GetProperty("validationErrors").EnumerateArray().Select(item => item.GetString()),
            value => value == "Manual wallet number is required.");
        Assert.Empty(await dbContext.TenantPaymentMethods.ToListAsync());
    }

    [Fact]
    public async Task GetPaymentMethods_ReturnsOnlyCurrentTenantRows()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        await using var dbContext = CreateDbContext(tenantId);

        dbContext.TenantPaymentMethods.AddRange(
            new TenantPaymentMethod
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Code = "COD",
                DisplayName = "Cash on Delivery",
                SupportsOffline = true,
                FeeType = "fixed",
                CreatedAt = DateTime.UtcNow
            },
            new TenantPaymentMethod
            {
                Id = Guid.NewGuid(),
                TenantId = otherTenantId,
                Code = "STRIPE",
                DisplayName = "Stripe",
                SupportsOnline = true,
                FeeType = "fixed",
                CreatedAt = DateTime.UtcNow
            });
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, tenantId, dataProtectionProvider);

        var result = await controller.GetPaymentMethods(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<List<PaymentMethodResponseDto>>(ok.Value);
        var method = Assert.Single(response);

        Assert.Equal("COD", method.Code);
    }

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
            IsSandbox = false, // This fixture intentionally contains production keys.
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
    public async Task UpdateConfiguration_WhenPaymobApiKeyIsSubmitted_EncryptsAndRedactsIt()
    {
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        await using var dbContext = CreateDbContext(tenantId);
        var controller = CreateController(dbContext, tenantId, dataProtectionProvider);

        var result = await controller.UpdateConfiguration(
            "PAYMOB",
            new UpsertPaymentMethodDto
            {
                Configuration = new Dictionary<string, string>
                {
                    ["publicKey"] = "egy_pk_test_public-placeholder",
                    ["apiKey"] = "paymob-dashboard-api-key",
                    ["secretKey"] = "egy_sk_test_secret-placeholder",
                    ["integrationId"] = "3153090",
                    ["hmacSecret"] = "paymob-hmac-secret"
                }
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PaymentMethodResponseDto>(ok.Value);
        var paymentMethod = await dbContext.TenantPaymentMethods.SingleAsync(method => method.Code == "PAYMOB");
        var decrypted = UnprotectConfiguration(dataProtectionProvider, paymentMethod.ConfigEncrypted!);
        var serializedResponse = JsonSerializer.Serialize(response);

        Assert.Equal("paymob-dashboard-api-key", decrypted["apiKey"]);
        Assert.DoesNotContain("paymob-dashboard-api-key", serializedResponse, StringComparison.Ordinal);
        Assert.DoesNotContain("apiKey", serializedResponse, StringComparison.Ordinal);
        Assert.True(response.IsConfigurationValid);
    }

    [Fact]
    public async Task UpdateConfiguration_WhenPaymobApiKeyIsBlank_PreservesExistingEncryptedApiKey()
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
                ["publicKey"] = "egy_pk_test_public-placeholder",
                ["apiKey"] = "paymob-dashboard-api-key",
                ["secretKey"] = "egy_sk_test_secret-placeholder",
                ["integrationId"] = "3153090",
                ["hmacSecret"] = "paymob-hmac-secret"
            }),
            FeeType = "fixed",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, tenantId, dataProtectionProvider);

        var result = await controller.UpdateConfiguration(
            "PAYMOB",
            new UpsertPaymentMethodDto
            {
                Configuration = new Dictionary<string, string>
                {
                    ["apiKey"] = " ",
                    ["integrationId"] = "3153090"
                }
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PaymentMethodResponseDto>(ok.Value);
        var paymentMethod = await dbContext.TenantPaymentMethods.SingleAsync(method => method.Code == "PAYMOB");
        var decrypted = UnprotectConfiguration(dataProtectionProvider, paymentMethod.ConfigEncrypted!);

        Assert.Equal("paymob-dashboard-api-key", decrypted["apiKey"]);
        Assert.True(response.IsConfigurationValid);
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
            value => value == "Gateway public key is required.");

        var paymentMethod = await dbContext.TenantPaymentMethods.SingleAsync(method => method.Code == "PAYMOB");
        Assert.False(paymentMethod.IsEnabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnablePaymentMethod_ProductionKeys_RequiresMatchingEnvironment(bool sandbox)
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
            IsSandbox = sandbox,
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

        if (sandbox)
        {
            var refused = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("environment", SerializeToJson(refused.Value).RootElement.GetProperty("validationErrors").ToString());
            Assert.False((await dbContext.TenantPaymentMethods.SingleAsync()).IsEnabled);
            publisher.VerifyNoOtherCalls();
            return;
        }

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
            tenantContext,
            PointOfSale.Tests.Infrastructure.PaymentMethodRuntimeTestStub.Ready())
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

    private static Dictionary<string, string> UnprotectConfiguration(
        IDataProtectionProvider dataProtectionProvider,
        string protectedConfiguration)
    {
        var protector = dataProtectionProvider.CreateProtector("PaymentMethodSecrets");
        var json = protector.Unprotect(protectedConfiguration);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
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
