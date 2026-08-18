using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Admin;

public sealed class AdminManualPaymentPendingTests
{
    [Fact]
    public async Task GetPendingPayments_WithValidManualWalletConfigurationAndEmptyQueue_ReturnsOkEmptyPage()
    {
        var tenantId = Guid.NewGuid();
        var dataProtectionProvider = new EphemeralDataProtectionProvider();
        var dataProtector = dataProtectionProvider.CreateProtector("PaymentMethodSecrets");
        await using var dbContext = CreateDbContext(tenantId);
        dbContext.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = ManualPaymentTenantConfigurationResolver.TenantPaymentMethodCode,
            DisplayName = "Manual Wallet",
            IsEnabled = true,
            SupportsOffline = true,
            SupportsOnline = false,
            IsSandbox = true,
            FeeType = "fixed",
            ConfigEncrypted = dataProtector.Protect("""{"walletNumber":"01099998888"}"""),
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, tenantId, ValidManualPaymentOptions());

        var result = await controller.GetPendingPayments(page: 1, pageSize: 25, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PendingManualPaymentPagedResponse>(ok.Value);

        Assert.Empty(response.Items);
        Assert.Equal(0, response.TotalCount);
        Assert.Equal(1, response.Page);
        Assert.Equal(25, response.PageSize);
    }

