using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.SuperAdmin.Controllers;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.API.SuperAdmin.Infrastructure;
using PointOfSale.API.SuperAdmin.Services;

namespace PointOfSale.Tests.SuperAdmin;

public class SuperAdminFinanceControllerTests
{
    [Fact]
    public async Task GetOverview_Returns_Standardized_FinancialDashboard_Response()
    {
        var financeService = CreateStubService();
        var dashboardService = CreateDashboardService();
        var auditService = new RecordingSuperAdminAuditService();
        var controller = new SuperAdminFinanceController(
            financeService,
            dashboardService,
            auditService,
            NullLogger<SuperAdminFinanceController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext()
            }
        };

        var actionResult = await controller.GetOverview(new SuperAdminFinancialDashboardFilterDto(), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var payload = Assert.IsType<SuperAdminSuccessResponse<SuperAdminFinancialOverviewDto>>(okResult.Value);

        Assert.True(payload.Success);
        Assert.NotNull(payload.Data);
        Assert.Equal(28m, payload.Data!.TotalGrossCommission);
        Assert.Equal(6m, payload.Data.TotalPromoCost);
        Assert.Equal(22m, payload.Data.TotalNetCommission);
        Assert.Equal(11m, payload.Data.TotalRefundAmount);
        Assert.Equal(4m, payload.Data.RefundImpactOnRevenue);
        Assert.Equal(18m, payload.Data.NetRevenue);
        Assert.Equal(4, payload.Data.TotalOrdersCount);
        Assert.Equal(45m, payload.Data.AverageOrderValue);
        Assert.Equal(1, payload.Data.RefundedOrdersCount);
        Assert.Equal(0.25m, payload.Data.RefundRate);
        Assert.Equal("SubOrder", payload.Data.OrderScope);
        Assert.NotNull(payload.Meta);
        Assert.Equal("req-finance-123", payload.Meta!["requestId"]);
        Assert.Contains("FINANCIAL_DASHBOARD_OVERVIEW_VIEWED", auditService.Actions);
    }

    [Fact]
    public async Task GetLedgerOverview_Returns_Standardized_FinanceOverview_Response()
    {
        var financeService = CreateStubService();
        var dashboardService = CreateDashboardService();
        var auditService = new RecordingSuperAdminAuditService();
        var controller = new SuperAdminFinanceController(
            financeService,
            dashboardService,
            auditService,
            NullLogger<SuperAdminFinanceController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext()
            }
        };

        var actionResult = await controller.GetLedgerOverview(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var payload = Assert.IsType<SuperAdminSuccessResponse<SuperAdminFinanceOverviewDto>>(okResult.Value);

        Assert.True(payload.Success);
        Assert.NotNull(payload.Data);
        Assert.Equal(118m, payload.Data!.PlatformBalance);
        Assert.Equal(25m, payload.Data.TotalCommissions);
        Assert.Equal(3m, payload.Data.CommissionReversals);
        Assert.Equal(80m, payload.Data.TotalPayouts);
        Assert.Equal(40m, payload.Data.TotalRefunds);
        Assert.Equal(22m, payload.Data.NetProfit);
        Assert.Equal("EGP", payload.Data.Currency);
        Assert.Equal(14, payload.Data.TransactionsCount);
        Assert.Single(payload.Data.Breakdown);
        Assert.Single(payload.Data.RecentTransactions);
        Assert.NotNull(payload.Meta);
        Assert.Equal("req-finance-123", payload.Meta!["requestId"]);
        Assert.Single(auditService.Actions);
        Assert.Equal("FINANCE_LEDGER_OVERVIEW_VIEWED", auditService.Actions[0]);
    }

    [Fact]
    public async Task GetCashflow_Returns_Standardized_Cashflow_Response()
    {
        var financeService = CreateStubService();
        var dashboardService = CreateDashboardService();
        var auditService = new RecordingSuperAdminAuditService();
        var controller = new SuperAdminFinanceController(
            financeService,
            dashboardService,
            auditService,
            NullLogger<SuperAdminFinanceController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext()
            }
        };

