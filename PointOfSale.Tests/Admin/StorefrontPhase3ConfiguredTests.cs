using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.SmartOffers;
using PointOfSale.API.Services.Promotions;
using PointOfSale.Core.Discounts;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Discounts;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Admin;

// Uses ONLY the configured Development database. Never provisions/resets schema.
public sealed class StorefrontPhase3ConfiguredTests
{
    private static IEventPublisher Publisher() { var p = new Mock<IEventPublisher>(); p.SetReturnsDefault(Task.CompletedTask); return p.Object; }
    private static async Task<DbContextOptions<ApplicationDbContext>> Options()
    {
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        return new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
    }
    private static async Task Evidence(string name, object value)
    {
        var directory = Environment.GetEnvironmentVariable("STOREFRONT_PHASE3_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(directory)) await File.WriteAllTextAsync(Path.Combine(directory, name + ".json"), JsonSerializer.Serialize(value));
    }

    [Phase3ConfiguredFact]
    public async Task Configured_CouponPromotionSmartOfferLifecycleAndPricing_RollbackAndFreshObserver()
    {
        var options = await Options();
        var tenant = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        Guid couponId, promotionId, smartId;
        await using (var db = new ApplicationDbContext(options, new TestTenantContext(tenant)))
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '3s'; SET LOCAL statement_timeout = '30s'");
            db.Tenants.AddRange(new Tenant { Id = tenant, Name = "SYNTHETIC Phase3 rollback", Slug = $"p3-{tenant:N}", Currency = "EGP" },
                new Tenant { Id = foreign, Name = "SYNTHETIC Phase3 foreign", Slug = $"p3-{foreign:N}", Currency = "EGP" });
            await db.SaveChangesAsync();
            var authority = new CouponValidationService(db, Publisher(), NullLogger<CouponValidationService>.Instance);
            var coupons = new CouponsController(db, new TestTenantContext(tenant), Publisher(), authority, NullLogger<CouponsController>.Instance)
                { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
            var created = Assert.IsType<CreatedAtActionResult>((await coupons.CreateCoupon(new CreateCouponDto { Code = " phase3 ", DiscountType = DiscountType.Percentage,
                DiscountValue = 12.5m, MaxDiscountAmount = 10m, MinOrderAmount = 50m, IsActive = true }, default)).Result);
            var coupon = Assert.IsType<CouponDto>(created.Value); couponId = coupon.Id;
            Assert.Equal("PHASE3", coupon.Code);
            Assert.Equal(coupon.Code, Assert.IsType<CouponDto>(Assert.IsType<OkObjectResult>((await coupons.GetCoupon(coupon.Id)).Result).Value).Code);
            var customer = Guid.NewGuid();
            var valid = await authority.ValidateCouponByCodeAsync(tenant, " phase3 ", customer, new() { OrderSubtotal = 200m }, "phase3");
            Assert.True(valid.IsValid); Assert.Equal(10m, valid.CalculatedDiscountAmount);
            Assert.False((await authority.ValidateCouponByCodeAsync(foreign, "phase3", customer, new() { OrderSubtotal = 200m }, "phase3")).IsValid);
            Assert.Equal(CouponErrorCodes.MinimumNotMet, (await authority.ValidateCouponByCodeAsync(tenant, "phase3", customer, new() { OrderSubtotal = 49.99m }, "phase3")).ErrorCode);
            coupons.Request.Headers["If-Match"] = $"\"{coupon.RowVersion}\"";
            Assert.IsType<OkObjectResult>(await coupons.ToggleCouponStatus(coupon.Id, default));
            Assert.Equal(CouponErrorCodes.Inactive, (await authority.ValidateCouponByCodeAsync(tenant, "phase3", customer, new() { OrderSubtotal = 200m }, "phase3")).ErrorCode);

            var promotions = new PromotionsController(db, new TestTenantContext(tenant), Publisher(), NullLogger<PromotionsController>.Instance)
                { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
            var promotion = Assert.IsType<PromotionDto>(Assert.IsType<CreatedAtActionResult>((await promotions.CreatePromotion(new() {
                Name = "SYNTHETIC Phase3", Scope = PromotionScope.Cart, DiscountType = DiscountType.FixedAmount, DiscountValue = 30m,
                StartDate = DateTime.UtcNow.AddHours(-1), EndDate = DateTime.UtcNow.AddHours(1) })).Result).Value);
            promotionId = promotion.Id;
            var engine = new PromotionEngine(db, NullLogger<PromotionEngine>.Instance);
            var basket = new CartSnapshotForPromotion { TenantId = tenant, Subtotal = 200m, Items = [new() { ProductId = Guid.NewGuid(), UnitPrice = 100m, Quantity = 2 }],
                AppliedCouponId = couponId, AppliedCouponCode = coupon.Code, PrevalidatedCouponDiscountAmount = 10m };
            var price = await engine.CalculateAsync(basket, DateTime.UtcNow, "phase3");
            Assert.Equal(promotionId, price.PromotionId); Assert.Equal(30m, price.DiscountAmount);
            Assert.Equal(30m, price.AppliedToItems.Sum(i => i.LineDiscountTotal));
            var previewCart = new PointOfSale.API.DTOs.Cart.CartDto { TenantId = tenant,
                Items = [new() { ProductId = Guid.NewGuid(), UnitPrice = 100m, Quantity = 2, HasActivePromotion = true, DiscountedUnitPrice = 80m }] };
            await new CartDiscountAuthority(db, engine).ReconcileAsync(previewCart, customer, "phase3", default);
            Assert.Equal(30m, previewCart.TotalDiscount); Assert.Equal(170m, previewCart.GrandTotal);
            promotions.Request.Headers["If-Match"] = $"\"{promotion.RowVersion}\"";
            Assert.Null((await promotions.TogglePromotionStatus(promotionId)).Result);
            Assert.Equal(10m, (await engine.CalculateAsync(basket, DateTime.UtcNow, "phase3")).DiscountAmount);

            var smart = new AdminSmartOffersController(db, new TestTenantContext(tenant), Publisher(), NullLogger<AdminSmartOffersController>.Instance)
                { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
            var smartRequest = new UpsertSmartOfferRequest { Name = "SYNTHETIC Phase3 popup", Status = SmartOfferStatus.Inactive, TriggerType = SmartOfferTriggerType.ExitIntent,
                PagesScope = SmartOfferPagesScope.Cart, PopupTitle = "Synthetic", ButtonText = "Continue", CouponCode = "P3SMART", CouponDiscountType = DiscountType.FixedAmount, CouponDiscountValue = 7.25m };
            var smartOffer = Assert.IsType<AdminSmartOfferDto>(Assert.IsType<CreatedAtActionResult>((await smart.CreateSmartOffer(smartRequest, default)).Result).Value);
            smartId = smartOffer.Id;
            Assert.NotNull(smartOffer.GeneratedCouponId);
            Assert.False((await authority.ValidateCouponByCodeAsync(tenant, "P3SMART", customer, new() { OrderSubtotal = 100m }, "phase3")).IsValid);
            smart.Request.Headers["If-Match"] = $"\"{smartOffer.RowVersion}\"";
            smartRequest.Status = SmartOfferStatus.Active;
            Assert.IsType<OkObjectResult>((await smart.UpdateSmartOffer(smartId, smartRequest, default)).Result);
            Assert.Equal(7.25m, (await authority.ValidateCouponByCodeAsync(tenant, "P3SMART", customer, new() { OrderSubtotal = 100m }, "phase3")).CalculatedDiscountAmount);
            var currentSmart = await db.SmartOffers.AsNoTracking().SingleAsync(s => s.Id == smartId);
            smart.Request.Headers["If-Match"] = $"\"{currentSmart.RowVersion}\"";
            Assert.IsType<NoContentResult>(await smart.DeleteSmartOffer(smartId, default));
            Assert.False((await authority.ValidateCouponByCodeAsync(tenant, "P3SMART", customer, new() { OrderSubtotal = 100m }, "phase3")).IsValid);
            await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
            await tx.RollbackAsync();
        }
        await using var observer = new ApplicationDbContext(options, new TestTenantContext(tenant));
        Assert.False(await observer.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenant || t.Id == foreign));
        Assert.False(await observer.Coupons.IgnoreQueryFilters().AnyAsync(c => c.TenantId == tenant));
        Assert.False(await observer.Promotions.IgnoreQueryFilters().AnyAsync(c => c.TenantId == tenant));
        Assert.False(await observer.SmartOffers.IgnoreQueryFilters().AnyAsync(c => c.TenantId == tenant));
        await Evidence("configured-lifecycle-rollback", new { observedUtc = DateTime.UtcNow, tenant, foreign, couponId, promotionId, smartId, rolledBack = true, freshObserverEmpty = true, externalPublisher = "controlled" });
    }

    [Phase3ConfiguredFact]
    public async Task Configured_ExistingTenantSyntheticScopeCleanup_IndependentReadOnly()
    {
        var raw = Environment.GetEnvironmentVariable("STOREFRONT_PHASE3_OBSERVE_TENANT");
        Assert.True(Guid.TryParse(raw, out var tenant), "Explicit existing tenant required.");
        var options = await Options();
        await using var db = new ApplicationDbContext(options, new TestTenantContext(tenant));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        var coupons = await db.Coupons.AsNoTracking().IgnoreQueryFilters().Where(c => c.TenantId == tenant && c.Code.StartsWith("P3QA"))
            .Select(c => new { c.Id, c.Code, c.IsDeleted, c.CurrentUsage }).OrderBy(c => c.Id).ToListAsync();
        var smart = await db.SmartOffers.AsNoTracking().IgnoreQueryFilters().Where(o => o.TenantId == tenant && o.Name.StartsWith("P3QA")).CountAsync();
        var promotions = await db.Promotions.AsNoTracking().IgnoreQueryFilters().Where(o => o.TenantId == tenant && o.Name.StartsWith("P3QA")).CountAsync();
        Assert.All(coupons, c => { Assert.True(c.IsDeleted); Assert.Equal(0, c.CurrentUsage); });
        Assert.Equal(0, smart); Assert.Equal(0, promotions);
        Assert.False(db.ChangeTracker.HasChanges());
        await Evidence("hosted-synthetic-scope-sql-cleanup", new { observedUtc = DateTime.UtcNow, tenant, evidence = "configured PostgreSQL repeatable-read READ ONLY",
            coupons, smartOffersRemaining = smart, promotionsRemaining = promotions, syntheticCouponRetention = "soft-deleted by supported API, usage zero", writes = 0 });
        await tx.RollbackAsync();
    }

    [Phase3ConfiguredFact]
    public async Task Configured_ConcurrentPromotionCapacity_TargetedSyntheticCleanup()
    {
        var options = await Options(); var tenant = Guid.NewGuid(); var promotionId = Guid.NewGuid();
        var orders = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        bool[] results = [];
        try
        {
            await using (var seed = new ApplicationDbContext(options, new TestTenantContext(tenant)))
            {
                seed.Tenants.Add(new Tenant { Id = tenant, Name = "SYNTHETIC Phase3 offer concurrency", Slug = $"p3-{tenant:N}", Currency = "EGP" });
                seed.Promotions.Add(new Promotion { Id = promotionId, TenantId = tenant, Name = "SYNTHETIC capacity", Scope = PromotionScope.Cart,
                    DiscountValue = 10m, MaxUsage = 2, StartDate = DateTime.UtcNow.AddHours(-1), EndDate = DateTime.UtcNow.AddHours(1) });
                await seed.SaveChangesAsync();
            }
            async Task<bool> Reserve(Guid orderId)
            {
                await using var db = new ApplicationDbContext(options, new TestTenantContext(tenant));
                await using var tx = await db.Database.BeginTransactionAsync();
                await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '10s'; SET LOCAL statement_timeout = '20s'");
                var service = typeof(StorefrontPhase1ConfiguredTests).GetMethod("CreateValidationService", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, [db]);
                var type = typeof(PointOfSale.API.Services.Checkout.CheckoutService);
                // Same capacity lock used by checkout, taken before inserting
                // synthetic order snapshots (no inventory/payment operation).
                await (Task)type.GetMethod("LockPromotionForCapacityMutationAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(service, [tenant, promotionId, CancellationToken.None])!;
                var order = new Order { Id = orderId, TenantId = tenant, OrderNumber = $"P3-{orderId:N}", Currency = "EGP", Subtotal = 100m, Total = 90m,
                    PaymentMethodCode = "SYNTHETIC", Source = OrderSource.Ecommerce, AppliedPromotionId = promotionId, AppliedPromotionType = "offer", PromotionDiscountAmount = 10m };
                db.Orders.Add(order);
                await db.SaveChangesAsync();
                try
                {
                    await (Task<bool>)type.GetMethod("ReserveAppliedOfferPromotionAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(service, [tenant, order, null, "phase3", CancellationToken.None])!;
                    await tx.CommitAsync(); return true;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("usage limit changed")) { await tx.RollbackAsync(); return false; }
            }
            results = await Task.WhenAll(orders.Select(Reserve)).WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal(2, results.Count(r => r));
            await using var observer = new ApplicationDbContext(options, new TestTenantContext(tenant));
            Assert.Equal(2, await observer.Orders.CountAsync(o => o.AppliedPromotionId == promotionId));
            Assert.False((await new PromotionEngine(observer, NullLogger<PromotionEngine>.Instance).CalculateAsync(new() { TenantId = tenant, Subtotal = 100m,
                Items = [new() { ProductId = Guid.NewGuid(), Quantity = 1, UnitPrice = 100m }] }, DateTime.UtcNow, "phase3")).HasPromotion);
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options, new TestTenantContext(tenant));
            await using var tx = await cleanup.Database.BeginTransactionAsync();
            await cleanup.Orders.IgnoreQueryFilters().Where(r => r.TenantId == tenant).ExecuteDeleteAsync();
            await cleanup.Promotions.IgnoreQueryFilters().Where(r => r.TenantId == tenant).ExecuteDeleteAsync();
            await cleanup.Tenants.IgnoreQueryFilters().Where(r => r.Id == tenant).ExecuteDeleteAsync();
            await tx.CommitAsync();
            await using var observer = new ApplicationDbContext(options, new TestTenantContext(tenant));
            Assert.False(await observer.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenant));
            Assert.False(await observer.Orders.IgnoreQueryFilters().AnyAsync(o => o.TenantId == tenant));
            await Evidence("configured-promotion-concurrency-cleanup", new { tenant, promotionId, attempts = orders.Length, accepted = results.Count(r => r), freshObserverEmpty = true,
                evidence = "actual configured SQL and checkout capacity methods; controlled assignment; not full concurrent HTTP checkout", externalCalls = 0 });
        }
    }

    [Phase3ConfiguredFact]
    public async Task Configured_ConcurrentCouponCapacityAndSameOrderRetry_TargetedSyntheticCleanup()
    {
        var options = await Options(); var tenant = Guid.NewGuid(); var couponId = Guid.NewGuid();
        var attempts = Enumerable.Range(0, 8).Select(_ => (order: Guid.NewGuid(), customer: Guid.NewGuid())).ToArray();
        var results = Array.Empty<Guid?>();
        try
        {
            await using (var seed = new ApplicationDbContext(options, new TestTenantContext(tenant)))
            {
                seed.Tenants.Add(new Tenant { Id = tenant, Name = "SYNTHETIC Phase3 concurrency", Slug = $"p3-{tenant:N}", Currency = "EGP" });
                var coupon = new Coupon { Id = couponId, TenantId = tenant, DiscountValue = 10m, MaxUsage = 2 }; coupon.SetCode("P3CAPACITY"); seed.Coupons.Add(coupon);
                foreach (var a in attempts)
                {
                    seed.OnlineCustomers.Add(new OnlineCustomer { Id = a.customer, TenantId = tenant, Email = $"p3-{a.customer:N}@example.test", NormalizedEmail = $"P3-{a.customer:N}@EXAMPLE.TEST", DisplayName = "Synthetic", IsActive = true });
                    seed.Orders.Add(new Order { Id = a.order, TenantId = tenant, OnlineCustomerId = a.customer, OrderNumber = $"P3-{a.order:N}", Currency = "EGP", Subtotal = 100m, Total = 90m,
                        DiscountAmount = 10m, CouponId = couponId, CouponCode = "P3CAPACITY", PaymentMethodCode = "SYNTHETIC", Source = OrderSource.Ecommerce });
                }
                await seed.SaveChangesAsync();
            }
            async Task<Guid?> Reserve((Guid order, Guid customer) a)
            {
                await using var db = new ApplicationDbContext(options, new TestTenantContext(tenant));
                await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
                await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '10s'; SET LOCAL statement_timeout = '20s'");
                try
                {
                    var result = await new CouponValidationService(db, Publisher(), NullLogger<CouponValidationService>.Instance).ReserveCouponAsync(tenant, couponId, "P3CAPACITY", a.order, a.customer,
                        new() { OrderSubtotal = 100m, AppliedDiscountAmount = 10m }, "phase3-concurrent");
                    await tx.CommitAsync(); return result.ReservationId;
                }
                catch (CouponAuthorityException e) when (e.ErrorCode == CouponErrorCodes.MaxUsageExceeded) { await tx.RollbackAsync(); return null; }
            }
            results = await Task.WhenAll(attempts.Select(Reserve)).WaitAsync(TimeSpan.FromSeconds(60));
            Assert.Equal(2, results.Count(r => r.HasValue));
            var accepted = attempts[Array.FindIndex(results, r => r.HasValue)];
            var retries = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Reserve(accepted)));
            Assert.All(retries, r => Assert.Equal(results.First(r => r.HasValue), r));
            await using var observer = new ApplicationDbContext(options, new TestTenantContext(tenant));
            Assert.Equal(2, await observer.CouponReservations.IgnoreQueryFilters().CountAsync(r => r.TenantId == tenant));
            Assert.Equal(0, await observer.CouponUsages.IgnoreQueryFilters().CountAsync(r => r.TenantId == tenant));
            await using (var confirm = new ApplicationDbContext(options, new TestTenantContext(tenant)))
            await using (var transaction = await confirm.Database.BeginTransactionAsync())
            {
                var authority = new CouponValidationService(confirm, Publisher(), NullLogger<CouponValidationService>.Instance);
                var first = await authority.ConfirmCouponUsageAsync(tenant, accepted.order, "phase3-confirm");
                var again = await authority.ConfirmCouponUsageAsync(tenant, accepted.order, "phase3-confirm-retry");
                Assert.True(first.WasConfirmed); Assert.True(again.WasAlreadyConfirmed);
                await transaction.CommitAsync();
            }
            Assert.Equal(1, await observer.CouponUsages.IgnoreQueryFilters().CountAsync(r => r.TenantId == tenant));
            Assert.Equal(1, await observer.Coupons.AsNoTracking().Where(c => c.Id == couponId).Select(c => c.CurrentUsage).SingleAsync());
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options, new TestTenantContext(tenant));
            await using var tx = await cleanup.Database.BeginTransactionAsync();
            await cleanup.CouponReservations.IgnoreQueryFilters().Where(r => r.TenantId == tenant).ExecuteDeleteAsync();
            await cleanup.CouponUsages.IgnoreQueryFilters().Where(r => r.TenantId == tenant).ExecuteDeleteAsync();
            await cleanup.Orders.IgnoreQueryFilters().Where(r => r.TenantId == tenant).ExecuteDeleteAsync();
            await cleanup.Coupons.IgnoreQueryFilters().Where(r => r.TenantId == tenant).ExecuteDeleteAsync();
            await cleanup.OnlineCustomers.IgnoreQueryFilters().Where(r => r.TenantId == tenant).ExecuteDeleteAsync();
            await cleanup.Tenants.IgnoreQueryFilters().Where(r => r.Id == tenant).ExecuteDeleteAsync();
            await tx.CommitAsync();
            await using var observer = new ApplicationDbContext(options, new TestTenantContext(tenant));
            Assert.False(await observer.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenant));
            Assert.False(await observer.CouponReservations.IgnoreQueryFilters().AnyAsync(r => r.TenantId == tenant));
            await Evidence("configured-concurrency-cleanup", new { tenant, couponId, attempts = attempts.Length, accepted = results.Count(r => r.HasValue), retries = 5,
                confirmation = "actual service twice, one SQL usage and counter=1; controlled publisher, not a payment event", freshObserverEmpty = true, externalCalls = 0 });
        }
    }
}

public sealed class Phase3ConfiguredFactAttribute : FactAttribute
{
    public Phase3ConfiguredFactAttribute() { if (Environment.GetEnvironmentVariable("STOREFRONT_PHASE3_CONFIGURED") != "1") Skip = "Explicit existing-database Phase3 opt-in required."; }
}
