using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using System.Security.Claims;

namespace PointOfSale.Tests.Affiliate;

public sealed class AdminAffiliateTenantScopeTests
{
    [Fact]
    public async Task MerchantRegistry_DoesNotExposeGlobalEnrolleeWithOnlyForeignEarnings()
    {
        var tenant = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ApplicationDbContext(options, new TestTenantContext(tenant));
        var account = AffiliateAccount.Create(Guid.Empty, Guid.NewGuid(), "GLOBAL123", "Global Affiliate");
        account.Activate();
        db.AffiliateAccounts.Add(account);
        db.AffiliateCommissionLedger.Add(AffiliateCommissionLedger.CreateEarnedFromSnapshot(
            Guid.NewGuid(), Guid.NewGuid(), account.Id, Guid.NewGuid(), Guid.NewGuid(), 100m, .1m, 10m, "EGP"));
        await db.SaveChangesAsync();
        var readiness = new Mock<IAffiliateReadinessService>();
        readiness.Setup(s => s.CheckAsync(tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PointOfSale.Core.Models.Affiliate.AffiliateReadinessResult { IsReady = true });
        var controller = new AdminAffiliateController(db, null!, Mock.Of<IAffiliateService>(), Mock.Of<IAffiliatePayoutService>(),
            Mock.Of<IAffiliateSettlementOrchestrator>(), readiness.Object, Mock.Of<IEventPublisher>(), Mock.Of<ITreasuryReadService>(),
            new TestTenantContext(tenant), NullLogger<AdminAffiliateController>.Instance);
        var result = Assert.IsType<OkObjectResult>((await controller.ListAffiliates()).Result);
        Assert.Empty(Assert.IsType<AdminAffiliateListResponse>(result.Value).Affiliates);
        Assert.IsType<NotFoundObjectResult>((await controller.GetAffiliateDetail(account.Id, default)).Result);
        Assert.Single(await db.AffiliateAccounts.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task GetAffiliateDetail_UsesTrustedTenantContextInsteadOfJwtTenantClaim()
    {
        var trustedTenantId = Guid.NewGuid();
        var foreignClaimTenantId = Guid.NewGuid();
        var affiliateAccountId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(trustedTenantId)))
        {
            var affiliate = AffiliateAccount.Create(Guid.NewGuid(), Guid.NewGuid(), "AFF123", "Tenant Affiliate");
            affiliate.Id = affiliateAccountId;
            affiliate.Activate();

            context.AffiliateAccounts.Add(affiliate);
            context.AffiliateCommissionLedger.Add(
                AffiliateCommissionLedger.CreateEarnedFromSnapshot(
                    trustedTenantId,
                    Guid.NewGuid(),
                    affiliateAccountId,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    100m,
                    0.10m,
                    10m,
                    "EGP"));

            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(trustedTenantId));
        var treasuryReadService = new Mock<ITreasuryReadService>();
        treasuryReadService
            .Setup(service => service.GetDashboardAsync(
                trustedTenantId,
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TreasuryDashboardDto { CashboxBalance = 125m });

        var controller = new AdminAffiliateController(
            dbContext,
            null!,
            Mock.Of<IAffiliateService>(),
            Mock.Of<IAffiliatePayoutService>(),
            Mock.Of<IAffiliateSettlementOrchestrator>(),
            Mock.Of<IAffiliateReadinessService>(),
            Mock.Of<IEventPublisher>(),
            treasuryReadService.Object,
            new TestTenantContext(trustedTenantId),
            NullLogger<AdminAffiliateController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim("tenant_id", foreignClaimTenantId.ToString()),
                        new Claim(ClaimTypes.Role, "Admin")
                    ],
                    "TestAuth"))
                }
            }
        };

        var result = await controller.GetAffiliateDetail(affiliateAccountId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<AdminAffiliateDetailResponse>(ok.Value);
        Assert.Equal(affiliateAccountId, payload.AffiliateAccountId);
        Assert.Equal(125m, payload.CashboxBalance);
    }

    [Fact]
    public async Task SuspendAffiliate_RejectsIfNoTenantRelationship_ReturnsNotFound()
    {
        var trustedTenantId = Guid.NewGuid();
        var affiliateAccountId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(trustedTenantId)))
        {
            var affiliate = AffiliateAccount.Create(Guid.NewGuid(), Guid.NewGuid(), "AFF123", "Tenant Affiliate");
            affiliate.Id = affiliateAccountId;
            affiliate.Activate();

            context.AffiliateAccounts.Add(affiliate);
            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(trustedTenantId));
        var affiliateServiceMock = new Mock<IAffiliateService>();

        var controller = new AdminAffiliateController(
            dbContext,
            null!,
            affiliateServiceMock.Object,
            Mock.Of<IAffiliatePayoutService>(),
            Mock.Of<IAffiliateSettlementOrchestrator>(),
            Mock.Of<IAffiliateReadinessService>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<ITreasuryReadService>(),
            new TestTenantContext(trustedTenantId),
            NullLogger<AdminAffiliateController>.Instance);

        var result = await controller.SuspendAffiliate(
            affiliateAccountId,
            new AdminSuspendRequest { Reason = "Abuse" },
            CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        affiliateServiceMock.Verify(s => s.SuspendAffiliateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ActivateAffiliate_RejectsIfNoTenantRelationship_ReturnsNotFound()
    {
        var trustedTenantId = Guid.NewGuid();
        var affiliateAccountId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(trustedTenantId)))
        {
            var affiliate = AffiliateAccount.Create(Guid.NewGuid(), Guid.NewGuid(), "AFF123", "Tenant Affiliate");
            affiliate.Id = affiliateAccountId;
            affiliate.Activate();
            affiliate.Suspend("Abuse");

            context.AffiliateAccounts.Add(affiliate);
            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(trustedTenantId));
        var affiliateServiceMock = new Mock<IAffiliateService>();

        var controller = new AdminAffiliateController(
            dbContext,
            null!,
            affiliateServiceMock.Object,
            Mock.Of<IAffiliatePayoutService>(),
            Mock.Of<IAffiliateSettlementOrchestrator>(),
            Mock.Of<IAffiliateReadinessService>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<ITreasuryReadService>(),
            new TestTenantContext(trustedTenantId),
            NullLogger<AdminAffiliateController>.Instance);

        var result = await controller.ActivateAffiliate(affiliateAccountId, CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
        affiliateServiceMock.Verify(s => s.ActivateAffiliateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordPayment_RejectsIfNoTenantRelationship_ReturnsNotFound()
    {
        var trustedTenantId = Guid.NewGuid();
        var affiliateAccountId = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(trustedTenantId)))
        {
            var affiliate = AffiliateAccount.Create(Guid.NewGuid(), Guid.NewGuid(), "AFF123", "Tenant Affiliate");
            affiliate.Id = affiliateAccountId;
            affiliate.Activate();

            context.AffiliateAccounts.Add(affiliate);
            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(trustedTenantId));
        var orchestratorMock = new Mock<IAffiliateSettlementOrchestrator>();

        var controller = new AdminAffiliateController(
            dbContext,
            null!,
            Mock.Of<IAffiliateService>(),
            Mock.Of<IAffiliatePayoutService>(),
            orchestratorMock.Object,
            Mock.Of<IAffiliateReadinessService>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<ITreasuryReadService>(),
            new TestTenantContext(trustedTenantId),
            NullLogger<AdminAffiliateController>.Instance);

        var result = await controller.RecordPayment(
            affiliateAccountId,
            new AdminSettlementActionRequest { ApplyAll = true, PaymentReference = "TX123" },
            CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result.Result);
        orchestratorMock.Verify(o => o.ExecuteAffiliateBatchPayoutAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<decimal?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordPayment_ExecutesBatchPayoutAtomically()
    {
        var trustedTenantId = Guid.NewGuid();
        var affiliateAccountId = Guid.NewGuid();
        var orderId1 = Guid.NewGuid();
        var databaseName = Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        await using (var context = new ApplicationDbContext(options, new TestTenantContext(trustedTenantId)))
        {
            var affiliate = AffiliateAccount.Create(Guid.NewGuid(), Guid.NewGuid(), "AFF123", "Tenant Affiliate");
            affiliate.Id = affiliateAccountId;
            affiliate.Activate();

            context.AffiliateAccounts.Add(affiliate);

            var earned = AffiliateCommissionLedger.CreateEarnedFromSnapshot(
                trustedTenantId,
                Guid.NewGuid(),
                affiliateAccountId,
                orderId1,
                Guid.NewGuid(),
                100m,
                0.10m,
                10m,
                "USD");
            context.AffiliateCommissionLedger.Add(earned);

            var approved = AffiliateCommissionLedger.CreateApproved(earned);
            context.AffiliateCommissionLedger.Add(approved);

            await context.SaveChangesAsync();
        }

        await using var dbContext = new ApplicationDbContext(options, new TestTenantContext(trustedTenantId));
        var orchestratorMock = new Mock<IAffiliateSettlementOrchestrator>();
        orchestratorMock.Setup(o => o.ExecuteAffiliateBatchPayoutAsync(
            affiliateAccountId,
            trustedTenantId,
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(orderId1)),
            It.IsAny<decimal>(),
            "TX123",
            null,
            "Notes",
            It.IsAny<decimal?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PayoutExecutionResult
            {
                PaidOrders = new List<PayoutOrderInfo> { new PayoutOrderInfo(orderId1, 10m, 1, "USD") }
            });

        var treasuryMock = new Mock<ITreasuryReadService>();
        treasuryMock.Setup(t => t.GetDashboardAsync(trustedTenantId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TreasuryDashboardDto { CashboxBalance = 1000m });

        var controller = new AdminAffiliateController(
            dbContext,
            null!,
            Mock.Of<IAffiliateService>(),
            Mock.Of<IAffiliatePayoutService>(),
            orchestratorMock.Object,
            Mock.Of<IAffiliateReadinessService>(),
            Mock.Of<IEventPublisher>(),
            treasuryMock.Object,
            new TestTenantContext(trustedTenantId),
            NullLogger<AdminAffiliateController>.Instance);

        var result = await controller.RecordPayment(
            affiliateAccountId,
            new AdminSettlementActionRequest
            {
                ApplyAll = false,
                OrderIds = new List<Guid> { orderId1 },
                PaymentReference = "TX123",
                AdminNotes = "Notes"
            },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AdminSettlementActionResponse>(ok.Value);
        Assert.Equal(1, response.ProcessedCount);
        Assert.Equal(0, response.FailedCount);
    }
}
