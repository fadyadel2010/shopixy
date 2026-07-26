using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Api.Controllers;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Models.Accounting.Reports;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests;

public sealed class FinancialReportsControllerTenantScopeTests
{
    [Fact]
    public async Task GetTrialBalance_UsesRequiredTenantContextForServiceScope()
    {
        var tenantId = Guid.NewGuid();
        var from = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc);

        var reportService = new Mock<IFinancialReportService>();
        reportService
            .Setup(service => service.GetTrialBalanceAsync(tenantId, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TrialBalanceReport
            {
                TenantId = tenantId,
                FromDate = from,
                ToDate = to
            });

        var controller = CreateController(reportService.Object, tenantId);

        var result = await controller.GetTrialBalance(from, to, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var report = Assert.IsType<TrialBalanceReport>(ok.Value);
        Assert.Equal(tenantId, report.TenantId);
        reportService.Verify(
            service => service.GetTrialBalanceAsync(tenantId, from, to, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetDashboard_UsesRequiredTenantContextAcrossAllFinancialSummaryCalls()
    {
        var tenantId = Guid.NewGuid();
        var from = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc);

        var reportService = new Mock<IFinancialReportService>();
        reportService
            .Setup(service => service.GetProfitAndLossAsync(tenantId, from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProfitAndLossReport
            {
                TenantId = tenantId,
                FromDate = from,
                ToDate = to
            });
        reportService
            .Setup(service => service.GetBalanceSheetAsync(tenantId, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BalanceSheetReport
            {
                TenantId = tenantId,
                AsOfDate = to
            });
        reportService
            .Setup(service => service.GetClearingAgingAsync(tenantId, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClearingAgingReport
            {
                TenantId = tenantId,
                AsOfDate = to
            });

        var controller = CreateController(reportService.Object, tenantId);

        var result = await controller.GetDashboard(from, to, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        reportService.Verify(
            service => service.GetProfitAndLossAsync(tenantId, from, to, It.IsAny<CancellationToken>()),
            Times.Once);
        reportService.Verify(
            service => service.GetBalanceSheetAsync(tenantId, to, It.IsAny<CancellationToken>()),
            Times.Once);
        reportService.Verify(
            service => service.GetClearingAgingAsync(tenantId, to, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetTrialBalance_RejectsInvertedDateRange()
    {
        var tenantId = Guid.NewGuid();
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc);
        var reportService = new Mock<IFinancialReportService>();
        var controller = CreateController(reportService.Object, tenantId);

        var result = await controller.GetTrialBalance(from, to, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        reportService.Verify(
            service => service.GetTrialBalanceAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static FinancialReportsController CreateController(
        IFinancialReportService reportService,
        Guid tenantId) =>
        new(
            reportService,
            new TestTenantContext(tenantId),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<FinancialReportsController>.Instance);
}
