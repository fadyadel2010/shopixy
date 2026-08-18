using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Architecture;

public sealed class PaymobCertificationClosure3Tests
{
    [Fact]
    public async Task TenantAuthority_MustReturnOnlyCurrentTenantOwnedCredentials()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var method = CreatePaymobMethod(tenantId, dataProtectionProvider);
        dbContext.TenantPaymentMethods.Add(method);
        await dbContext.SaveChangesAsync();

        var credentials = await CreateResolver(dbContext, dataProtectionProvider)
            .ResolveCredentialsAsync(tenantId, PaymobOperationalCapability.Webhook);

        Assert.Equal(tenantId, credentials.TenantId);
        Assert.Equal(method.Id, credentials.TenantPaymentMethodId);
        Assert.Equal(PublicKey, credentials.PublicKey);
        Assert.Equal(SecretKey, credentials.SecretKey);
        Assert.Equal(IntegrationId, credentials.IntegrationId);
        Assert.Equal(HmacSecret, credentials.HmacSecret);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task TenantAuthority_MustRejectDisabledOrDeletedConfiguration(
        bool isEnabled,
        bool isDeleted)
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        dbContext.TenantPaymentMethods.Add(CreatePaymobMethod(
            tenantId,
            dataProtectionProvider,
            isEnabled: isEnabled,
            isDeleted: isDeleted));
        await dbContext.SaveChangesAsync();

        var resolver = CreateResolver(dbContext, dataProtectionProvider);

