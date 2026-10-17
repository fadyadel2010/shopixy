using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.SmartOffers;
using PointOfSale.API.Services.Promotions;
using PointOfSale.API.Services.Storefront;
using PointOfSale.API.DTOs.Cart;
using PointOfSale.Core.Discounts;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Discounts;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests;

public sealed class Phase3DiscountRegressionTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private ApplicationDbContext Db() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new TestTenantContext(_tenant));
    private static IEventPublisher Publisher() { var mock = new Mock<IEventPublisher>(); mock.SetReturnsDefault(Task.CompletedTask); return mock.Object; }

    [Fact]
    public async Task CouponCodeReuse_ArchivedRecordCannotMaskActiveReplacement()
    {
        await using var db = Db();
        var archived = Coupon("REUSE", true);
        var current = Coupon("REUSE", false);
        db.Coupons.AddRange(archived, current);
        await db.SaveChangesAsync();
        var authority = new CouponValidationService(db, Publisher(), NullLogger<CouponValidationService>.Instance);
        var result = await authority.ValidateCouponByCodeAsync(_tenant, " reuse ", Guid.NewGuid(), new CouponValidationSnapshot { OrderSubtotal = 100m }, "phase3");
        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Equal(current.Id, result.CouponId);
        Assert.Equal(10m, result.CalculatedDiscountAmount);
    }

    [Fact]
    public async Task CartCouponRevalidation_RepricesChangedBasketInsteadOfRejectingOldPreview()
    {
        await using var db = Db();
        db.Coupons.Add(Coupon("REPRICE", false));
        await db.SaveChangesAsync();
        var authority = new CouponValidationService(db, Publisher(), NullLogger<CouponValidationService>.Instance);
        var adapter = new StorefrontCouponService(authority, db);
        var result = await adapter.RevalidateCouponAsync(_tenant, "REPRICE", new CartDto { Subtotal = 200m,
            AppliedCoupon = new AppliedCouponInfo { Code = "REPRICE", Amount = 10m } }, Guid.NewGuid(), "phase3");
        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Equal(20m, result.DiscountAmount);
    }

    [Theory]
    [InlineData(99, 0)]
    [InlineData(0, -1)]
    public async Task Promotion_InvalidScopeOrPriority_RejectedBeforePersistence(int scope, int priority)
    {
        await using var db = Db();
        db.Tenants.Add(new Tenant { Id = _tenant, Name = "Synthetic", Slug = $"p3-{_tenant:N}", Currency = "EGP" });
        await db.SaveChangesAsync();
        var controller = new PromotionsController(db, new TestTenantContext(_tenant), Publisher(), NullLogger<PromotionsController>.Instance);
        var result = await controller.CreatePromotion(new CreatePromotionDto { Name = "Synthetic", Scope = (PromotionScope)scope, Priority = priority,
            DiscountValue = 10m, StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(1) });
        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await db.Promotions.ToListAsync());
    }

    [Theory]
    [InlineData("status")]
    [InlineData("trigger")]
    [InlineData("pages")]
    public async Task SmartOffer_InvalidEnum_RejectedBeforePersistence(string field)
    {
        await using var db = Db();
        db.Tenants.Add(new Tenant { Id = _tenant, Name = "Synthetic", Slug = $"p3-{_tenant:N}", Currency = "EGP" });
        await db.SaveChangesAsync();
        var controller = new AdminSmartOffersController(db, new TestTenantContext(_tenant), Publisher(), NullLogger<AdminSmartOffersController>.Instance);
        var request = new UpsertSmartOfferRequest { Name = "Synthetic", PopupTitle = "Synthetic", ButtonText = "Continue", Status = SmartOfferStatus.Inactive,
            TriggerType = SmartOfferTriggerType.ExitIntent, PagesScope = SmartOfferPagesScope.All };
        if (field == "status") request.Status = (SmartOfferStatus)99;
        if (field == "trigger") request.TriggerType = (SmartOfferTriggerType)99;
        if (field == "pages") request.PagesScope = (SmartOfferPagesScope)99;
        var result = await controller.CreateSmartOffer(request, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(await db.SmartOffers.ToListAsync());
    }

    [Fact]
    public async Task CartPromotionPreview_UsesSameBestSavingsWinnerAsCheckout()
    {
        await using var db = Db();
        var now = DateTime.UtcNow;
        db.Promotions.AddRange(Offer("Priority", 5m, 100, now), Offer("Savings", 20m, 0, now));
        await db.SaveChangesAsync();
        var engine = new PromotionEngine(db, NullLogger<PromotionEngine>.Instance);
        var preview = await engine.GetActiveCartPromotionAsync(_tenant, 100m, now);
        var checkout = await engine.CalculateAsync(new CartSnapshotForPromotion { TenantId = _tenant, Subtotal = 100m,
            Items = [new() { ProductId = Guid.NewGuid(), Quantity = 1, UnitPrice = 100m }] }, now, "phase3");
        Assert.Equal(20m, checkout.DiscountAmount);
        Assert.Equal(checkout.PromotionId, preview!.PromotionId);
        Assert.Equal(checkout.DiscountAmount, preview.CalculatedDiscount);
    }

    [Fact]
    public async Task SmartOfferEdit_PreservesExplicitZeroUsageLimits()
    {
        await using var db = Db();
        db.Tenants.Add(new Tenant { Id = _tenant, Name = "Synthetic", Slug = $"p3-{_tenant:N}", Currency = "EGP" });
        var coupon = Coupon("GENERATED", false); coupon.MaxUsage = 0; coupon.MaxUsagePerCustomer = 0;
        var offer = new SmartOffer { Id = Guid.NewGuid(), TenantId = _tenant, Name = "Synthetic", PopupTitle = "Synthetic", ButtonText = "Continue", GeneratedCouponId = coupon.Id, CouponCode = coupon.Code, CouponDiscountValue = 10m, RowVersion = 1 };
        db.Coupons.Add(coupon); db.SmartOffers.Add(offer); await db.SaveChangesAsync();
        var controller = new AdminSmartOffersController(db, new TestTenantContext(_tenant), Publisher(), NullLogger<AdminSmartOffersController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        controller.Request.Headers["If-Match"] = $"\"{offer.RowVersion}\"";
        Assert.IsType<OkObjectResult>((await controller.UpdateSmartOffer(offer.Id, new() { Name = "Edited", PopupTitle = "Synthetic", ButtonText = "Continue", CouponCode = coupon.Code, CouponDiscountValue = 10m }, default)).Result);
        Assert.Equal(0, coupon.MaxUsage); Assert.Equal(0, coupon.MaxUsagePerCustomer);
    }

    [Fact]
    public async Task PromotionEvaluation_UsesSuppliedClock_NotWallClockOrDerivativeCounter()
    {
        await using var db = Db();
        var now = DateTime.UtcNow.AddYears(1);
        var offer = Offer("Scheduled", 20m, 0, now);
        offer.MaxUsage = 1;
        offer.CurrentUsage = 1; // deliberately stale derivative; no SQL usage/reservations
        db.Promotions.Add(offer);
        await db.SaveChangesAsync();
        var engine = new PromotionEngine(db, NullLogger<PromotionEngine>.Instance);
        var result = await engine.CalculateAsync(new CartSnapshotForPromotion { TenantId = _tenant, Subtotal = 100m,
            Items = [new() { ProductId = Guid.NewGuid(), Quantity = 1, UnitPrice = 100m }] }, now, "phase3");
        Assert.Equal(20m, result.DiscountAmount);
    }

    [Fact]
    public async Task CartDiscountAuthority_CollapsesDerivativeStackingToCheckoutWinnerAndIsRepeatable()
    {
        await using var db = Db();
        db.Promotions.Add(Offer("SQL winner", 20m, 0, DateTime.UtcNow));
        await db.SaveChangesAsync();
        var cart = new CartDto { TenantId = _tenant, Subtotal = 999m, TaxTotal = 14m,
            Items = [new() { ProductId = Guid.NewGuid(), UnitPrice = 100m, Quantity = 1, HasActivePromotion = true, DiscountedUnitPrice = 60m }] };
        cart.ApplyCouponDiscount(new AppliedCouponInfo { CouponId = Guid.NewGuid(), Code = "VALIDATED", Amount = 10m });
        cart.ApplyCartPromotion(new AppliedCartPromotionInfo { PromotionId = Guid.NewGuid(), DiscountAmount = 30m });
        var service = new CartDiscountAuthority(db, new PromotionEngine(db, NullLogger<PromotionEngine>.Instance));
        await service.ReconcileAsync(cart, Guid.NewGuid(), "phase3", default);
        Assert.Equal(100m, cart.Subtotal); Assert.Equal(20m, cart.TotalDiscount); Assert.Equal(94m, cart.GrandTotal);
        Assert.Equal(0m, cart.PromotionDiscountTotal); Assert.Equal(0m, cart.DiscountTotal);
        await service.ReconcileAsync(cart, Guid.NewGuid(), "phase3", default);
        Assert.Equal(20m, cart.TotalDiscount); Assert.Equal(94m, cart.GrandTotal);
    }

    [Theory]
    [InlineData("Subtotal")]
    [InlineData("Discount")]
    [InlineData("CouponDiscount")]
    [InlineData("PromotionDiscount")]
    [InlineData("CartPromotionDiscount")]
    [InlineData("Shipping")]
    [InlineData("Tax")]
    [InlineData("PaymentFee")]
    [InlineData("ManualDiscount")]
    [InlineData("Total")]
    public async Task FinalCheckoutGuard_RejectsEveryForgedFinancialEcho(string property)
    {
        await using var db = Db();
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var service = typeof(PointOfSale.Tests.Admin.StorefrontPhase1ConfiguredTests).GetMethod("CreateValidationService", flags)!.Invoke(null, [db]);
        var method = typeof(PointOfSale.API.Services.Checkout.CheckoutService).GetMethod("EnsureExpectedPricingMatches", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var actual = new PointOfSale.API.Services.Pricing.StorefrontPricingBreakdown { Subtotal = 100m, CouponDiscount = 20m, TotalDiscount = 20m, FinalTotal = 80m };
        var expected = new PointOfSale.API.DTOs.Checkout.CheckoutExpectedPricingDto { Subtotal = 100m, Discount = 20m, CouponDiscount = 20m, Total = 80m };
        method.Invoke(service, [_tenant, expected, actual, "phase3"]); // genuine echo admitted
        typeof(PointOfSale.API.DTOs.Checkout.CheckoutExpectedPricingDto).GetProperty(property)!.SetValue(expected, 0.01m);
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => method.Invoke(service, [_tenant, expected, actual, "phase3"]));
        Assert.IsType<PointOfSale.API.Services.Checkout.CheckoutTotalMismatchException>(error.InnerException);
    }

    [Theory]
    [InlineData(0, 12.5, 80, null, 10)]
    [InlineData(0, 50, 80, 7.25, 7.25)]
    [InlineData(1, 200, 80, null, 80)]
    [InlineData(0, 10, 10.05, null, 1.01)]
    [InlineData(0, 10, 80, 0d, 0)]
    public async Task SqlOfferCalculation_PercentageFixedCapsAndRounding(int type, double value, double subtotal, double? cap, double expected)
    {
        await using var db = Db(); var now = DateTime.UtcNow;
        var offer = Offer("Arithmetic", (decimal)value, 0, now);
        offer.DiscountType = (DiscountType)type; offer.MaxDiscountAmount = cap.HasValue ? (decimal)cap.Value : null;
        db.Promotions.Add(offer); await db.SaveChangesAsync();
        var result = await new PromotionEngine(db, NullLogger<PromotionEngine>.Instance).CalculateAsync(new() { TenantId = _tenant, Subtotal = (decimal)subtotal,
            Items = [new() { ProductId = Guid.NewGuid(), UnitPrice = (decimal)subtotal, Quantity = 1 }] }, now, "phase3");
        Assert.Equal((decimal)expected, result.DiscountAmount);
        Assert.Equal(result.DiscountAmount, result.AppliedToItems.Sum(i => i.LineDiscountTotal));
    }

    [Fact]
    public async Task ProductAndCategoryOffers_DiscountOnlyMatchingLinesAndCouponWinsTies()
    {
        await using var db = Db(); var now = DateTime.UtcNow; var target = Guid.NewGuid(); var category = Guid.NewGuid();
        var offer = Offer("Targeted", 20m, 0, now); offer.Scope = PromotionScope.Category; offer.AppliesToId = category;
        db.Promotions.Add(offer); await db.SaveChangesAsync();
        var cart = new CartSnapshotForPromotion { TenantId = _tenant, Subtotal = 150m, Items = [new() { ProductId = target, CategoryId = category, UnitPrice = 100m, Quantity = 1 },
            new() { ProductId = Guid.NewGuid(), CategoryId = Guid.NewGuid(), UnitPrice = 50m, Quantity = 1 }] };
        var engine = new PromotionEngine(db, NullLogger<PromotionEngine>.Instance);
        var result = await engine.CalculateAsync(cart, now, "phase3");
        Assert.Equal(20m, result.DiscountAmount); Assert.Equal(target, Assert.Single(result.AppliedToItems).ProductId);
        cart.AppliedCouponId = Guid.NewGuid(); cart.AppliedCouponCode = "PREVALIDATED"; cart.PrevalidatedCouponDiscountAmount = 20m;
        Assert.Equal(PromotionType.Coupon, (await engine.CalculateAsync(cart, now, "phase3")).PromotionType);
        offer.Scope = PromotionScope.Product; offer.AppliesToId = target; await db.SaveChangesAsync();
        cart.AppliedCouponId = null; cart.AppliedCouponCode = null;
        Assert.Equal(20m, (await engine.CalculateAsync(cart, now, "phase3")).DiscountAmount);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task PromotionSchedule_InclusiveBoundaryAndInactiveWindows(int position, bool eligible)
    {
        await using var db = Db(); var now = DateTime.UtcNow;
        var offer = Offer("Boundary", 10m, 0, now);
        if (position < 0) offer.EndDate = now.AddTicks(-1);
        if (position == 0) { offer.StartDate = now; offer.EndDate = now; }
        if (position > 0) offer.StartDate = now.AddTicks(1);
        db.Promotions.Add(offer); await db.SaveChangesAsync();
        var result = await new PromotionEngine(db, NullLogger<PromotionEngine>.Instance).CalculateAsync(new() { TenantId = _tenant, Subtotal = 100m,
            Items = [new() { ProductId = Guid.NewGuid(), UnitPrice = 100m, Quantity = 1 }] }, now, "phase3");
        Assert.Equal(eligible, result.HasPromotion);
    }

    private Coupon Coupon(string code, bool deleted) { var c = new Coupon { Id = Guid.NewGuid(), TenantId = _tenant, IsDeleted = deleted, DiscountValue = 10m }; c.SetCode(code); return c; }
    private Promotion Offer(string name, decimal value, int priority, DateTime now) => new() { Id = Guid.NewGuid(), TenantId = _tenant, Name = name,
        Scope = PromotionScope.Cart, DiscountValue = value, Priority = priority, StartDate = now.AddDays(-1), EndDate = now.AddDays(1) };
}
