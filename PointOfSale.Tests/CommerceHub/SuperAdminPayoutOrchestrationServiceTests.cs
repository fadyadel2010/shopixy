using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class SuperAdminPayoutOrchestrationServiceTests
{
    [Fact]
    public async Task PrepareAsync_CommitsProcessingHandoffWithoutCompletingPayout()
    {
        await using var dbContext = CreateDbContext();
        var vendorId = Guid.NewGuid();
        var payoutMethodId = Guid.NewGuid();
        var earningId = Guid.NewGuid();
        SeedExecutionMapping(dbContext, vendorId, payoutMethodId, earningId, amount: 50m);

        var actorId = Guid.NewGuid();
        var request = new SuperAdminPrepareVendorPayoutRequestDto
        {
            Amount = 50m,
            PayoutMethodId = payoutMethodId,
            IdempotencyKey = "super-admin-payout-canonical"
        };
        var payoutId = Guid.NewGuid();
        var externalTransferIdempotencyKey = "CH-PAYOUT-INTENT";
        var handoffToken = "HANDOFF-TOKEN";
        var sequence = new MockSequence();
        var canonicalPayoutService = new Mock<ICommerceHubPayoutService>(MockBehavior.Strict);
        canonicalPayoutService
            .InSequence(sequence)
            .Setup(service => service.CreatePayoutAsync(
                vendorId,
                It.Is<CommerceHubCreatePayoutRequestDto>(create =>
                    create.PayoutMethodId == payoutMethodId &&
                    create.Method == CommerceHubPayoutMethod.BankTransfer &&
                    create.IdempotencyKey == request.IdempotencyKey &&
                    create.Reference == null &&
                    create.EarningIds.SequenceEqual(new[] { earningId })),
                It.Is<string>(correlationId => correlationId.Contains(request.IdempotencyKey, StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CommerceHubPayoutDto
            {
                PayoutId = payoutId,
                TenantId = vendorId,
                PayoutMethodId = payoutMethodId,
                Amount = request.Amount,
                Status = "Pending",
                ExternalTransferIdempotencyKey = externalTransferIdempotencyKey
            });
        canonicalPayoutService
            .InSequence(sequence)
            .Setup(service => service.MarkProcessingAsync(
                vendorId,
                payoutId,
                It.Is<CommerceHubUpdatePayoutRequestDto>(update =>
                    update.Reference == $"SUPER-ADMIN-HANDOFF:{payoutId:N}" &&
                    update.ExternalReferenceId == null &&
                    update.BankTransactionId == null &&
                    update.ExternalTransferIdempotencyKey == externalTransferIdempotencyKey &&
                    !update.ExternalTransferConfirmedExecuted),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                "ops@commercehub.test"))
            .ReturnsAsync(new CommerceHubPayoutDto
            {
                PayoutId = payoutId,
                TenantId = vendorId,
                Amount = request.Amount,
                Status = "Processing",
                ExternalTransferIdempotencyKey = externalTransferIdempotencyKey,
                ExternalTransferHandoffToken = handoffToken,
                ExternalTransferHandoffCommittedAtUtc = DateTime.UtcNow
            });
        using var telemetry = new SuperAdminPayoutOrchestrationTelemetry();
        var orchestration = new SuperAdminPayoutOrchestrationService(
            dbContext,
            canonicalPayoutService.Object,
            telemetry,
            NullLogger<SuperAdminPayoutOrchestrationService>.Instance);

        var actual = await orchestration.PrepareAsync(
            vendorId,
            request,
            actorId,
            "ops@commercehub.test");

        Assert.Equal("Processing", actual.Status);
        canonicalPayoutService.Verify(service => service.MarkCompletedAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<CommerceHubUpdatePayoutRequestDto>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<string>()), Times.Never);
        canonicalPayoutService.VerifyAll();
    }

    [Fact]
    public async Task CompleteAsync_ReconcilesCommittedHandoffWithoutCreatingOrProcessingPayout()
    {
        await using var dbContext = CreateDbContext();
        var vendorId = Guid.NewGuid();
        var payoutId = Guid.NewGuid();
        var handoffToken = "COMMITTED-HANDOFF-TOKEN";
        var canonicalPayoutService = new Mock<ICommerceHubPayoutService>(MockBehavior.Strict);
        canonicalPayoutService
            .Setup(service => service.MarkCompletedAsync(
                vendorId,
                payoutId,
                It.Is<CommerceHubUpdatePayoutRequestDto>(update =>
                    update.ExternalReferenceId == "EXT-BANK-TRF-001" &&
                    update.BankTransactionId == "BANK-TXN-001" &&
                    update.ExternalTransferHandoffToken == handoffToken &&
                    update.ExternalTransferConfirmedExecuted),
                It.Is<string>(correlationId => correlationId.Contains(payoutId.ToString("N"), StringComparison.Ordinal)),
                It.IsAny<CancellationToken>(),
                "ops@commercehub.test"))
            .ReturnsAsync(new CommerceHubPayoutDto
            {
                PayoutId = payoutId,
                TenantId = vendorId,
                Amount = 50m,
                Status = "Completed"
            });

        using var telemetry = new SuperAdminPayoutOrchestrationTelemetry();
        var orchestration = new SuperAdminPayoutOrchestrationService(
            dbContext,
            canonicalPayoutService.Object,
            telemetry,
            NullLogger<SuperAdminPayoutOrchestrationService>.Instance);

        var actual = await orchestration.CompleteAsync(
            vendorId,
            payoutId,
            new SuperAdminCompleteVendorPayoutRequestDto
            {
                ExternalReferenceId = "EXT-BANK-TRF-001",
                BankTransactionId = "BANK-TXN-001",
                ExternalTransferHandoffToken = handoffToken,
                ExternalTransferConfirmedExecuted = true
            },
            Guid.NewGuid(),
            "ops@commercehub.test");

        Assert.Equal("Completed", actual.Status);
        canonicalPayoutService.Verify(service => service.CreatePayoutAsync(
            It.IsAny<Guid>(),
            It.IsAny<CommerceHubCreatePayoutRequestDto>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        canonicalPayoutService.Verify(service => service.MarkProcessingAsync(
            It.IsAny<Guid>(),
            It.IsAny<Guid>(),
            It.IsAny<CommerceHubUpdatePayoutRequestDto>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<string>()), Times.Never);
        canonicalPayoutService.VerifyAll();
    }

    [Fact]
    public async Task CompleteAsync_WithoutExplicitExecutionConfirmation_DoesNotCallCanonicalCompletion()
    {
        await using var dbContext = CreateDbContext();
        var canonicalPayoutService = new Mock<ICommerceHubPayoutService>(MockBehavior.Strict);
        using var telemetry = new SuperAdminPayoutOrchestrationTelemetry();
        var orchestration = new SuperAdminPayoutOrchestrationService(
            dbContext,
            canonicalPayoutService.Object,
            telemetry,
            NullLogger<SuperAdminPayoutOrchestrationService>.Instance);

        var exception = await Assert.ThrowsAsync<CommerceHubVendorFinancialControlException>(() =>
            orchestration.CompleteAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new SuperAdminCompleteVendorPayoutRequestDto
                {
                    ExternalReferenceId = "EXT-BANK-TRF-001",
                    ExternalTransferHandoffToken = "COMMITTED-HANDOFF-TOKEN"
                },
                Guid.NewGuid(),
                "ops@commercehub.test"));

        Assert.Equal("PAYOUT_EXTERNAL_TRANSFER_EXECUTION_CONFIRMATION_REQUIRED", exception.Code);
        canonicalPayoutService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PrepareAsync_RetryResumesExistingPendingPayoutWithoutCreatingAnother()
    {
        await using var dbContext = CreateDbContext();
        var vendorId = Guid.NewGuid();
        var payoutMethodId = Guid.NewGuid();
        var payoutId = Guid.NewGuid();
        var idempotencyKey = "super-admin-payout-resume-pending";
        var transferIntent = "CH-PAYOUT-INTENT-RESUME";
        dbContext.CommerceHubPayouts.Add(new CommerceHubPayout
        {
            Id = payoutId,
            TenantId = vendorId,
            PayoutMethodId = payoutMethodId,
            Amount = 50m,
            Currency = "EGP",
            Status = CommerceHubPayoutStatus.Pending,
            Method = CommerceHubPayoutMethod.BankTransfer,
            DestinationValueSnapshot = "EG-ORCH-RESUME",
            IdempotencyKey = idempotencyKey,
            ExternalTransferIdempotencyKey = transferIntent,
            CreatedAt = DateTime.UtcNow
        });
        using (CommerceHubBoundaryMutationScope.BeginPayout())
        using (CommerceHubBoundaryMutationScope.BeginFinancialWorkflow())
        {
            await dbContext.SaveChangesAsync();
        }

        var canonicalPayoutService = new Mock<ICommerceHubPayoutService>(MockBehavior.Strict);
        canonicalPayoutService
            .Setup(service => service.MarkProcessingAsync(
                vendorId,
                payoutId,
                It.Is<CommerceHubUpdatePayoutRequestDto>(update =>
                    update.ExternalTransferIdempotencyKey == transferIntent &&
                    !update.ExternalTransferConfirmedExecuted),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string>()))
            .ReturnsAsync(new CommerceHubPayoutDto
            {
                PayoutId = payoutId,
                TenantId = vendorId,
                PayoutMethodId = payoutMethodId,
                Amount = 50m,
                Currency = "EGP",
                Status = "Processing",
                Method = "BankTransfer",
                IdempotencyKey = idempotencyKey,
                ExternalTransferIdempotencyKey = transferIntent,
                ExternalTransferHandoffToken = "COMMITTED-HANDOFF-TOKEN",
                ExternalTransferHandoffCommittedAtUtc = DateTime.UtcNow
            });

        using var telemetry = new SuperAdminPayoutOrchestrationTelemetry();
        var orchestration = new SuperAdminPayoutOrchestrationService(
            dbContext,
            canonicalPayoutService.Object,
            telemetry,
            NullLogger<SuperAdminPayoutOrchestrationService>.Instance);

        var result = await orchestration.PrepareAsync(
            vendorId,
            new SuperAdminPrepareVendorPayoutRequestDto
            {
                Amount = 50m,
                PayoutMethodId = payoutMethodId,
                IdempotencyKey = idempotencyKey
            },
            Guid.NewGuid(),
            "ops@commercehub.test");

        Assert.Equal("Processing", result.Status);
        canonicalPayoutService.Verify(service => service.CreatePayoutAsync(
            It.IsAny<Guid>(),
            It.IsAny<CommerceHubCreatePayoutRequestDto>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        canonicalPayoutService.VerifyAll();
    }

    [Fact]
    public async Task PrepareAsync_DoesNotTranslateCanonicalBusinessExceptions()
    {
        await using var dbContext = CreateDbContext();
        var vendorId = Guid.NewGuid();
        var payoutMethodId = Guid.NewGuid();
        SeedExecutionMapping(dbContext, vendorId, payoutMethodId, Guid.NewGuid(), amount: 50m);
        var request = new SuperAdminPrepareVendorPayoutRequestDto
        {
            Amount = 50m,
            PayoutMethodId = payoutMethodId,
            IdempotencyKey = "super-admin-payout-business-exception"
        };
        var expected = new CommerceHubCheckoutException(
            "PAYOUT_AMOUNT_EXCEEDS_PAYABLE",
            "Payout amount cannot exceed the vendor payable balance.",
            StatusCodes.Status409Conflict);

        var canonicalPayoutService = new Mock<ICommerceHubPayoutService>(MockBehavior.Strict);
        canonicalPayoutService
            .Setup(service => service.CreatePayoutAsync(
                vendorId,
                It.IsAny<CommerceHubCreatePayoutRequestDto>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        using var telemetry = new SuperAdminPayoutOrchestrationTelemetry();
        var orchestration = new SuperAdminPayoutOrchestrationService(
            dbContext,
            canonicalPayoutService.Object,
            telemetry,
            NullLogger<SuperAdminPayoutOrchestrationService>.Instance);

        var actual = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            orchestration.PrepareAsync(vendorId, request, Guid.NewGuid(), null));

        Assert.Same(expected, actual);
        canonicalPayoutService.VerifyAll();
        canonicalPayoutService.Verify(
            service => service.MarkProcessingAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CommerceHubUpdatePayoutRequestDto>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task PrepareAsync_WrapsUnexpectedInfrastructureFailures()
    {
        await using var dbContext = CreateDbContext();
        var vendorId = Guid.NewGuid();
        var payoutMethodId = Guid.NewGuid();
        SeedExecutionMapping(dbContext, vendorId, payoutMethodId, Guid.NewGuid(), amount: 50m);
        var request = new SuperAdminPrepareVendorPayoutRequestDto
        {
            Amount = 50m,
            PayoutMethodId = payoutMethodId,
            IdempotencyKey = "super-admin-payout-unexpected-exception"
        };
        var expected = new TimeoutException("database timeout");

        var canonicalPayoutService = new Mock<ICommerceHubPayoutService>(MockBehavior.Strict);
        canonicalPayoutService
            .Setup(service => service.CreatePayoutAsync(
                vendorId,
                It.IsAny<CommerceHubCreatePayoutRequestDto>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(expected);

        using var telemetry = new SuperAdminPayoutOrchestrationTelemetry();
        var orchestration = new SuperAdminPayoutOrchestrationService(
            dbContext,
            canonicalPayoutService.Object,
            telemetry,
            NullLogger<SuperAdminPayoutOrchestrationService>.Instance);

        var actual = await Assert.ThrowsAsync<SuperAdminPayoutOrchestrationException>(() =>
            orchestration.PrepareAsync(vendorId, request, Guid.NewGuid(), null));

        Assert.Same(expected, actual.InnerException);
        canonicalPayoutService.VerifyAll();
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static void SeedExecutionMapping(
        ApplicationDbContext dbContext,
        Guid vendorId,
        Guid payoutMethodId,
        Guid earningId,
        decimal amount)
    {
        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = "CH-PAYOUT-ORCH-PARENT",
            CustomerId = Guid.NewGuid(),
            FullName = "Payout Customer",
            PhoneNumber = "01000000000",
            AddressLine = "Payout Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "ONLINE",
            PaymentMethodName = "Online",
            Subtotal = amount,
            TotalAmount = amount,
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        };
        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            StoreId = vendorId,
            Currency = "EGP",
            OrderNumber = "CH-PAYOUT-ORCH",
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        };

        dbContext.CommerceHubMarketplaceOrders.Add(order);
        dbContext.CommerceHubSubOrders.Add(subOrder);
        dbContext.CommerceHubVendorPayoutMethods.Add(new CommerceHubVendorPayoutMethod
        {
            Id = payoutMethodId,
            VendorId = vendorId,
            MethodType = CommerceHubPayoutMethod.BankTransfer,
            DestinationValue = "EG-ORCH-001",
            IsDefault = true,
            IsVerified = true
        });
        dbContext.CommerceHubVendorEarnings.Add(new CommerceHubVendorEarning
        {
            Id = earningId,
            TenantId = vendorId,
            SubOrderId = subOrder.Id,
            SubOrder = subOrder,
            Amount = amount,
            Status = CommerceHubVendorEarningStatus.Available,
            AvailableAtUtc = DateTime.UtcNow.AddDays(-5),
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        });
        using var orderLifecycleBoundary = CommerceHubBoundaryMutationScope.BeginOrderLifecycle();
        using var financialWorkflowBoundary = CommerceHubBoundaryMutationScope.BeginFinancialWorkflow();
        dbContext.SaveChanges();
    }
}