        await Assert.ThrowsAsync<PaymobNotConfiguredException>(() =>
            resolver.ResolveCredentialsAsync(tenantId, PaymobOperationalCapability.Webhook));
    }

    [Fact]
    public async Task TenantAuthority_MustRejectEnvironmentMismatch()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        dbContext.TenantPaymentMethods.Add(CreatePaymobMethod(
            tenantId,
            dataProtectionProvider,
            isSandbox: false));
        await dbContext.SaveChangesAsync();

        var resolver = CreateResolver(dbContext, dataProtectionProvider);

        await Assert.ThrowsAsync<PaymobNotConfiguredException>(() =>
            resolver.ResolveCredentialsAsync(tenantId, PaymobOperationalCapability.Webhook));
    }

    [Fact]
    public async Task TenantAuthority_MustRejectDisabledCapability()
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        dbContext.TenantPaymentMethods.Add(CreatePaymobMethod(tenantId, dataProtectionProvider));
        await dbContext.SaveChangesAsync();
        var options = CreateEnabledOptions() with
        {
            FeatureFlags = CreateEnabledOptions().FeatureFlags with
            {
                WebhookExecutionEnabled = false
            }
        };

        var resolver = CreateResolver(dbContext, dataProtectionProvider, options);

        await Assert.ThrowsAsync<PaymobNotConfiguredException>(() =>
            resolver.ResolveCredentialsAsync(tenantId, PaymobOperationalCapability.Webhook));
    }

    [Theory]
    [InlineData("", IntegrationId, HmacSecret)]
    [InlineData(SecretKey, "", HmacSecret)]
    [InlineData(SecretKey, IntegrationId, "")]
    public async Task TenantAuthority_MustRejectMissingRequiredCredentials(
        string secretKey,
        string integrationId,
        string hmacSecret)
    {
        await using var dbContext = CreateDbContext();
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        dbContext.TenantPaymentMethods.Add(CreatePaymobMethod(
            tenantId,
            dataProtectionProvider,
            secretKey: secretKey,
            integrationId: integrationId,
            hmacSecret: hmacSecret));
        await dbContext.SaveChangesAsync();

        var resolver = CreateResolver(dbContext, dataProtectionProvider);

        await Assert.ThrowsAsync<PaymobNotConfiguredException>(() =>
            resolver.ResolveCredentialsAsync(tenantId, PaymobOperationalCapability.Webhook));
    }

    [Fact]
    public async Task TenantAuthority_MustNeverFallBackToValidGlobalConfiguration()
    {
        await using var dbContext = CreateDbContext();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var resolver = CreateResolver(dbContext, dataProtectionProvider);

        await Assert.ThrowsAsync<PaymobNotConfiguredException>(() =>
            resolver.ResolveCredentialsAsync(Guid.NewGuid(), PaymobOperationalCapability.PaymentIntent));
    }

    [Fact]
    public async Task ProviderExecution_MustStopBeforeRuntimeBridgeWhenAuthorityRejects()
    {
        var tenantId = Guid.NewGuid();
        var resolver = new Mock<IPaymobTenantConfigurationResolver>();
        resolver
            .Setup(value => value.ResolveCredentialsAsync(
                tenantId,
                PaymobOperationalCapability.PaymentIntent,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PaymobNotConfiguredException(tenantId));
        var runtimeBridge = new Mock<IPaymobRuntimeBridge>();
        var service = new PaymobRuntimeApplicationService(
            resolver.Object,
            new PaymobAdapterCredentialHandoff(),
            runtimeBridge.Object,
            NullLogger<PaymobRuntimeApplicationService>.Instance);

        await Assert.ThrowsAsync<PaymobNotConfiguredException>(() =>
            service.CreatePaymentIntentAsync(CreateApplicationRequest(tenantId)));

        runtimeBridge.Verify(
            value => value.ExecuteAsync(It.IsAny<PaymobExecutionRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void ArchitectureGuard_MustKeepCredentialAuthorityInApplicationLayer()
    {
        var resolver = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Paymob/PaymobTenantConfigurationResolver.cs");
        var paymentService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Paymob/PaymobPaymentService.cs");
        var applicationService = CertificationSource.ReadRepoFile(
            "PointOfSale.API/Services/Payments/Paymob/PaymobRuntimeApplicationService.cs");

        Assert.Contains("!pm.IsDeleted", resolver, StringComparison.Ordinal);
        Assert.Contains("AsNoTracking()", resolver, StringComparison.Ordinal);
        Assert.Contains("IsCapabilityEnabled", resolver, StringComparison.Ordinal);
        Assert.Contains("PaymobOperationalCapability.Webhook", paymentService, StringComparison.Ordinal);
        Assert.Contains("PaymobOperationalCapability.Callback", paymentService, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPaymobConfigurationUnfilteredAsync", paymentService, StringComparison.Ordinal);
        Assert.Contains("IPaymobTenantConfigurationResolver", applicationService, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantPaymentMethods", applicationService, StringComparison.Ordinal);
    }

    private static PaymobTenantConfigurationResolver CreateResolver(
        ApplicationDbContext dbContext,
        IDataProtectionProvider dataProtectionProvider,
        PaymobOptions? options = null)
    {
        return new PaymobTenantConfigurationResolver(
            dbContext,
            dataProtectionProvider,
            Options.Create(options ?? CreateEnabledOptions()),
            NullLogger<PaymobTenantConfigurationResolver>.Instance);
    }

    private static PaymobOptions CreateEnabledOptions()
    {
        return new PaymobOptions
        {
            Enabled = true,
            Environment = PaymobEnvironment.Sandbox,
            SandboxMode = true,
            Callback = new PaymobCallbackConfiguration
            {
                Configured = true,
                CallbackUrl = new Uri("https://store.example.test/api/paymob/callback"),
                WebhookUrl = new Uri("https://store.example.test/api/paymob/webhook")
            },
            FeatureFlags = new PaymobFeatureFlags
            {
                Enabled = true,
                SandboxEnabled = true,
                PaymentExecutionEnabled = true,
                CallbackExecutionEnabled = true,
                WebhookExecutionEnabled = true,
                RefundExecutionEnabled = true
            },
            Capabilities = new PaymobCapabilityModel
            {
                PaymentExecutionCertified = true,
                CallbackCertified = true,
                ProviderAdapterCertified = true
            }
        };
    }

    private static TenantPaymentMethod CreatePaymobMethod(
        Guid tenantId,
        IDataProtectionProvider dataProtectionProvider,
        bool isEnabled = true,
        bool isDeleted = false,
        bool isSandbox = true,
        string secretKey = SecretKey,
        string integrationId = IntegrationId,
        string hmacSecret = HmacSecret)
    {
        var protector = dataProtectionProvider.CreateProtector("PaymentMethodSecrets");
        var config = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["publicKey"] = PublicKey,
            ["secretKey"] = secretKey,
            ["integrationId"] = integrationId,
            ["hmacSecret"] = hmacSecret
        });

        return new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = "PAYMOB",
            DisplayName = "Paymob",
            IsEnabled = isEnabled,
            SupportsOnline = true,
            IsSandbox = isSandbox,
            IsDeleted = isDeleted,
            ConfigEncrypted = protector.Protect(config),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static PaymobRuntimePaymentIntentApplicationRequest CreateApplicationRequest(Guid tenantId)
    {
        return new PaymobRuntimePaymentIntentApplicationRequest(
            tenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ORDER-1",
            100m,
            "EGP",
            "https://tenant.example.test",
            null,
            "payment-reference",
            "idempotency-key",
            "correlation-id",
            DateTime.UtcNow,
            DateTimeOffset.UtcNow,
            "buyer@example.test",
            "Buyer",
            "Test",
            "Street",
            "+201000000000",
            "12345",
            "Cairo",
            "EG",
            "Cairo");
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private const string PublicKey = "egy_pk_test_closure3";
    private const string SecretKey = "egy_sk_test_closure3";
    private const string IntegrationId = "123456";
    private const string HmacSecret = "paymob-hmac-secret-12345";
}