        var actionResult = await controller.GetCashflow(new SuperAdminFinanceReportQueryDto(), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var payload = Assert.IsType<SuperAdminSuccessResponse<SuperAdminFinanceCashflowDto>>(okResult.Value);

        Assert.True(payload.Success);
        Assert.NotNull(payload.Data);
        Assert.Equal(160m, payload.Data!.CashInTotal);
        Assert.Equal(55m, payload.Data.CashOutTotal);
        Assert.Equal(105m, payload.Data.NetCashFlow);
        Assert.Equal("EGP", payload.Data.Currency);
        Assert.Contains("FINANCE_CASHFLOW_VIEWED", auditService.Actions);
    }

    [Fact]
    public async Task GetPromos_Returns_Paged_PromoAnalytics_Response()
    {
        var financeService = CreateStubService();
        var dashboardService = CreateDashboardService();
        var auditService = new RecordingSuperAdminAuditService();
        var controller = new SuperAdminFinanceController(
            financeService,
            dashboardService,
            auditService,
            NullLogger<SuperAdminFinanceController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext()
            }
        };

        var actionResult = await controller.GetPromos(new SuperAdminPromoAnalyticsQueryDto
        {
            Page = 2,
            PageSize = 5,
            SortBy = SuperAdminPromoAnalyticsSortField.NetImpact,
            SortDirection = SuperAdminSortDirection.Desc
        }, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var payload = Assert.IsType<SuperAdminSuccessResponse<SuperAdminPromoAnalyticsResponseDto>>(okResult.Value);

        Assert.True(payload.Success);
        var promo = Assert.Single(payload.Data!.Items);
        Assert.Equal("SAVE10", promo.PromoCode);
        Assert.Equal(9.5m, promo.NetImpact);
        Assert.NotNull(payload.Meta);
        Assert.Equal(2, payload.Meta!["page"]);
        Assert.Equal(5, payload.Meta["pageSize"]);
        Assert.Equal(6, payload.Meta["totalCount"]);
        Assert.Equal(2, payload.Meta["totalPages"]);
        Assert.Equal("NetImpact", payload.Meta["sortBy"]);
        Assert.Equal("Desc", payload.Meta["sortDirection"]);
        Assert.Contains("FINANCIAL_DASHBOARD_PROMOS_VIEWED", auditService.Actions);
    }

    [Fact]
    public async Task GetProfitAndLoss_Returns_Standardized_Pnl_Response()
    {
        var financeService = CreateStubService();
        var dashboardService = CreateDashboardService();
        var auditService = new RecordingSuperAdminAuditService();
        var controller = new SuperAdminFinanceController(
            financeService,
            dashboardService,
            auditService,
            NullLogger<SuperAdminFinanceController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext()
            }
        };

        var actionResult = await controller.GetProfitAndLoss(new SuperAdminFinanceReportQueryDto(), CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var payload = Assert.IsType<SuperAdminSuccessResponse<SuperAdminFinanceProfitAndLossDto>>(okResult.Value);

        Assert.True(payload.Success);
        Assert.NotNull(payload.Data);
        Assert.Equal(25m, payload.Data!.Revenue);
        Assert.Equal(3m, payload.Data.RefundLosses);
        Assert.Equal(22m, payload.Data.NetProfit);
        Assert.Contains("FINANCE_PNL_VIEWED", auditService.Actions);
    }

    [Fact]
    public async Task GetReconciliation_Returns_Standardized_Reconciliation_Response()
    {
        var financeService = CreateStubService();
        var dashboardService = CreateDashboardService();
        var auditService = new RecordingSuperAdminAuditService();
        var controller = new SuperAdminFinanceController(
            financeService,
            dashboardService,
            auditService,
            NullLogger<SuperAdminFinanceController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext()
            }
        };

        var actionResult = await controller.GetReconciliation(CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var payload = Assert.IsType<SuperAdminSuccessResponse<SuperAdminFinanceReconciliationDto>>(okResult.Value);

        Assert.True(payload.Success);
        Assert.NotNull(payload.Data);
        Assert.Equal("OK", payload.Data!.Status);
        Assert.Equal(0, payload.Data.CorrelationIntegrityMismatchCount);
        Assert.Equal(0, payload.Data.PayableMismatchCount);
        Assert.Equal(0, payload.Data.ReceivableMismatchCount);
        Assert.Contains("FINANCE_RECONCILIATION_VIEWED", auditService.Actions);
    }

