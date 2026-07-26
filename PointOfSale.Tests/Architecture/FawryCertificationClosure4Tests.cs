using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Payments.Runtime.Domain;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.Architecture;

public sealed class FawryCertificationClosure4Tests
{
    [Fact]
    public async Task WebhookAuthority_MustResolveTenantMerchantSecureKeyEnvironmentAndRegistration()
    {
        await using var dbContext = CreateDbContext();
        var protection = new EphemeralDataProtectionProvider();
        await SeedActiveTenantAsync(dbContext);
        var method = CreatePaymentMethod(protection);
        dbContext.TenantPaymentMethods.Add(method);
        await dbContext.SaveChangesAsync();
        var options = EnabledWebhookOptions() with
        {
            MerchantCode = "unused-global-merchant",
            ApiKey = "unused-global-api",
            Secret = "unused-global-secret"
        };

        var authority = await CreateResolver(dbContext, protection, options)
            .ResolveWebhookAuthorityAsync(TenantId);

        Assert.Equal(TenantId, authority.TenantId);
        Assert.Equal(method.Id, authority.TenantPaymentMethodId);
        Assert.Equal(MerchantCode, authority.MerchantCode);
        Assert.Equal(SecureKey, authority.SecureKey);
        Assert.Equal(FawryEnvironment.Sandbox, authority.Environment);
        Assert.True(authority.IsSandbox);
        Assert.Equal(CallbackUrl, authority.CallbackUrl);
        Assert.Equal(WebhookUrl, authority.WebhookUrl);
    }

    [Fact]
    public async Task WebhookAuthority_MustRejectMissingOrDeletedTenant()
    {
        var protection = new EphemeralDataProtectionProvider();

        await using (var missingTenantDb = CreateDbContext())
        {
            missingTenantDb.TenantPaymentMethods.Add(CreatePaymentMethod(protection));
            await missingTenantDb.SaveChangesAsync();

            var missing = await Assert.ThrowsAsync<FawryApplicationException>(() =>
                CreateResolver(missingTenantDb, protection).ResolveWebhookAuthorityAsync(TenantId));

            Assert.Equal("FAWRY_CONFIGURATION_MISSING", missing.Code);
        }

        await using var deletedTenantDb = CreateDbContext();
        deletedTenantDb.Tenants.Add(CreateTenant(TenantStatus.Deleted, isActive: false, deletedAtUtc: DateTime.UtcNow));
        deletedTenantDb.TenantPaymentMethods.Add(CreatePaymentMethod(protection));
        await deletedTenantDb.SaveChangesAsync();

        var deleted = await Assert.ThrowsAsync<FawryApplicationException>(() =>
            CreateResolver(deletedTenantDb, protection).ResolveWebhookAuthorityAsync(TenantId));

        Assert.Equal("FAWRY_CONFIGURATION_MISSING", deleted.Code);
    }

    [Theory]
    [InlineData(false, false, true, "FAWRY_PROVIDER_DISABLED")]
    [InlineData(true, true, true, "FAWRY_CONFIGURATION_DELETED")]
    [InlineData(true, false, false, "FAWRY_ENVIRONMENT_MISMATCH")]
    public async Task WebhookAuthority_MustRejectDisabledDeletedOrWrongEnvironmentConfiguration(
        bool isEnabled,
        bool isDeleted,
        bool isSandbox,
        string expectedCode)
    {
        await using var dbContext = CreateDbContext();
        var protection = new EphemeralDataProtectionProvider();
        await SeedActiveTenantAsync(dbContext);
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod(
            protection,
            isEnabled: isEnabled,
            isDeleted: isDeleted,
            isSandbox: isSandbox));
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<FawryApplicationException>(() =>
            CreateResolver(dbContext, protection).ResolveWebhookAuthorityAsync(TenantId));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task WebhookAuthority_MustRejectMissingTenantConfigurationWithoutGlobalFallback()
    {
        await using var dbContext = CreateDbContext();
        var protection = new EphemeralDataProtectionProvider();
        await SeedActiveTenantAsync(dbContext);

        var exception = await Assert.ThrowsAsync<FawryApplicationException>(() =>
            CreateResolver(dbContext, protection).ResolveWebhookAuthorityAsync(TenantId));

        Assert.Equal("FAWRY_CONFIGURATION_MISSING", exception.Code);
    }

