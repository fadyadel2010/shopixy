using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services.Payments;
using PointOfSale.API.Services.Payments.Fawry;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.API.Services.Payments.Paymob;
using PointOfSale.API.Services.Payments.Stripe;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Admin;

public sealed class ManualWalletCheckoutReadinessTests
{
    [Theory]
    [InlineData(true, true, "01000000000", true, null, true)]
    [InlineData(true, false, "01000000000", false, "MANUAL_PAYMENT_ENVIRONMENT_MISMATCH", true)]
    [InlineData(true, false, "01000000000", true, null, false)]
    [InlineData(false, true, "01000000000", false, "TENANT_METHOD_DISABLED", true)]
    [InlineData(true, true, null, false, "TENANT_METHOD_CONFIGURATION_INVALID", true)]
    [InlineData(true, true, "bad-wallet", false, "TENANT_METHOD_CONFIGURATION_INVALID", true)]
    public async Task AdminRead_UsesCanonicalReadiness_WithoutConflatingActivationAndSandbox(
        bool enabled, bool sandbox, string? recipient, bool available, string? gate, bool runtimeSandbox)
    {
        var tenant = Guid.NewGuid();
        var context = new TenantContext();
        context.SetTenantId(tenant);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, context);
        var protection = new EphemeralDataProtectionProvider();
        var method = new TenantPaymentMethod
        {
            Id = Guid.NewGuid(), TenantId = tenant, Code = "MANUALWALLET", DisplayName = "QA wallet",
            IsEnabled = enabled, IsSandbox = sandbox, SupportsOffline = true,
            ConfigEncrypted = recipient is null ? null : protection.CreateProtector("PaymentMethodSecrets")
                .Protect(JsonSerializer.Serialize(new Dictionary<string, string> { ["walletNumber"] = recipient }))
        };
        db.TenantPaymentMethods.Add(method);
        db.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), Code = "MANUALWALLET", DisplayName = "Foreign wallet",
            IsEnabled = true, IsSandbox = true, SupportsOffline = true
        });
        await db.SaveChangesAsync();
        var settings = ReadySandboxOptions();
        if (!runtimeSandbox)
        {
            settings = settings with
            {
                Environment = ManualPaymentEnvironment.Production, SandboxMode = false,
                FeatureFlags = settings.FeatureFlags with { ProductionEnabled = true }
            };
        }
        var options = Options.Create(settings);
        var runtime = new StorefrontPaymentRuntimeReadinessService(db, protection,
            Mock.Of<IStripeTenantConfigurationResolver>(), Mock.Of<IPaymobTenantConfigurationResolver>(),
            Mock.Of<IFawryTenantConfigurationResolver>(), new ManualPaymentTenantConfigurationResolver(db, options),
            Options.Create(new StripeOptions()), Options.Create(new PaymobOptions()), Options.Create(new FawryOptions()),
            NullLogger<StorefrontPaymentRuntimeReadinessService>.Instance);
        var controller = new PaymentMethodsAdminController(db, Mock.Of<IEventPublisher>(), protection,
            new CommerceReliabilityTelemetry(), NullLogger<PaymentMethodsAdminController>.Instance, context, runtime)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

        var dto = Assert.IsType<PaymentMethodResponseDto>(Assert.IsType<OkObjectResult>(
            await controller.GetPaymentMethod("MANUALWALLET", default)).Value);
        Assert.Equal(enabled, dto.IsEnabled);
        Assert.Equal(sandbox, dto.IsSandbox);
        Assert.Equal(available, dto.IsAvailableForCheckout);
        Assert.Equal(gate, dto.CheckoutUnavailableReasonCode);
        var list = Assert.IsType<List<PaymentMethodResponseDto>>(Assert.IsType<OkObjectResult>(
            await controller.GetPaymentMethods(default)).Value);
        Assert.Equal(dto.Id, Assert.Single(list).Id);
        Assert.Equal(available, list[0].IsAvailableForCheckout);
        Assert.Equal(gate, list[0].CheckoutUnavailableReasonCode);
        Assert.Equal("no-store, no-cache, max-age=0", controller.Response.Headers.CacheControl);
        Assert.IsType<NotFoundObjectResult>(await controller.GetPaymentMethod("STRIPE", default));
    }

    private static ManualPaymentOptions ReadySandboxOptions() => new()
    {
        Enabled = true, Environment = ManualPaymentEnvironment.Sandbox, SandboxMode = true,
        FeatureFlags = new()
        {
            Enabled = true, SandboxEnabled = true, ProofUploadEnabled = true,
            ReviewExecutionEnabled = true, ApprovalExecutionEnabled = true, RejectionExecutionEnabled = true
        },
        Capabilities = new()
        {
            ProviderAdapterCertified = true, RuntimeBridgeCertified = true, EvidenceAdapterCertified = true,
            ReviewWorkflowCertified = true, ApprovalWorkflowCertified = true, PersistenceCertified = true
        },
        Instructions = new() { Configured = true, DefaultInstructions = "TEST ONLY" },
        Expiration = new() { Configured = true },
        Channels = [new() { Channel = ManualPaymentChannel.VodafoneCash, Enabled = true, DisplayName = "QA", Instructions = "TEST ONLY" }]
    };
}