    [Fact]
    public async Task GetIntegrityCheck_Returns_Standardized_Integrity_Response()
    {
        var financeService = CreateStubService();
        var dashboardService = CreateDashboardService();
        var auditService = new RecordingSuperAdminAuditService();
        var controller = new SuperAdminFinanceController(
            financeService,
            dashboardService,
            auditService,
            NullLogger<SuperAdminFinanceController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = BuildHttpContext()
            }
        };

        var actionResult = await controller.GetIntegrityCheck(new SuperAdminFinancialIntegrityCheckQueryDto
        {
            MaxIssues = 10
        }, CancellationToken.None);

        var okResult = Assert.IsType<OkObjectResult>(actionResult);
        var payload = Assert.IsType<SuperAdminSuccessResponse<SuperAdminFinancialIntegrityCheckDto>>(okResult.Value);

        Assert.True(payload.Success);
        Assert.NotNull(payload.Data);
        Assert.Equal("ISSUES_DETECTED", payload.Data!.Status);
        Assert.False(payload.Data.SupportsGlobalDebitCreditParity);
        Assert.Equal("NOT_APPLICABLE", payload.Data.DebitCreditParityStatus);
        Assert.Equal(3, payload.Data.TotalIssueCount);
        Assert.Equal(2, payload.Data.OrderFlowMismatchCount);
        Assert.Single(payload.Data.Issues);
        Assert.Contains("FINANCIAL_DASHBOARD_INTEGRITY_VIEWED", auditService.Actions);
    }

    private static DefaultHttpContext BuildHttpContext()
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Email, "root@commercehub.test")
            },
            authenticationType: "SuperAdminJwt");

        return new DefaultHttpContext
        {
            TraceIdentifier = "req-finance-123",
            User = new ClaimsPrincipal(identity)
        };
    }

    private static StubSuperAdminFinanceOverviewService CreateStubService()
    {
        return new StubSuperAdminFinanceOverviewService(
            new SuperAdminFinanceOverviewDto
            {
                PlatformBalance = 118m,
                TotalCommissions = 25m,
                CommissionReversals = 3m,
                TotalPayouts = 80m,
                TotalRefunds = 40m,
                NetProfit = 22m,
                Currency = "EGP",
                TransactionsCount = 14,
                LastTransactionAtUtc = new DateTime(2026, 3, 24, 12, 30, 0, DateTimeKind.Utc),
                Breakdown = new List<CommerceHubFinanceBreakdownItemDto>
                {
                    new()
                    {
                        TransactionType = "Commission",
                        TotalCredits = 25m,
                        TotalDebits = 3m,
                        NetAmount = 22m,
                        Count = 4
                    }
                },
                RecentTransactions = new List<CommerceHubFinanceActivityDto>
                {
                    new()
                    {
                        TransactionId = Guid.NewGuid(),
                        EntityType = "Platform",
                        TransactionType = "Commission",
                        Direction = "Credit",
                        Amount = 10m,
                        SignedAmount = 10m,
                        Currency = "EGP",
                        ReferenceType = "Order",
                        ReferenceId = Guid.NewGuid(),
                        Description = "Commission captured",
                        CreatedAtUtc = new DateTime(2026, 3, 24, 12, 0, 0, DateTimeKind.Utc)
                    }
                }
            },
            new SuperAdminFinanceCashflowDto
            {
                OnlinePaymentsIn = 150m,
                VendorSettlementsIn = 10m,
                CashInTotal = 160m,
                RefundsOut = 15m,
                VendorPayoutsOut = 40m,
                CashOutTotal = 55m,
                NetCashFlow = 105m,
                Currency = "EGP",
                TransactionsCount = 4
            },
            new SuperAdminFinanceProfitAndLossDto
            {
                Revenue = 25m,
                RefundLosses = 3m,
                NetProfit = 22m,
                Currency = "EGP",
                TransactionsCount = 2
            },
            new SuperAdminFinanceReconciliationDto
            {
                Status = "OK",
                TotalTransactions = 10,
                CorrelationIntegrityMismatchCount = 0,
                PayableMismatchCount = 0,
                ReceivableMismatchCount = 0,
                MissingPairCount = 0,
                DuplicateEntryCount = 0,
                ExecutedAtUtc = new DateTime(2026, 3, 24, 13, 0, 0, DateTimeKind.Utc)
            });
    }

    private static StubSuperAdminFinancialDashboardService CreateDashboardService()
    {
        return new StubSuperAdminFinancialDashboardService(
            new SuperAdminFinancialOverviewDto
            {
                FromDateUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                ToDateUtc = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc),
                Currency = "EGP",
                TotalGrossCommission = 28m,
                TotalPromoCost = 6m,
                TotalNetCommission = 22m,
                TotalRefundAmount = 11m,
                RefundImpactOnRevenue = 4m,
                NetRevenue = 18m,
                TotalOrdersCount = 4,
                AverageOrderValue = 45m,
                RefundedOrdersCount = 1,
                RefundRate = 0.25m,
                OrderScope = "SubOrder"
            });
    }

    private sealed class StubSuperAdminFinanceOverviewService : ISuperAdminFinanceOverviewService
    {
        private readonly SuperAdminFinanceOverviewDto _overview;
        private readonly SuperAdminFinanceCashflowDto _cashflow;
        private readonly SuperAdminFinanceProfitAndLossDto _profitAndLoss;
        private readonly SuperAdminFinanceReconciliationDto _reconciliation;

        public StubSuperAdminFinanceOverviewService(
            SuperAdminFinanceOverviewDto overview,
            SuperAdminFinanceCashflowDto cashflow,
            SuperAdminFinanceProfitAndLossDto profitAndLoss,
            SuperAdminFinanceReconciliationDto reconciliation)
        {
            _overview = overview;
            _cashflow = cashflow;
            _profitAndLoss = profitAndLoss;
            _reconciliation = reconciliation;
        }

        public Task<SuperAdminFinanceOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_overview);
        }

        public Task<SuperAdminFinanceCashflowDto> GetCashflowAsync(
            SuperAdminFinanceReportQueryDto? query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_cashflow);
        }

        public Task<SuperAdminFinanceProfitAndLossDto> GetProfitAndLossAsync(
            SuperAdminFinanceReportQueryDto? query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_profitAndLoss);
        }

        public Task<SuperAdminFinanceReconciliationDto> GetReconciliationAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_reconciliation);
        }
    }

    private sealed class StubSuperAdminFinancialDashboardService : ISuperAdminFinancialDashboardService
    {
        private readonly SuperAdminFinancialOverviewDto _overview;

        public StubSuperAdminFinancialDashboardService(SuperAdminFinancialOverviewDto overview)
        {
            _overview = overview;
        }

        public Task<SuperAdminFinancialOverviewDto> GetOverviewAsync(
            SuperAdminFinancialDashboardFilterDto? query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_overview);
        }

        public Task<SuperAdminFinancialTrendsDto> GetTrendsAsync(
            SuperAdminFinancialTrendsQueryDto? query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SuperAdminFinancialTrendsDto
            {
                FromDateUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                ToDateUtc = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc),
                Currency = "EGP",
                GroupBy = (query?.GroupBy ?? SuperAdminFinancialTrendGroupBy.Day).ToString(),
                Points = new List<SuperAdminFinancialTrendPointDto>
                {
                    new()
                    {
                        BucketStartUtc = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc),
                        GrossCommission = 12m,
                        PromoCost = 2m,
                        NetCommission = 10m,
                        CommissionReversals = 1m,
                        NetRevenue = 9m,
                        RefundAmount = 4m,
                        OrdersCount = 2
                    }
                }
            });
        }

        public Task<SuperAdminPagedResult<SuperAdminPromoAnalyticsItemDto>> GetPromoAnalyticsAsync(
            SuperAdminPromoAnalyticsQueryDto? query,
            CancellationToken cancellationToken = default)
        {
            var page = query?.Page ?? 1;
            var pageSize = query?.PageSize ?? 20;

            return Task.FromResult(new SuperAdminPagedResult<SuperAdminPromoAnalyticsItemDto>
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = 6,
                TotalPages = 2,
                Items = new List<SuperAdminPromoAnalyticsItemDto>
                {
                    new()
                    {
                        PromoCodeId = Guid.NewGuid(),
                        PromoCode = "SAVE10",
                        IsActive = true,
                        TotalUsageCount = 3,
                        TotalPromoCost = 4.5m,
                        RevenueGenerated = 15m,
                        CommissionReversals = 1m,
                        NetImpact = 9.5m,
                        AverageDiscountPerUse = 1.5m,
                        LastUsedAtUtc = new DateTime(2026, 3, 26, 10, 0, 0, DateTimeKind.Utc),
                        Currency = "EGP"
                    }
                }
            });
        }

        public Task<SuperAdminPagedResult<SuperAdminVendorPerformanceItemDto>> GetVendorPerformanceAsync(
            SuperAdminVendorPerformanceQueryDto? query,
            CancellationToken cancellationToken = default)
        {
            var page = query?.Page ?? 1;
            var pageSize = query?.PageSize ?? 20;

            return Task.FromResult(new SuperAdminPagedResult<SuperAdminVendorPerformanceItemDto>
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = 1,
                TotalPages = 1,
                Items = new List<SuperAdminVendorPerformanceItemDto>
                {
                    new()
                    {
                        VendorId = Guid.NewGuid(),
                        VendorName = "Vendor Alpha",
                        OrdersCount = 4,
                        TotalSales = 220m,
                        GrossCommission = 22m,
                        PromoCost = 5m,
                        CommissionReversals = 2m,
                        TotalRefundAmount = 10m,
                        RefundedOrdersCount = 1,
                        RefundRate = 0.25m,
                        NetContribution = 15m,
                        Currency = "EGP",
                        LastOrderAtUtc = new DateTime(2026, 3, 25, 11, 0, 0, DateTimeKind.Utc)
                    }
                }
            });
        }

        public Task<SuperAdminRefundAnalysisDto> GetRefundAnalysisAsync(
            SuperAdminRefundAnalysisQueryDto? query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SuperAdminRefundAnalysisDto
            {
                FromDateUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                ToDateUtc = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc),
                Currency = "EGP",
                GroupBy = (query?.GroupBy ?? SuperAdminFinancialTrendGroupBy.Day).ToString(),
                TotalRefundAmount = 11m,
                TotalRefundCount = 2,
                RefundedOrdersCount = 1,
                RefundRate = 0.25m,
                PromoReversalAmount = 1m,
                Points = new List<SuperAdminRefundTrendPointDto>
                {
                    new()
                    {
                        BucketStartUtc = new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc),
                        RefundAmount = 11m,
                        RefundCount = 2,
                        PromoReversalAmount = 1m
                    }
                }
            });
        }

        public Task<SuperAdminFinancialIntegrityCheckDto> GetIntegrityCheckAsync(
            SuperAdminFinancialIntegrityCheckQueryDto? query,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SuperAdminFinancialIntegrityCheckDto
            {
                Status = "ISSUES_DETECTED",
                ExecutedAtUtc = new DateTime(2026, 3, 27, 12, 0, 0, DateTimeKind.Utc),
                ScopeDescription = "Scoped integrity scan for promo 'SAVE10'.",
                SupportsGlobalDebitCreditParity = false,
                DebitCreditParityStatus = "NOT_APPLICABLE",
                DebitCreditParityNote = "Operational bucket ledger parity is evaluated per flow, not as a double-entry balance equation.",
                TotalIssueCount = 3,
                OrderFlowMismatchCount = 2,
                RefundFlowMismatchCount = 1,
                Issues = new List<SuperAdminFinancialIntegrityIssueDto>
                {
                    new()
                    {
                        Severity = "ERROR",
                        IssueType = "ORDER_LEDGER_MISMATCH",
                        ReferenceType = "Order",
                        ReferenceId = Guid.NewGuid(),
                        Summary = "Gross commission does not reconcile to the platform ledger.",
                        ExpectedAmount = 10m,
                        ActualAmount = 9m,
                        Currency = "EGP",
                        OccurredAtUtc = new DateTime(2026, 3, 26, 8, 0, 0, DateTimeKind.Utc)
                    }
                }
            });
        }
    }

    private sealed class RecordingSuperAdminAuditService : ISuperAdminAuditService
    {
        public List<string> Actions { get; } = new();

        public Task LogActionAsync(
            string action,
            string entityType,
            string entityId,
            string description,
            bool success,
            object? details = null,
            string? reference = null,
            Guid? actorId = null,
            string? actorEmail = null,
            CancellationToken cancellationToken = default)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }
}