    [Fact]
    public async Task GetPendingPayments_WithInvalidGlobalManualPaymentConfiguration_ReturnsConfigurationError()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantId);
        var controller = CreateController(dbContext, tenantId, new ManualPaymentOptions());

        var result = await controller.GetPendingPayments(page: 1, pageSize: 25, cancellationToken: CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);

        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
        Assert.Contains("MANUAL_PAYMENT_CONFIGURATION_INVALID", SerializeToJson(objectResult.Value));
    }

    [Fact]
    public async Task GetPendingPayments_WithFreshManualWalletPayment_ReturnsMatchingTenantOrderIdentity()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var paymentIntentId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;
        await using var dbContext = CreateDbContext(tenantId);
        await SeedManualPaymentConfigurationAsync(dbContext, tenantId);

        dbContext.Orders.AddRange(
            NewPendingManualWalletOrder(orderId, tenantId, "ORD-FRESH-MANUAL-WALLET", nowUtc),
            NewPendingManualWalletOrder(Guid.NewGuid(), otherTenantId, "ORD-OTHER-TENANT", nowUtc.AddMinutes(1)));
        dbContext.PaymentIntents.AddRange(
            NewPendingManualWalletIntent(paymentIntentId, orderId, tenantId, nowUtc),
            NewPendingManualWalletIntent(Guid.NewGuid(), dbContext.Orders.Local.Single(order => order.TenantId == otherTenantId).Id, otherTenantId, nowUtc.AddMinutes(1)));
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, tenantId, ValidManualPaymentOptions());

        var result = await controller.GetPendingPayments(page: 1, pageSize: 25, cancellationToken: CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PendingManualPaymentPagedResponse>(ok.Value);
        var pending = Assert.Single(response.Items);
        Assert.Equal(paymentIntentId, pending.PaymentIntentId);
        Assert.Equal(orderId, pending.OrderId);
        Assert.Equal("ORD-FRESH-MANUAL-WALLET", pending.OrderNumber);
        Assert.True(pending.HasProofFile);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task GetProofImage_WithCurrentTenantScopedProof_StreamsAuthorizedStorageObject()
    {
        var tenantId = Guid.NewGuid();
        var paymentIntentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var proofKey = $"tenants/{tenantId:D}/manual-wallet-proofs/checkout-sessions/{Guid.NewGuid():D}/proof.png";
        var nowUtc = DateTime.UtcNow;
        await using var dbContext = CreateDbContext(tenantId);
        await SeedManualPaymentConfigurationAsync(dbContext, tenantId);
        dbContext.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-PROOF-STREAM",
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            Subtotal = 100m,
            Total = 100m,
            Currency = "EGP",
            CreatedAt = nowUtc
        });
        dbContext.PaymentIntents.Add(new PaymentIntent
        {
            Id = paymentIntentId,
            TenantId = tenantId,
            OrderId = orderId,
            Amount = 100m,
            Currency = "EGP",
            PaymentMethod = PaymentMethod.BankTransfer,
            Provider = PaymentProvider.ManualWallet,
            PaymentReference = "PAY-PROOF-STREAM",
            IdempotencyKey = $"proof-{paymentIntentId:N}",
            Status = PaymentIntentStatus.AwaitingVerification,
            ProofImageUrl = proofKey,
            ProofImageExpiresAtUtc = nowUtc.AddDays(7),
            CreatedAt = nowUtc
        });
        await dbContext.SaveChangesAsync();

        var expectedBytes = new byte[] { 1, 2, 3, 4 };
        var storage = new Mock<IR2StorageService>(MockBehavior.Strict);
        storage
            .Setup(service => service.DownloadObjectAsync(
                proofKey,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new R2DownloadObjectResult(
                new MemoryStream(expectedBytes),
                "image/png",
                expectedBytes.Length,
                "etag"));

        var controller = CreateController(
            dbContext,
            tenantId,
            ValidManualPaymentOptions(),
            storage.Object,
            new ManualPaymentRetentionAuthority(Options.Create(ValidManualPaymentOptions())));

        var result = await controller.GetProofImage(paymentIntentId, CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("image/png", file.ContentType);
        using var actual = new MemoryStream();
        await file.FileStream.CopyToAsync(actual);
        Assert.Equal(expectedBytes, actual.ToArray());
        storage.VerifyAll();
    }

    [Fact]
    public async Task GetProofImage_WithPaymentOrderTenantMismatch_DoesNotTouchStorage()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var paymentIntentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var proofKey = $"tenants/{tenantId:D}/manual-wallet-proofs/checkout-sessions/{Guid.NewGuid():D}/proof.png";
        var nowUtc = DateTime.UtcNow;
        await using var dbContext = CreateDbContext(tenantId);
        await SeedManualPaymentConfigurationAsync(dbContext, tenantId);
        dbContext.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = otherTenantId,
            OrderNumber = "ORD-PROOF-CROSS-TENANT",
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            Subtotal = 100m,
            Total = 100m,
            Currency = "EGP",
            CreatedAt = nowUtc
        });
        dbContext.PaymentIntents.Add(new PaymentIntent
        {
            Id = paymentIntentId,
            TenantId = tenantId,
            OrderId = orderId,
            Amount = 100m,
            Currency = "EGP",
            PaymentMethod = PaymentMethod.BankTransfer,
            Provider = PaymentProvider.ManualWallet,
            PaymentReference = "PAY-PROOF-CROSS-TENANT",
            IdempotencyKey = $"proof-{paymentIntentId:N}",
            Status = PaymentIntentStatus.AwaitingVerification,
            ProofImageUrl = proofKey,
            ProofImageExpiresAtUtc = nowUtc.AddDays(7),
            CreatedAt = nowUtc
        });
        await dbContext.SaveChangesAsync();

        var storage = new Mock<IR2StorageService>(MockBehavior.Strict);
        var controller = CreateController(
            dbContext,
            tenantId,
            ValidManualPaymentOptions(),
            storage.Object,
            new ManualPaymentRetentionAuthority(Options.Create(ValidManualPaymentOptions())));

        var result = await controller.GetProofImage(paymentIntentId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        storage.Verify(
            service => service.DownloadObjectAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetProofImage_WithWrongTenantScopedProofKey_DoesNotTouchStorage()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var paymentIntentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var proofKey = $"tenants/{otherTenantId:D}/manual-wallet-proofs/checkout-sessions/{Guid.NewGuid():D}/proof.png";
        var nowUtc = DateTime.UtcNow;
        await using var dbContext = CreateDbContext(tenantId);
        await SeedManualPaymentConfigurationAsync(dbContext, tenantId);
        dbContext.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-PROOF-WRONG-KEY",
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            Subtotal = 100m,
            Total = 100m,
            Currency = "EGP",
            CreatedAt = nowUtc
        });
        dbContext.PaymentIntents.Add(new PaymentIntent
        {
            Id = paymentIntentId,
            TenantId = tenantId,
            OrderId = orderId,
            Amount = 100m,
            Currency = "EGP",
            PaymentMethod = PaymentMethod.BankTransfer,
            Provider = PaymentProvider.ManualWallet,
            PaymentReference = "PAY-PROOF-WRONG-KEY",
            IdempotencyKey = $"proof-{paymentIntentId:N}",
            Status = PaymentIntentStatus.AwaitingVerification,
            ProofImageUrl = proofKey,
            ProofImageExpiresAtUtc = nowUtc.AddDays(7),
            CreatedAt = nowUtc
        });
        await dbContext.SaveChangesAsync();

        var storage = new Mock<IR2StorageService>(MockBehavior.Strict);
        var controller = CreateController(
            dbContext,
            tenantId,
            ValidManualPaymentOptions(),
            storage.Object,
            new ManualPaymentRetentionAuthority(Options.Create(ValidManualPaymentOptions())));

        var result = await controller.GetProofImage(paymentIntentId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        storage.Verify(
            service => service.DownloadObjectAsync(
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static AdminManualPaymentController CreateController(
        ApplicationDbContext dbContext,
        Guid tenantId,
        ManualPaymentOptions options,
        IR2StorageService? storageService = null,
        IManualPaymentRetentionAuthority? retentionAuthority = null)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")) },
                "test"))
        };
        var admissionAuthority = new ManualPaymentTenantConfigurationResolver(
            dbContext,
            Options.Create(options));
        var expirationAuthority = new Mock<IManualPaymentExpirationAuthority>();
        expirationAuthority
            .Setup(authority => authority.ExpireElapsedPendingPaymentsAsync(
                tenantId,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new AdminManualPaymentController(
            Mock.Of<IManualPaymentReviewApplicationService>(),
            admissionAuthority,
            expirationAuthority.Object,
            retentionAuthority ?? Mock.Of<IManualPaymentRetentionAuthority>(),
            tenantContext,
            dbContext,
            storageService ?? Mock.Of<IR2StorageService>(),
            Options.Create(new R2Options()),
            NullLogger<AdminManualPaymentController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static async Task SeedManualPaymentConfigurationAsync(
        ApplicationDbContext dbContext,
        Guid tenantId)
    {
        var dataProtector = new EphemeralDataProtectionProvider()
            .CreateProtector("PaymentMethodSecrets");
        dbContext.TenantPaymentMethods.Add(new TenantPaymentMethod
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = ManualPaymentTenantConfigurationResolver.TenantPaymentMethodCode,
            DisplayName = "Manual Wallet",
            IsEnabled = true,
            SupportsOffline = true,
            SupportsOnline = false,
            IsSandbox = true,
            FeeType = "fixed",
            ConfigEncrypted = dataProtector.Protect("{\"walletNumber\":\"01099998888\"}"),
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    private static Order NewPendingManualWalletOrder(
        Guid orderId,
        Guid tenantId,
        string orderNumber,
        DateTime createdAtUtc) => new()
    {
        Id = orderId,
        TenantId = tenantId,
        OrderNumber = orderNumber,
        GuestName = "Manual Wallet Guest",
        Status = OrderStatus.Pending,
        PaymentStatus = PaymentStatus.AwaitingVerification,
        PaymentMethod = PaymentMethod.BankTransfer,
        PaymentMethodCode = "MANUALWALLET",
        Subtotal = 100m,
        Total = 100m,
        Currency = "EGP",
        CreatedAt = createdAtUtc
    };

    private static PaymentIntent NewPendingManualWalletIntent(
        Guid paymentIntentId,
        Guid orderId,
        Guid tenantId,
        DateTime createdAtUtc) => new()
    {
        Id = paymentIntentId,
        TenantId = tenantId,
        OrderId = orderId,
        Amount = 100m,
        Currency = "EGP",
        PaymentMethod = PaymentMethod.BankTransfer,
        Provider = PaymentProvider.ManualWallet,
        PaymentReference = $"PAY-{paymentIntentId:N}",
        IdempotencyKey = $"manual-wallet-{paymentIntentId:N}",
        Status = PaymentIntentStatus.AwaitingVerification,
        WalletType = "VODAFONECASH",
        WalletPhoneNumber = "+201000000000",
        ProofImageUrl = $"tenants/{tenantId:D}/manual-wallet-proofs/checkout-sessions/{Guid.NewGuid():D}/proof.png",
        ExpiresAtUtc = createdAtUtc.AddDays(2),
        ProofImageExpiresAtUtc = createdAtUtc.AddDays(7),
        CreatedAt = createdAtUtc
    };

    private static ApplicationDbContext CreateDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        return new ApplicationDbContext(options, tenantContext);
    }

    private static ManualPaymentOptions ValidManualPaymentOptions() => new()
    {
        Enabled = true,
        Environment = ManualPaymentEnvironment.Sandbox,
        SandboxMode = true,
        FeatureFlags = new ManualPaymentFeatureFlags
        {
            Enabled = true,
            SandboxEnabled = true,
            ProductionEnabled = false,
            FoundationHealthEnabled = true,
            ConfigurationValidationEnabled = true,
            ProofUploadEnabled = true,
            ReviewExecutionEnabled = true,
            ApprovalExecutionEnabled = true,
            RejectionExecutionEnabled = true
        },
        Capabilities = new ManualPaymentCapabilityModel
        {
            ProviderAdapterCertified = true,
            RuntimeBridgeCertified = true,
            EvidenceAdapterCertified = true,
            ReviewWorkflowCertified = true,
            ApprovalWorkflowCertified = true,
            PersistenceCertified = true
        },
        Instructions = new ManualPaymentInstructionConfiguration
        {
            Configured = true,
            DefaultInstructions = "Transfer to the merchant wallet configured for this store and upload proof.",
            RequireReferenceNumber = true,
            RequireCustomerProof = true
        },
        Proof = new ManualPaymentProofConfiguration(),
        Expiration = new ManualPaymentExpirationPolicy
        {
            Configured = true,
            PendingPaymentTtl = TimeSpan.FromDays(2),
            ProofAccessTtl = TimeSpan.FromDays(7)
        },
        Channels = new[]
        {
            new ManualPaymentChannelConfiguration
            {
                Channel = ManualPaymentChannel.VodafoneCash,
                Enabled = true,
                DisplayName = "Vodafone Cash",
                Instructions = "Transfer to the configured merchant wallet number and upload proof."
            }
        }
    };

    private static string SerializeToJson(object? value) =>
        System.Text.Json.JsonSerializer.Serialize(value);
}
