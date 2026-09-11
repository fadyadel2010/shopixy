using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.CommerceHub.Wallet;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubVendorDebtServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceHubVendorDebtService _debtService;
    private readonly CommerceHubFinancialDashboardService _dashboardService;

    public CommerceHubVendorDebtServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _debtService = new CommerceHubVendorDebtService(
            _dbContext,
            Options.Create(new CommerceHubVendorDebtOptions()));
        _dashboardService = new CommerceHubFinancialDashboardService(
            _dbContext,
            new CommerceHubWalletService(_dbContext),
            _debtService);
    }

    [Fact]
    public async Task GetVendorDebtSnapshotAsync_OnlineCommission_ShouldRemainRevenueWithoutCreatingDebt()
    {
        var vendorId = Guid.NewGuid();
        await SeedLegacyOrderCommissionAsync(vendorId, CommerceHubPaymentMethod.Online, 220m);

        var snapshot = await _debtService.GetVendorDebtSnapshotAsync(vendorId);
        var dashboard = await _dashboardService.GetVendorDashboardAsync(vendorId);
        var reconciliation = await new CommerceHubFinanceReconciliationReadService(_dbContext, _debtService)
            .GetSnapshotAsync();

        Assert.Equal(0m, snapshot.AmountOwedByVendor);
        Assert.Equal(0m, dashboard.AmountOwedByVendor);
        Assert.Equal(220m, dashboard.TotalCommissions);
        Assert.Equal("ACTIVE", dashboard.DebtStatus);
        Assert.Equal(0, reconciliation.ReceivableMismatchCount);
    }

    [Fact]
    public async Task GetVendorDebtSnapshotAsync_LegacyCodCommission_ShouldCreateDebt()
    {
        var vendorId = Guid.NewGuid();
        await SeedLegacyOrderCommissionAsync(vendorId, CommerceHubPaymentMethod.CashOnDelivery, 220m);

        var snapshot = await _debtService.GetVendorDebtSnapshotAsync(vendorId);

        Assert.Equal(220m, snapshot.AmountOwedByVendor);
        Assert.Equal("AT_RISK", snapshot.Status);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private async Task SeedLegacyOrderCommissionAsync(
        Guid vendorId,
        CommerceHubPaymentMethod paymentMethod,
        decimal commission)
    {
        var order = new CommerceHubMarketplaceOrder
        {
            Id = Guid.NewGuid(),
            OrderNumber = $"CH-DEBT-{Guid.NewGuid():N}"[..20],
            FullName = "Debt Test Customer",
            PhoneNumber = "01000000000",
            AddressLine = "123 Debt Street",
            City = "Cairo",
            Currency = "EGP",
            PaymentMethodCode = paymentMethod == CommerceHubPaymentMethod.CashOnDelivery ? "COD" : "ONLINE",
            PaymentMethodName = paymentMethod == CommerceHubPaymentMethod.CashOnDelivery
                ? "Cash on Delivery"
                : "Online",
            Subtotal = 2200m,
            TotalAmount = 2200m,
            Status = OrderStatus.Delivered,
            PaymentStatus = paymentMethod == CommerceHubPaymentMethod.CashOnDelivery
                ? PaymentStatus.CashOnDelivery
                : PaymentStatus.Settled
        };

        var subOrder = new CommerceHubSubOrder
        {
            Id = Guid.NewGuid(),
            MarketplaceOrderId = order.Id,
            MarketplaceOrder = order,
            OrderNumber = $"{order.OrderNumber}-A",
            StoreId = vendorId,
            StoreName = "Debt Test Vendor",
            Currency = "EGP",
            Subtotal = 2200m,
            PaymentMethod = paymentMethod,
            PaymentMethodCode = order.PaymentMethodCode,
            PaymentMethodName = order.PaymentMethodName,
            PlatformCommissionAmount = commission,
            FinalPlatformCommissionAmount = commission,
            VendorEarningAmount = 2200m - commission,
            Total = 2200m,
            Status = OrderStatus.Delivered,
            PaymentStatus = order.PaymentStatus
        };

        order.SubOrders.Add(subOrder);
        _dbContext.Tenants.Add(new Tenant
        {
            Id = vendorId,
            Name = "Debt Test Vendor",
            IsActive = true,
            Status = TenantStatus.Active
        });
        _dbContext.CommerceHubMarketplaceOrders.Add(order);
        _dbContext.CommerceHubFinancialTransactions.AddRange(
            CommerceHubFinancialTransaction.Create(
                CommerceHubFinancialEntityType.Vendor,
                vendorId,
                CommerceHubFinancialTransactionType.OrderPayment,
                CommerceHubFinancialTransactionDirection.Credit,
                paymentMethod == CommerceHubPaymentMethod.CashOnDelivery ? 2200m : 2200m - commission,
                "EGP",
                CommerceHubFinancialReferenceType.Order,
                subOrder.Id,
                "Vendor order proceeds"),
            CommerceHubFinancialTransaction.Create(
                CommerceHubFinancialEntityType.Platform,
                null,
                CommerceHubFinancialTransactionType.Commission,
                CommerceHubFinancialTransactionDirection.Credit,
                commission,
                "EGP",
                CommerceHubFinancialReferenceType.Order,
                subOrder.Id,
                "Platform commission"));

        using (CommerceHubBoundaryMutationScope.BeginOrderLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginPayment())
        using (CommerceHubBoundaryMutationScope.BeginPaymentLifecycle())
        using (CommerceHubBoundaryMutationScope.BeginFinancialWorkflow())
        {
            await _dbContext.SaveChangesAsync();
        }
    }
}
