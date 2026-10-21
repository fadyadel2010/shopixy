using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.EventBus;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Services.Affiliate;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Affiliate;

/// <summary>Real SQL locks, uniqueness, rollback and bus outbox; disposable test database only.
/// The bus is never started and no provider, payout or configured development record is used.</summary>
public sealed class AffiliatePhase4PostgresTests
{
    [Fact]
    public async Task ConcurrentRecognition_AtomicOutboxFailureRetry_AndCumulativeRefunds()
    {
        await using var database = TestDatabaseScope.Open(); // Missing SQL setup fails explicitly, never silently passes.
        await using var seed = database.CreateDbContext();
        var tenant = Guid.NewGuid();
        seed.Tenants.Add(new Tenant { Id = tenant, Name = "Phase4 isolated", Slug = $"phase4-{tenant:N}", Currency = "EGP", IsActive = true, Status = TenantStatus.Active });
        var category = new Category { Id = Guid.NewGuid(), TenantId = tenant, Name = "Phase4" };
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenant, Name = "Phase4", CategoryId = category.Id };
        var account = AffiliateAccount.Create(Guid.Empty, Guid.NewGuid(), "PHASE4PG", "Isolated Phase4");
        account.Id = Guid.NewGuid(); account.Activate();
        var user = AffiliateUser.Create(Guid.Empty, "phase4@example.invalid", "not-a-login-hash", account.Id);
        user.Id = account.UserId; user.VerifyEmail();
        var affiliateCategory = AffiliateCategory.CreateMainCategory("Phase4", $"phase4-{tenant:N}", 1);
        affiliateCategory.Id = Guid.NewGuid();
        var policy = AffiliateProductPolicy.Create(tenant, product.Id, .10m, null, affiliateCategory.Id);
        policy.Id = Guid.NewGuid();
        var order = new Order { Id = Guid.NewGuid(), TenantId = tenant, OrderNumber = $"AFFILIATE_PHASE4_20261021_{Guid.NewGuid():N}",
            AffiliateAccountId = account.Id, AffiliateReferralCode = account.ReferralCode, Currency = "EGP", Total = 100m,
            PaymentMethodCode = "COD", PaymentStatus = PaymentStatus.Collected, FinancialPostingStatus = FinancialPostingStatus.Posted,
            FinancialPostedAtUtc = DateTime.UtcNow, FinancialPostingJournalId = Guid.NewGuid(),
            Status = OrderStatus.Delivered, FulfillmentMode = FulfillmentMode.Carrier, FulfillmentStatus = FulfillmentStatus.Delivered, ShippingAddress = "Isolated fixture" };
        var item = new OrderItem { Id = Guid.NewGuid(), OrderId = order.Id, ProductId = product.Id, Quantity = 1, UnitPrice = 100m, Total = 100m, PaidAmount = 100m,
            AffiliateCommissionEligible = true, AffiliateCommissionPolicyIdSnapshot = policy.Id, AffiliateCommissionRateSnapshot = .10m,
            AffiliateCommissionBaseAmountSnapshot = 100m, AffiliateCommissionAmountSnapshot = 10m, AffiliateCommissionSnapshotCapturedAtUtc = DateTime.UtcNow };
        order.Items.Add(item);
        seed.AddRange(category, product, account, user, affiliateCategory, policy, order,
            AffiliateAttribution.Create(tenant, order.Id, account.Id, account.ReferralCode, AttributionSource.ReferralLink));
        await seed.SaveChangesAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(s => s.GetRequiredService<TenantContext>());
        services.AddScoped(s => new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(seed.Database.GetConnectionString()).Options, s.GetRequiredService<ITenantContext>()));
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
        services.AddMassTransitTestHarness(config => config.AddEntityFrameworkOutbox<ApplicationDbContext>(outbox =>
        { outbox.UsePostgres(); outbox.UseBusOutbox(); }));
        await using var provider = services.BuildServiceProvider(validateScopes: true);

        AffiliateService Service(ApplicationDbContext db, ITenantContext scope, IEventPublisher publisher) =>
            new(db, scope, publisher, new AffiliateTelemetry(), NullLogger<AffiliateService>.Instance,
                Mock.Of<IAffiliatePayoutService>(MockBehavior.Strict), Mock.Of<IAffiliateSettlementOrchestrator>(MockBehavior.Strict), Options.Create(new AffiliateSettlementOptions()));

        // Fail after Earned was saved and its real outbox message staged, before approval commits.
        await using (var failureScope = provider.CreateAsyncScope())
        {
            var sp = failureScope.ServiceProvider;
            sp.GetRequiredService<TenantContext>().SetTenantId(tenant);
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var real = sp.GetRequiredService<IEventPublisher>();
            var fail = new Mock<IEventPublisher>();
            fail.Setup(p => p.PublishAsync(It.IsAny<AffiliateCommissionEarnedEvent>(), It.IsAny<CancellationToken>()))
                .Returns((AffiliateCommissionEarnedEvent e, CancellationToken ct) => real.PublishAsync(e, ct));
            fail.Setup(p => p.PublishAsync(It.IsAny<AffiliateCommissionApprovedEvent>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("PHASE4_INJECTED_FAILURE"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Service(db, sp.GetRequiredService<ITenantContext>(), fail.Object).RecordCommissionForOrderAsync(order.Id));
            await using var readback = database.CreateDbContext();
            Assert.Empty(await readback.AffiliateCommissionLedger.IgnoreQueryFilters().ToListAsync());
            Assert.Empty(await readback.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().ToListAsync());
            // Race the FIRST successful recognition: one same-scope retry with its
            // cached rolled-back OutboxState, and seven independent SQL handlers.
            await Task.WhenAll(Enumerable.Range(0, 7).Select(_ => Recognize()).Append(
                Service(db, sp.GetRequiredService<ITenantContext>(), real).RecordCommissionForOrderAsync(order.Id)));
        }

        async Task Recognize()
        {
            await using var scope = provider.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().SetTenantId(tenant);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await Service(db, scope.ServiceProvider.GetRequiredService<ITenantContext>(), scope.ServiceProvider.GetRequiredService<IEventPublisher>())
                .RecordCommissionForOrderAsync(order.Id);
        }
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Recognize()));
        await using var verify = database.CreateDbContext();
        Assert.Equal(2, await verify.AffiliateCommissionLedger.IgnoreQueryFilters().CountAsync());
        Assert.Equal(2, await verify.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().CountAsync());

        var persistedItem = await verify.OrderItems.IgnoreQueryFilters().SingleAsync(i => i.Id == item.Id);
        persistedItem.RefundedAmount = 25m;
        await verify.SaveChangesAsync();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Recognize()));
        Assert.Equal(2.5m, await verify.AffiliateCommissionLedger.IgnoreQueryFilters()
            .Where(e => e.EntryType == CommissionEntryType.Reversed).SumAsync(e => e.CommissionAmount));
        Assert.Equal(3, await verify.Set<MassTransit.EntityFrameworkCoreIntegration.OutboxMessage>().CountAsync());
        var balance = await new AffiliatePayoutService(verify, NullLogger<AffiliatePayoutService>.Instance).GetAvailableBalanceAsync(account.Id);
        Assert.Equal(7.5m, balance.RawAvailable);
        Assert.Empty(await verify.AffiliateBalanceLedger.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await verify.AffiliatePayoutRequests.IgnoreQueryFilters().ToListAsync());
    }
}
