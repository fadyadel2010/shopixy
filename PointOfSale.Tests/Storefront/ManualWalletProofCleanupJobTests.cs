using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Jobs;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Storefront;

public sealed class ManualWalletProofCleanupJobTests
{
    [Fact]
    public async Task RunAsync_WhenExpiredCloudProofExists_DeletesObject_ClearsReference_And_RecordsMetric()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var paymentIntentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var databaseName = $"manual-wallet-proof-cleanup-{Guid.NewGuid():N}";
        var databaseRoot = new InMemoryDatabaseRoot();

        await using var dbContext = CreateDbContext(tenantId, databaseName, databaseRoot);

        dbContext.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-CLEANUP-1",
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            PaymentMethod = PaymentMethod.Card,
            Total = 120m,
            Currency = "EGP",
            CreatedAt = DateTime.UtcNow
        });

        dbContext.PaymentIntents.Add(new PaymentIntent
        {
            Id = paymentIntentId,
            OrderId = orderId,
            TenantId = tenantId,
            Amount = 120m,
            Currency = "EGP",
            PaymentMethod = PaymentMethod.Card,
            Provider = PaymentProvider.ManualWallet,
            PaymentReference = "PAY-CLEANUP-1",
            IdempotencyKey = "cleanup-proof-1",
            CreatedAt = now.AddDays(-10),
            ProofImageUrl = $"tenants/{tenantId:D}/manual-wallet-proofs/proof-1.jpg",
            ProofImageExpiresAtUtc = now.AddHours(-4),
            ProofImageDeleted = false
        });
        await dbContext.SaveChangesAsync();
        var seededPaymentIntent = await dbContext.PaymentIntents.SingleAsync();
        seededPaymentIntent.CreatedAt = now.AddDays(-10);
        seededPaymentIntent.ProofImageExpiresAtUtc = now.AddHours(-4);
        await dbContext.SaveChangesAsync();

        var storageService = new Mock<IR2StorageService>();
        storageService
            .Setup(service => service.DeleteObjectAsync(
                $"tenants/{tenantId:D}/manual-wallet-proofs/proof-1.jpg",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var telemetry = new CommerceReliabilityTelemetry();
        long deletedCount = 0;
        using var listener = CreateCounterListener(
            "orphan_files_deleted_total",
            measurement => Interlocked.Add(ref deletedCount, measurement));

        var services = new ServiceCollection()
            .AddScoped(_ => CreateDbContext(tenantId, databaseName, databaseRoot))
            .AddScoped(_ => storageService.Object)
            .AddScoped(_ => Options.Create(new R2Options()))
            .AddScoped(_ => Options.Create(EnabledManualPaymentOptions()))
            .AddScoped<IManualPaymentRetentionAuthority, ManualPaymentRetentionAuthority>()
            .AddScoped(_ => telemetry)
            .BuildServiceProvider();

        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.ContentRootPath).Returns(Path.GetTempPath());

        var job = new ManualWalletProofCleanupJob(
            services,
            environment.Object,
            NullLogger<ManualWalletProofCleanupJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        await using var assertDbContext = CreateDbContext(tenantId, databaseName, databaseRoot);
        var paymentIntent = await assertDbContext.PaymentIntents.SingleAsync();
        Assert.True(paymentIntent.ProofImageDeleted);
        Assert.Null(paymentIntent.ProofImageUrl);
        Assert.Equal(1, deletedCount);
        storageService.Verify(service => service.DeleteObjectAsync(
            $"tenants/{tenantId:D}/manual-wallet-proofs/proof-1.jpg",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenStaleCheckoutSessionProofExists_DeletesObject_And_ClearsWalletRecoveryFields()
    {
        var tenantId = Guid.NewGuid();
        var checkoutSessionId = Guid.NewGuid();
        var databaseName = $"manual-wallet-session-cleanup-{Guid.NewGuid():N}";
        var databaseRoot = new InMemoryDatabaseRoot();
        var objectKey = $"tenants/{tenantId:D}/manual-wallet-proofs/checkout-sessions/{checkoutSessionId:D}/proof.png";

        await using var dbContext = CreateDbContext(tenantId, databaseName, databaseRoot);
        dbContext.CheckoutSessions.Add(new CheckoutSession
        {
            Id = checkoutSessionId,
            TenantId = tenantId,
            CartId = Guid.NewGuid(),
            SessionId = "guest-session-1",
            Status = CheckoutSessionStatus.Active,
            PaymentMethodCode = "MANUALWALLET",
            WalletType = "vodafone_cash",
            WalletPhoneNumber = "01011112222",
            ProofImageUrl = objectKey,
            ItemsSnapshotJson = "[]",
            CartTotal = 150m,
            Currency = "EGP",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-2),
            LastActivityAtUtc = DateTime.UtcNow.AddHours(-30)
        });
        await dbContext.SaveChangesAsync();

        var storageService = new Mock<IR2StorageService>();
        storageService
            .Setup(service => service.DeleteObjectAsync(objectKey, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        using var telemetry = new CommerceReliabilityTelemetry();
        long deletedCount = 0;
        using var listener = CreateCounterListener(
            "orphan_files_deleted_total",
            measurement => Interlocked.Add(ref deletedCount, measurement));

        var services = new ServiceCollection()
            .AddScoped(_ => CreateDbContext(tenantId, databaseName, databaseRoot))
            .AddScoped(_ => storageService.Object)
            .AddScoped(_ => Options.Create(new R2Options()))
            .AddScoped(_ => Options.Create(EnabledManualPaymentOptions()))
            .AddScoped<IManualPaymentRetentionAuthority, ManualPaymentRetentionAuthority>()
            .AddScoped(_ => telemetry)
            .BuildServiceProvider();

        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(value => value.ContentRootPath).Returns(Path.GetTempPath());

        var job = new ManualWalletProofCleanupJob(
            services,
            environment.Object,
            NullLogger<ManualWalletProofCleanupJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        await using var assertDbContext = CreateDbContext(tenantId, databaseName, databaseRoot);
        var checkoutSession = await assertDbContext.CheckoutSessions
            .IgnoreQueryFilters()
            .SingleAsync();

        Assert.Null(checkoutSession.ProofImageUrl);
        Assert.Null(checkoutSession.WalletType);
        Assert.Null(checkoutSession.WalletPhoneNumber);
        Assert.Equal(1, deletedCount);
        storageService.Verify(service => service.DeleteObjectAsync(objectKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId, string databaseName, InMemoryDatabaseRoot databaseRoot)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName, databaseRoot)
            .Options;

        return new ApplicationDbContext(options, new TestTenantContext(tenantId));
    }

    private static MeterListener CreateCounterListener(string instrumentName, Action<long> onMeasurement)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == CommerceReliabilityTelemetry.MeterName &&
                    instrument.Name == instrumentName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            onMeasurement(measurement);
        });
        listener.Start();
        return listener;
    }

    private static ManualPaymentOptions EnabledManualPaymentOptions() => new()
    {
        Enabled = true,
        Environment = ManualPaymentEnvironment.Sandbox,
        SandboxMode = true,
        FeatureFlags = new ManualPaymentFeatureFlags
        {
            Enabled = true,
            SandboxEnabled = true,
            ProofUploadEnabled = true,
            ReviewExecutionEnabled = true
        },
        Capabilities = new ManualPaymentCapabilityModel
        {
            ProviderAdapterCertified = true,
            RuntimeBridgeCertified = true,
            EvidenceAdapterCertified = true,
            ReviewWorkflowCertified = true,
            ApprovalWorkflowCertified = true,
            SupportsReceiptProofUpload = true,
            SupportsExpirationPolicy = true
        },
        Instructions = new ManualPaymentInstructionConfiguration
        {
            Configured = true,
            DefaultInstructions = "Transfer manually.",
            RequireCustomerProof = true
        },
        Expiration = new ManualPaymentExpirationPolicy
        {
            Configured = true,
            PendingPaymentTtl = TimeSpan.FromHours(2),
            ProofAccessTtl = TimeSpan.FromDays(7)
        },
        Channels =
        [
            new ManualPaymentChannelConfiguration
            {
                Channel = ManualPaymentChannel.BankTransfer,
                Enabled = true,
                DisplayName = "Bank transfer",
                Instructions = "Transfer manually."
            }
        ]
    };
}
