using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Redis.Configuration;
using PointOfSale.Infrastructure.Redis.Services;

namespace PointOfSale.Tests.Payments;

public sealed class PaymobCallbackValidationTests
{
    [Fact]
    public async Task ValidateCallbackAsync_ResolvesTenantFromStoredPaymentIntentContext()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        const string paymobOrderId = "paymob-order-123";
        const string hmacSecret = "paymob-hmac-secret";
        const string storefrontBaseUrl = "https://tenant-a.example.com";
        var dataProtectionProvider = CreateDataProtectionProvider();

        await using var context = CreateDbContext();
        context.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-PAYMOB-1",
            PaymentMethod = PaymentMethod.Card,
            PaymentMethodCode = "PAYMOB",
            Status = OrderStatus.Pending,
            PaymentStatus = PointOfSale.Core.Enums.PaymentStatus.Pending,
            Currency = "EGP",
            Total = 150m
        });
        context.PaymentIntents.Add(new PaymentIntent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = orderId,
            Provider = PaymentProvider.Paymob,
            ProviderIntentId = paymobOrderId,
            PaymentReference = "PMB-TEST-1",
            IdempotencyKey = "paymob:intent:test",
            Amount = 150m,
            Currency = "EGP",
            PaymentMethod = PaymentMethod.Card,
            Status = PaymentIntentStatus.Pending,
            ProviderMetadata = PaymentIntentRoutingMetadataSerializer.Serialize(
                tenantId,
                orderId,
                storefrontBaseUrl)
        });
        context.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "PAYMOB",
            DisplayName = "Paymob",
            IsEnabled = true,
            SupportsOnline = true,
            ConfigEncrypted = ProtectPaymobConfig(dataProtectionProvider, hmacSecret)
        });
        await context.SaveChangesAsync();

        var service = CreatePaymobService(context, dataProtectionProvider, hmacSecret);
        var callbackParams = CreateSignedCallbackParams(paymobOrderId, hmacSecret);

        var result = await service.ValidateCallbackAsync(
            callbackParams,
            "trace-paymob-callback",
            CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(tenantId, result.TenantId);
        Assert.Equal(orderId, result.OrderId);
        Assert.Equal(storefrontBaseUrl, result.StorefrontBaseUrl);
    }

    [Fact]
    public async Task ValidateCallbackAsync_WhenStoredRoutingMetadataMissing_ReturnsInvalid()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        const string paymobOrderId = "paymob-order-missing";
        const string hmacSecret = "paymob-hmac-secret";
        var dataProtectionProvider = CreateDataProtectionProvider();

        await using var context = CreateDbContext();
        context.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-PAYMOB-2",
            PaymentMethod = PaymentMethod.Card,
            PaymentMethodCode = "PAYMOB",
            Status = OrderStatus.Pending,
            PaymentStatus = PointOfSale.Core.Enums.PaymentStatus.Pending,
            Currency = "EGP",
            Total = 150m
        });
        context.PaymentIntents.Add(new PaymentIntent
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = orderId,
            Provider = PaymentProvider.Paymob,
            ProviderIntentId = paymobOrderId,
            PaymentReference = "PMB-TEST-2",
            IdempotencyKey = "paymob:intent:test-missing",
            Amount = 150m,
            Currency = "EGP",
            PaymentMethod = PaymentMethod.Card,
            Status = PaymentIntentStatus.Pending,
            ProviderMetadata = "legacy-payment-key-only"
        });
        context.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "PAYMOB",
            DisplayName = "Paymob",
            IsEnabled = true,
            SupportsOnline = true,
            ConfigEncrypted = ProtectPaymobConfig(dataProtectionProvider, hmacSecret)
        });
        await context.SaveChangesAsync();

        var service = CreatePaymobService(context, dataProtectionProvider, hmacSecret);
        var callbackParams = CreateSignedCallbackParams(paymobOrderId, hmacSecret);

        var result = await service.ValidateCallbackAsync(
            callbackParams,
            "trace-paymob-callback-missing",
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Equal(tenantId, result.TenantId);
        Assert.Equal(orderId, result.OrderId);
        Assert.Contains("incomplete", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PaymobPaymentService CreatePaymobService(
        ApplicationDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        string hmacSecret)
    {
        var tenantConfigurationResolver = new Mock<IPaymobTenantConfigurationResolver>();
        tenantConfigurationResolver
            .Setup(resolver => resolver.ResolveCredentialsAsync(
                It.IsAny<Guid>(),
                PaymobOperationalCapability.Callback,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantId, PaymobOperationalCapability _, CancellationToken _) =>
                new PaymobTenantRuntimeCredentials(
                    tenantId,
                    Guid.NewGuid(),
                    "api-key",
                    "123",
                    "456",
                    hmacSecret,
                    true));

        return new PaymobPaymentService(
            dbContext,
            dataProtectionProvider,
            new RedisConnectionFactory(
                Options.Create(new RedisSettings { Enabled = false, InstanceName = "test" }),
                NullLogger<RedisConnectionFactory>.Instance),
            Mock.Of<IEventPublisher>(),
            Mock.Of<PointOfSale.Core.Interfaces.Payments.IPaymentLifecycleService>(),
            tenantConfigurationResolver.Object,
            Mock.Of<IPaymobRuntimeApplicationService>(),
            Mock.Of<IPaymobWebhookRuntimeIntegrationService>(),
            new PaymobSensitiveArtifactProtector(dataProtectionProvider),
            CreateStorefrontUrlResolver(),
            NullLogger<PaymobPaymentService>.Instance);
    }

    private static IDataProtectionProvider CreateDataProtectionProvider()
    {
        return new EphemeralDataProtectionProvider();
    }

    private static string ProtectPaymobConfig(
        IDataProtectionProvider dataProtectionProvider,
        string hmacSecret)
    {
        var protector = dataProtectionProvider.CreateProtector("PaymentMethodSecrets");
        var json = JsonSerializer.Serialize(
            new TenantPaymobConfiguration
            {
                ApiKey = "api-key",
                IntegrationId = "123",
                IframeId = "456",
                HmacSecret = hmacSecret
            },
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

        return protector.Protect(json);
    }

    private static StorefrontUrlResolver CreateStorefrontUrlResolver()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storefront:UrlTemplate"] = "https://{slug}.example.com"
            })
            .Build();

        return new StorefrontUrlResolver(
            configuration,
            NullLogger<StorefrontUrlResolver>.Instance,
            services.GetRequiredService<IServiceScopeFactory>(),
            new MemoryCache(new MemoryCacheOptions()));
    }

    private static Dictionary<string, string> CreateSignedCallbackParams(string paymobOrderId, string hmacSecret)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["amount_cents"] = "15000",
            ["created_at"] = "2026-04-17T18:00:00Z",
            ["currency"] = "EGP",
            ["error_occured"] = "false",
            ["has_parent_transaction"] = "false",
            ["id"] = "987654321",
            ["integration_id"] = "123",
            ["is_3d_secure"] = "false",
            ["is_auth"] = "false",
            ["is_capture"] = "false",
            ["is_refunded"] = "false",
            ["is_standalone_payment"] = "true",
            ["is_voided"] = "false",
            ["order"] = paymobOrderId,
            ["owner"] = "111222",
            ["pending"] = "false",
            ["source_data.pan"] = "411111******1111",
            ["source_data.sub_type"] = "mastercard",
            ["source_data.type"] = "card",
            ["success"] = "true"
        };

        parameters["hmac"] = ComputePaymobCallbackHmac(parameters, hmacSecret);
        return parameters;
    }

    private static string ComputePaymobCallbackHmac(
        IReadOnlyDictionary<string, string> callbackParams,
        string hmacSecret)
    {
        var fieldsToHash = new[]
        {
            "amount_cents",
            "created_at",
            "currency",
            "error_occured",
            "has_parent_transaction",
            "id",
            "integration_id",
            "is_3d_secure",
            "is_auth",
            "is_capture",
            "is_refunded",
            "is_standalone_payment",
            "is_voided",
            "order",
            "owner",
            "pending",
            "source_data.pan",
            "source_data.sub_type",
            "source_data.type",
            "success"
        };

        var concatenated = string.Concat(fieldsToHash.Select(field => callbackParams.GetValueOrDefault(field, string.Empty)));

        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(hmacSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(concatenated));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }
}