    [Fact]
    public async Task WebhookAuthority_MustRejectInvalidCredentialAndDisabledWebhookCapability()
    {
        var protection = new EphemeralDataProtectionProvider();

        await using (var invalidCredentialDb = CreateDbContext())
        {
            await SeedActiveTenantAsync(invalidCredentialDb);
            invalidCredentialDb.TenantPaymentMethods.Add(CreatePaymentMethod(protection, secureKey: "short"));
            await invalidCredentialDb.SaveChangesAsync();

            var invalid = await Assert.ThrowsAsync<FawryApplicationException>(() =>
                CreateResolver(invalidCredentialDb, protection).ResolveWebhookAuthorityAsync(TenantId));

            Assert.Equal("FAWRY_CREDENTIALS_INVALID", invalid.Code);
        }

        await using var disabledCapabilityDb = CreateDbContext();
        await SeedActiveTenantAsync(disabledCapabilityDb);
        disabledCapabilityDb.TenantPaymentMethods.Add(CreatePaymentMethod(protection));
        await disabledCapabilityDb.SaveChangesAsync();
        var options = EnabledWebhookOptions() with
        {
            FeatureFlags = EnabledWebhookOptions().FeatureFlags with { WebhookExecutionEnabled = false }
        };

        var disabled = await Assert.ThrowsAsync<FawryApplicationException>(() =>
            CreateResolver(disabledCapabilityDb, protection, options).ResolveWebhookAuthorityAsync(TenantId));

        Assert.Equal("FAWRY_CAPABILITY_DISABLED", disabled.Code);
    }

    [Fact]
    public async Task WebhookAuthority_MustRejectMissingWebhookRegistration()
    {
        await using var dbContext = CreateDbContext();
        var protection = new EphemeralDataProtectionProvider();
        await SeedActiveTenantAsync(dbContext);
        dbContext.TenantPaymentMethods.Add(CreatePaymentMethod(protection));
        await dbContext.SaveChangesAsync();
        var options = EnabledWebhookOptions() with
        {
            Callback = new FawryCallbackConfiguration()
        };

        var exception = await Assert.ThrowsAsync<FawryApplicationException>(() =>
            CreateResolver(dbContext, protection, options).ResolveWebhookAuthorityAsync(TenantId));

        Assert.Equal("FAWRY_CAPABILITY_DISABLED", exception.Code);
    }

