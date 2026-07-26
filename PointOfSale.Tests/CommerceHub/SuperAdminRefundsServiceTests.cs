using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using StatusCodes = Microsoft.AspNetCore.Http.StatusCodes;

namespace PointOfSale.Tests.CommerceHub;

public sealed class SuperAdminRefundsServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<IRefundExecutionService> _refundExecutionService = new();
    private readonly Mock<IRefundEvidenceService> _refundEvidenceService = new();
    private readonly Mock<ISuperAdminAuditService> _auditService = new();
    private readonly IReturnManagementService _returnManagementService;
    private readonly ICommerceHubVendorDebtService _vendorDebtService;

    public SuperAdminRefundsServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _returnManagementService = new ReturnManagementService(
            _dbContext,
            Mock.Of<IInventoryConsistencyService>());
        _vendorDebtService = new CommerceHubVendorDebtService(
            _dbContext,
            Options.Create(new CommerceHubVendorDebtOptions()));
        _refundEvidenceService
            .Setup(service => service.GetEvidenceLookupAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, IReadOnlyList<RefundEvidenceMediaDto>>());
    }

    [Fact]
    public async Task ApproveAsync_ShouldMarkRefundApproved_WithoutExecuting()
    {
        var graph = await SeedRefundRequestAsync(CommerceHubRefundRequestStatus.Requested);
        var actorId = Guid.NewGuid();
        var service = CreateService();

        var result = await service.ApproveAsync(
            graph.RefundRequest.Id,
            new SuperAdminApproveRefundRequest { Note = "Support verified the defect." },
            actorId,
            "owner@commercehub.local",
            "corr-approve");

        var persisted = await _dbContext.CommerceHubRefundRequests.SingleAsync();
        var createdReturn = await _dbContext.CommerceHubReturnRequests.SingleAsync();

        Assert.Equal(CommerceHubRefundRequestStatus.WaitingForReturn, persisted.Status);
        Assert.Equal(actorId, persisted.ReviewedBy);
        Assert.NotNull(persisted.ReviewedAtUtc);
        Assert.NotNull(persisted.ApprovedAtUtc);
        Assert.Equal("Support verified the defect.", persisted.AdminNote);
        Assert.Equal(createdReturn.Id, persisted.ReturnRequestId);
        Assert.Equal(RefundExecutionTrigger.OnPickup, persisted.RefundExecutionTrigger);
        Assert.Equal(CommerceHubRefundRequestStatus.WaitingForReturn, result.Status);
        Assert.False(result.CanExecute);
        Assert.NotNull(result.ReturnRequest);
        Assert.Equal(CommerceHubReturnStatus.Pending, result.ReturnRequest!.Status);

        _refundExecutionService.Verify(
            service => service.ExecuteAsync(
                It.IsAny<Guid>(),
                It.IsAny<SuperAdminExecuteRefundRequest>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _auditService.Verify(
            service => service.LogActionAsync(
                "REFUND_APPROVED",
                "CommerceHubRefundRequest",
                graph.RefundRequest.Id.ToString(),
                It.IsAny<string>(),
                true,
                It.IsAny<object?>(),
                It.IsAny<string?>(),
                actorId,
                "owner@commercehub.local",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RetryAsync_ShouldReopenFailedRefund_ForManualExecution()
    {
        var graph = await SeedRefundRequestAsync(CommerceHubRefundRequestStatus.Failed);
        graph.RefundRequest.FailureReason = "Transfer reference was rejected by operations.";
        graph.RefundRequest.FailedAtUtc = DateTime.UtcNow.AddMinutes(-10);
        await SaveCommerceHubFixtureAsync();

        var actorId = Guid.NewGuid();
        var service = CreateService();

        var result = await service.RetryAsync(
            graph.RefundRequest.Id,
            new SuperAdminRetryRefundRequest { Note = "Operations is resending with a corrected reference." },
            actorId,
            "owner@commercehub.local");

        var persisted = await _dbContext.CommerceHubRefundRequests.SingleAsync();
        var createdReturn = await _dbContext.CommerceHubReturnRequests.SingleAsync();

        Assert.Equal(CommerceHubRefundRequestStatus.WaitingForReturn, persisted.Status);
        Assert.Null(persisted.FailureReason);
        Assert.Null(persisted.FailedAtUtc);
        Assert.Equal("Operations is resending with a corrected reference.", persisted.AdminNote);
        Assert.Equal(CommerceHubRefundRequestStatus.WaitingForReturn, result.Status);
        Assert.Equal(createdReturn.Id, persisted.ReturnRequestId);
        Assert.False(result.CanExecute);
        Assert.NotNull(result.ReturnRequest);
        Assert.Equal(CommerceHubReturnStatus.Pending, result.ReturnRequest!.Status);

        _refundExecutionService.Verify(
            service => service.ExecuteAsync(
                It.IsAny<Guid>(),
                It.IsAny<SuperAdminExecuteRefundRequest>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        _auditService.Verify(
            service => service.LogActionAsync(
                "REFUND_RETRY_PREPARED",
                "CommerceHubRefundRequest",
                graph.RefundRequest.Id.ToString(),
                It.IsAny<string>(),
                true,
                It.IsAny<object?>(),
                It.IsAny<string?>(),
                actorId,
                "owner@commercehub.local",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldDelegateToExecutionService_AndReturnUpdatedDetails()
    {
        var graph = await SeedRefundRequestAsync(CommerceHubRefundRequestStatus.Approved);
        var actorId = Guid.NewGuid();
        var service = CreateService();

        _refundExecutionService
            .Setup(execution => execution.ExecuteAsync(
                graph.RefundRequest.Id,
                It.IsAny<SuperAdminExecuteRefundRequest>(),
                actorId,
                "owner@commercehub.local",
                "corr-execute",
                It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                var refundRequest = await _dbContext.CommerceHubRefundRequests.SingleAsync();
                refundRequest.Status = CommerceHubRefundRequestStatus.Completed;
                refundRequest.CompletedAtUtc = DateTime.UtcNow;
                refundRequest.FailureReason = null;

                _dbContext.CommerceHubRefundExecutions.Add(new CommerceHubRefundExecution
                {
                    Id = Guid.NewGuid(),
                    RefundId = refundRequest.Id,
                    Method = RefundPayoutMethod.VodafoneCash,
                    AccountSnapshot = refundRequest.PayoutAccountSnapshot!,
                    TransactionReference = "TXN123456",
                    Amount = refundRequest.TotalAmount,
                    Currency = refundRequest.Order.Currency,
                    ExecutedBy = actorId,
                    ExecutedAt = DateTime.UtcNow,
                    Notes = "Sent via Vodafone Cash",
                    IsSuccessful = true,
                    CreatedBy = actorId
                });

                _dbContext.CommerceHubRefunds.Add(new CommerceHubRefund
                {
                    Id = Guid.NewGuid(),
                    TenantId = refundRequest.VendorId,
                    SubOrderId = graph.SubOrder.Id,
                    RefundRequestId = refundRequest.Id,
                    OrderItemId = graph.Item.Id,
                    Quantity = refundRequest.Quantity,
                    Amount = refundRequest.TotalAmount,
                    CommissionReversalAmount = 10m,
                    VendorEarningReversalAmount = 190m,
                    Currency = refundRequest.Order.Currency,
                    Status = CommerceHubRefundStatus.Completed,
                    Reason = refundRequest.Reason,
                    Reference = "TXN123456",
                    GatewayStatus = "manual-confirmed",
                    IdempotencyKey = "refund-exec-test",
                    IsFullRefund = false,
                    IsPostPayoutAdjustment = false,
                    ProcessingStartedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                    ProcessedAtUtc = DateTime.UtcNow,
                    CreatedBy = actorId
                });

                await SaveCommerceHubFixtureAsync();
            });

        var result = await service.ExecuteAsync(
            graph.RefundRequest.Id,
            new SuperAdminExecuteRefundRequest
            {
                Method = RefundPayoutMethod.VodafoneCash,
                TransactionReference = "TXN123456",
                Notes = "Sent via Vodafone Cash"
            },
            actorId,
            "owner@commercehub.local",
            "corr-execute");

        Assert.Equal(CommerceHubRefundRequestStatus.Paid, result.Status);
        Assert.NotNull(result.Execution);
        Assert.Equal("TXN123456", result.Execution!.TransactionReference);
        Assert.True(result.Execution.IsSuccessful);
        Assert.Equal("010****678", result.Execution.MaskedAccountIdentifier);

        _refundExecutionService.Verify(
            service => service.ExecuteAsync(
                graph.RefundRequest.Id,
                It.Is<SuperAdminExecuteRefundRequest>(request =>
                    request.Method == RefundPayoutMethod.VodafoneCash &&
                    request.TransactionReference == "TXN123456" &&
                    request.Notes == "Sent via Vodafone Cash"),
                actorId,
                "owner@commercehub.local",
                "corr-execute",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ApproveAsync_ShouldReject_WhenRefundHasFailed()
    {
        var graph = await SeedRefundRequestAsync(CommerceHubRefundRequestStatus.Failed);
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            service.ApproveAsync(
                graph.RefundRequest.Id,
                new SuperAdminApproveRefundRequest { Note = "Should not approve from failed." },
                Guid.NewGuid(),
                "owner@commercehub.local",
                "corr-invalid-approve"));

        Assert.Equal("REFUND_APPROVAL_NOT_ALLOWED", exception.Code);
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
    }

    [Fact]
    public async Task RetryAsync_ShouldReject_WhenRefundIsNotFailed()
    {
        var graph = await SeedRefundRequestAsync(CommerceHubRefundRequestStatus.Requested);
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<CommerceHubCheckoutException>(() =>
            service.RetryAsync(
                graph.RefundRequest.Id,
                new SuperAdminRetryRefundRequest { Note = "Should fail" },
                Guid.NewGuid(),
                "owner@commercehub.local"));

        Assert.Equal("REFUND_RETRY_NOT_ALLOWED", exception.Code);
        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private SuperAdminRefundsService CreateService()
    {
        return new SuperAdminRefundsService(
            _dbContext,
            _returnManagementService,
            _refundExecutionService.Object,
            _refundEvidenceService.Object,
            _auditService.Object,
            _vendorDebtService);
    }

    private async Task<RefundGraph> SeedRefundRequestAsync(CommerceHubRefundRequestStatus status)
    {
        var customer = CommerceHubCustomer.Create("refund-admin@example.com", "Refund Admin Test", "01011111111", "hash");

        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            Customer = customer,
            OrderNumber = $"ORD-{Guid.NewGuid():N}"[..12],
            FullName = "Refund Admin Test",
            PhoneNumber = "01011111111",
            AddressLine = "123 Admin Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = "CARD",
            PaymentMethodName = "Card",
            Subtotal = 400m,
            ShippingTotal = 20m,
            TotalAmount = 420m,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Settled
        };

        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = $"SUB-{Guid.NewGuid():N}"[..12],
            StoreId = Guid.NewGuid(),
            StoreName = "Vendor Refund Control",
            Currency = "EGP",
            Subtotal = 400m,
            Shipping = 20m,
            PaymentMethod = CommerceHubPaymentMethod.CashOnDelivery,
            PaymentMethodCode = "COD",
            PaymentMethodName = "Cash on delivery",
            Total = 420m,
            Status = OrderStatus.Delivered,
            PaymentStatus = PaymentStatus.Collected
        };

        var item = new CommerceHubSubOrderItem
        {
            Id = Guid.NewGuid(),
            SubOrderId = subOrder.Id,
            SubOrder = subOrder,
            ListingId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ProductName = "Refund Control Product",
            VariantName = "Black / Medium",
            Quantity = 2,
            UnitPrice = 200m,
            PlatformCommissionAmount = 20m,
            Total = 400m,
            Status = CommerceHubOrderItemStatus.Delivered,
            DeliveredAt = DateTime.UtcNow.AddDays(-1)
        };

        var refundRequest = new CommerceHubRefundRequest
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Order = order,
            OrderItemId = item.Id,
            OrderItem = item,
            CustomerId = customer.Id,
            Customer = customer,
            VendorId = subOrder.StoreId,
            Quantity = 1,
            UnitPriceSnapshot = 200m,
            TotalAmount = 200m,
            Reason = "Damaged package",
            ReasonCode = RefundReasonCode.DamagedItem,
            Status = status,
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            IdempotencyKey = $"refund-request-{Guid.NewGuid():N}",
            PayoutMethodSnapshot = RefundPayoutMethod.VodafoneCash,
            PayoutAccountSnapshot = "ENC:test-account",
            PayoutAccountMaskedSnapshot = "010****678",
            PayoutAccountHolderNameSnapshot = "Refund Admin Test"
        };

        order.SubOrders.Add(subOrder);
        subOrder.Items.Add(item);

        _dbContext.CommerceHubCustomers.Add(customer);
        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubRefundRequests.Add(refundRequest);
        await SaveCommerceHubFixtureAsync();

        return new RefundGraph(customer, order, subOrder, item, refundRequest);
    }

    private async Task SaveCommerceHubFixtureAsync()
    {
        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        using (CommerceHubBoundaryMutationScope.BeginRefund())
        {
            await _dbContext.SaveChangesAsync();
        }
    }

    private sealed record RefundGraph(
        CommerceHubCustomer Customer,
        CommerceHubMarketplaceOrder Order,
        CommerceHubSubOrder SubOrder,
        CommerceHubSubOrderItem Item,
        CommerceHubRefundRequest RefundRequest);
}