    [Fact]
    public async Task WebhookIntegration_MustStopBeforeProtocolAndRuntimeWhenCanonicalAuthorityRejects()
    {
        await using var dbContext = CreateDbContext();
        var resolver = new Mock<IFawryTenantConfigurationResolver>();
        resolver
            .Setup(value => value.ResolveWebhookAuthorityAsync(TenantId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(FawryApplicationException.ProviderDisabled());
        var evidenceAdapter = new Mock<IFawryWebhookEvidenceAdapter>();
        var service = new FawryWebhookRuntimeIntegrationService(
            dbContext,
            resolver.Object,
            evidenceAdapter.Object,
            new PaymentRuntimeOrchestrator(),
            NullLogger<FawryWebhookRuntimeIntegrationService>.Instance);

        var exception = await Assert.ThrowsAsync<FawryApplicationException>(() =>
            service.EvaluateAndExecuteAsync(new FawryWebhookRuntimeAdmissionRequest(
                TenantId,
                SignedPayload(SecureKey),
                "closure-4-authority-rejection")));

        Assert.Equal("FAWRY_PROVIDER_DISABLED", exception.Code);
        evidenceAdapter.Verify(
            value => value.ProcessAsync(It.IsAny<FawryWebhookEvidenceRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Empty(dbContext.FawryWebhookReplayRecords);
        Assert.Empty(dbContext.PaymentWebhookLogs);
    }

    [Fact]
    public async Task WebhookIntegration_MustRejectCredentialMismatchWithoutLifecycleAuthority()
    {
        await using var dbContext = CreateDbContext();
        await SeedPaymentIntentAsync(dbContext);
        var resolver = new Mock<IFawryTenantConfigurationResolver>();
        resolver
            .Setup(value => value.ResolveWebhookAuthorityAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookAuthority());
        var service = new FawryWebhookRuntimeIntegrationService(
            dbContext,
            resolver.Object,
            new FawryWebhookEvidenceAdapter(),
            new PaymentRuntimeOrchestrator(),
            NullLogger<FawryWebhookRuntimeIntegrationService>.Instance);

        var result = await service.EvaluateAndExecuteAsync(new FawryWebhookRuntimeAdmissionRequest(
            TenantId,
            SignedPayload("wrong-secure-key-123"),
            "closure-4-credential-mismatch",
            ReceivedAtUtc: DateTimeOffset.FromUnixTimeMilliseconds(PaymentTime)));

        Assert.False(result.AdmittedToRuntime);
        Assert.Equal("FAWRY_WEBHOOK_SIGNATURE_INVALID", result.AdapterResult?.Error?.Code);
        Assert.False(FawryWebhookLifecycleAdmission.IsLifecyclePersistenceAllowed(result));
        Assert.Empty(dbContext.FawryWebhookReplayRecords);
        Assert.Empty(dbContext.PaymentWebhookLogs);
    }

    [Fact]
    public void ArchitectureGuard_MustKeepOneApplicationCredentialAuthority()
    {
        var controller = CertificationSource.ReadRepoFile("PointOfSale.API/Controllers/FawryWebhookController.cs");
        var integration = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryWebhookRuntimeIntegrationService.cs");
        var resolver = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryTenantConfigurationResolver.cs");
        var evidenceAdapter = CertificationSource.ReadRepoFile("PointOfSale.API/Services/Payments/Fawry/FawryWebhookEvidenceAdapter.cs");

        Assert.DoesNotContain("GetWebhookSecretAsync", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("TenantPaymentMethods", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("IDataProtectionProvider", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("IDataProtector", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("IOptions<", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("IConfiguration", controller, StringComparison.Ordinal);

        Assert.Contains("IFawryTenantConfigurationResolver", integration, StringComparison.Ordinal);
        Assert.Contains("ResolveWebhookAuthorityAsync", integration, StringComparison.Ordinal);
        Assert.Contains("webhookAuthority.SecureKey", integration, StringComparison.Ordinal);
        Assert.DoesNotContain("string SecureKey,", AdmissionRequestSource(integration), StringComparison.Ordinal);
        Assert.True(
            integration.IndexOf("ResolveWebhookAuthorityAsync", StringComparison.Ordinal) <
            integration.IndexOf("_webhookEvidenceAdapter.ProcessAsync", StringComparison.Ordinal));

        Assert.Contains("FawryOperationalCapability.WebhookAuthentication", resolver, StringComparison.Ordinal);
        Assert.Contains("TenantStatus.Active", resolver, StringComparison.Ordinal);
        Assert.Contains("WebhookExecutionEnabled", resolver, StringComparison.Ordinal);
        Assert.Contains("WebhookCertified", resolver, StringComparison.Ordinal);
        Assert.Contains("SupportsConfiguredServerNotification", resolver, StringComparison.Ordinal);
        Assert.Contains("options.Callback.Configured", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("options.MerchantCode", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("options.ApiKey", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("options.Secret", resolver, StringComparison.Ordinal);

        Assert.DoesNotContain("TenantPaymentMethods", evidenceAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("IFawryTenantConfigurationResolver", evidenceAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("IOptions<", evidenceAdapter, StringComparison.Ordinal);
        Assert.DoesNotContain("IConfiguration", evidenceAdapter, StringComparison.Ordinal);
    }

    private static string AdmissionRequestSource(string integration)
    {
        var start = integration.IndexOf("public sealed record FawryWebhookRuntimeAdmissionRequest", StringComparison.Ordinal);
        return start < 0 ? integration : integration[start..];
    }

    private static FawryTenantConfigurationResolver CreateResolver(
        ApplicationDbContext dbContext,
        IDataProtectionProvider protection,
        FawryOptions? options = null) =>
        new(
            dbContext,
            protection,
            Options.Create(options ?? EnabledWebhookOptions()),
            NullLogger<FawryTenantConfigurationResolver>.Instance);

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static async Task SeedActiveTenantAsync(ApplicationDbContext dbContext)
    {
        dbContext.Tenants.Add(CreateTenant(TenantStatus.Active, isActive: true));
        await dbContext.SaveChangesAsync();
    }

    private static Tenant CreateTenant(
        TenantStatus status,
        bool isActive,
        DateTime? deletedAtUtc = null) =>
        new()
        {
            Id = TenantId,
            Name = "Closure 4 Tenant",
            Slug = $"closure-4-{Guid.NewGuid():N}",
            Status = status,
            IsActive = isActive,
            DeletedAtUtc = deletedAtUtc,
            CreatedAtUtc = DateTime.UtcNow
        };

    private static TenantPaymentMethod CreatePaymentMethod(
        IDataProtectionProvider protection,
        bool isEnabled = true,
        bool isDeleted = false,
        bool isSandbox = true,
        string secureKey = SecureKey)
    {
        var configuration = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["merchantCode"] = MerchantCode,
            ["apiKey"] = ApiKey,
            ["webhookSecret"] = secureKey
        });

        return new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            Code = "FAWRY",
            DisplayName = "Fawry",
            IsEnabled = isEnabled,
            SupportsOnline = true,
            IsSandbox = isSandbox,
            IsDeleted = isDeleted,
            ConfigEncrypted = protection.CreateProtector("PaymentMethodSecrets").Protect(configuration),
            CreatedAt = DateTime.UtcNow
        };
    }

    private static FawryOptions EnabledWebhookOptions() => new()
    {
        Enabled = true,
        MerchantCode = "valid-global-merchant",
        ApiKey = "valid-global-api-key",
        Secret = "valid-global-secure-key",
        Environment = FawryEnvironment.Sandbox,
        SandboxMode = true,
        Callback = new FawryCallbackConfiguration
        {
            Configured = true,
            CallbackUrl = CallbackUrl,
            WebhookUrl = WebhookUrl
        },
        FeatureFlags = new FawryFeatureFlags
        {
            Enabled = true,
            SandboxEnabled = true,
            CredentialValidationEnabled = true,
            EnvironmentValidationEnabled = true,
            CallbackExecutionEnabled = true,
            WebhookExecutionEnabled = true
        },
        Capabilities = new FawryCapabilityModel
        {
            SupportsOfficialHttpsApi = true,
            SupportsSha256Signature = true,
            SupportsTenantScopedCredentials = true,
            SupportsSandboxEnvironment = true,
            SupportsConfiguredServerNotification = true,
            CallbackCertified = true,
            WebhookCertified = true
        }
    };

    private static FawryTenantWebhookAuthority WebhookAuthority() => new(
        TenantId,
        Guid.NewGuid(),
        MerchantCode,
        SecureKey,
        FawryEnvironment.Sandbox,
        true,
        CallbackUrl,
        WebhookUrl);

    private static async Task SeedPaymentIntentAsync(ApplicationDbContext dbContext)
    {
        var orderId = Guid.NewGuid();
        dbContext.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = TenantId,
            OrderNumber = "ORD-CLOSURE-4",
            PaymentMethod = PaymentMethod.Card,
            PaymentMethodCode = "FAWRY",
            Status = OrderStatus.Pending,
            PaymentStatus = global::PointOfSale.Core.Enums.PaymentStatus.Pending,
            Currency = "EGP",
            Total = Amount,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        dbContext.PaymentIntents.Add(new PaymentIntent
        {
            Id = Guid.NewGuid(),
            TenantId = TenantId,
            OrderId = orderId,
            Provider = PaymentProvider.Fawry,
            ProviderIntentId = MerchantReference,
            ReferenceCode = FawryReference,
            PaymentReference = MerchantReference,
            IdempotencyKey = "fawry:closure-4",
            Amount = Amount,
            Currency = "EGP",
            PaymentMethod = PaymentMethod.Card,
            Status = PaymentIntentStatus.Pending,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5)
        });
        await dbContext.SaveChangesAsync();
    }

    private static string SignedPayload(string secureKey)
    {
        const string status = "PAID";
        const string paymentMethod = "PAYATFAWRY";
        const string paymentReferenceNumber = "369552233";
        var signatureInput = string.Concat(
            FawryReference,
            MerchantReference,
            Amount.ToString("0.00", CultureInfo.InvariantCulture),
            Amount.ToString("0.00", CultureInfo.InvariantCulture),
            status,
            paymentMethod,
            paymentReferenceNumber,
            secureKey);
        var signature = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(signatureInput)))
            .ToLowerInvariant();

        return JsonSerializer.Serialize(new
        {
            requestId = "closure-4-request",
            fawryRefNumber = FawryReference,
            merchantRefNumber = MerchantReference,
            paymentAmount = Amount,
            orderAmount = Amount,
            orderStatus = status,
            paymentMethod,
            paymentTime = PaymentTime.ToString(CultureInfo.InvariantCulture),
            paymentReferenceNumber,
            messageSignature = signature
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111114");
    private static readonly Uri CallbackUrl = new("https://api.example.test/fawry/callback");
    private static readonly Uri WebhookUrl = new("https://api.example.test/webhooks/fawry");
    private const string MerchantCode = "tenant-merchant-code";
    private const string ApiKey = "tenant-api-key";
    private const string SecureKey = "tenant-secure-key-123";
    private const string FawryReference = "9990076204";
    private const string MerchantReference = "merchant-1001";
    private const decimal Amount = 350.50m;
    private const long PaymentTime = 1784111400000;
}
