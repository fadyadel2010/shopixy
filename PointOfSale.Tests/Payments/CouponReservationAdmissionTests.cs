using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.Core.Discounts;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Discounts;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Payments;

public sealed class CouponReservationAdmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BackgroundRelease_UsesExplicitTenant_PreservesConfirmedUsageAndOtherTenants(bool confirmed)
    {
        var tenant=Guid.NewGuid();var foreign=Guid.NewGuid();var order=Guid.NewGuid();
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using(var seed=new ApplicationDbContext(options,new TestTenantContext(tenant)))
        {
            var coupon=new Coupon{Id=Guid.NewGuid(),TenantId=tenant,DiscountValue=5m};coupon.SetCode("QA-RELEASE");seed.Coupons.Add(coupon);
            seed.CouponReservations.Add(new CouponReservation{Id=Guid.NewGuid(),TenantId=tenant,CouponId=coupon.Id,OrderId=order,CustomerId=Guid.NewGuid(),Status=CouponReservationStatus.Reserved});
            if(confirmed)seed.CouponUsages.Add(new CouponUsage{Id=Guid.NewGuid(),TenantId=tenant,CouponId=coupon.Id,OrderId=order,CouponCode=coupon.Code});
            await seed.SaveChangesAsync();
        }
        await using var db=new ApplicationDbContext(options);
        var publisher=new Mock<IEventPublisher>();publisher.SetReturnsDefault(Task.CompletedTask);
        var service=new CouponValidationService(db,publisher.Object,NullLogger<CouponValidationService>.Instance);
        Assert.False((await service.ReleaseCouponReservationAsync(foreign,order,"qa","expiration")).WasReleased);
        Assert.Equal(!confirmed,(await service.ReleaseCouponReservationAsync(tenant,order,"qa","expiration")).WasReleased);
        var row=await db.CouponReservations.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(confirmed?CouponReservationStatus.Reserved:CouponReservationStatus.Released,row.Status);
        Assert.False((await service.ReleaseCouponReservationAsync(tenant,order,"qa","repeat")).WasReleased);
    }

    [Theory]
    [InlineData(CouponReservationStatus.Reserved, false, false)]
    [InlineData(CouponReservationStatus.Reserved, true, true)]
    [InlineData(CouponReservationStatus.Released, false, true)]
    [InlineData(CouponReservationStatus.Confirmed, false, true)]
    public async Task UnlimitedCoupon_PreviewMatchesOneActiveReservationBoundary_AndAllowsSameOrderReplay(
        CouponReservationStatus status, bool sameOrder, bool valid)
    {
        var tenant=Guid.NewGuid();var customer=Guid.NewGuid();var order=Guid.NewGuid();
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db=new ApplicationDbContext(options,new TestTenantContext(tenant));
        var coupon=new Coupon{Id=Guid.NewGuid(),TenantId=tenant,DiscountType=DiscountType.Percentage,DiscountValue=5m,IsActive=true};
        coupon.SetCode("SYNTHETIC-FIVE");db.Coupons.Add(coupon);
        db.CouponReservations.Add(new CouponReservation{Id=Guid.NewGuid(),TenantId=tenant,CouponId=coupon.Id,CustomerId=customer,OrderId=order,Status=status});
        await db.SaveChangesAsync();
        var publisher=new Mock<IEventPublisher>();publisher.SetReturnsDefault(Task.CompletedTask);
        var authority=new CouponValidationService(db,publisher.Object,NullLogger<CouponValidationService>.Instance);
        var snapshot=new CouponValidationSnapshot{OrderSubtotal=550m};
        var result=await authority.ValidateCouponByIdAsync(tenant,coupon.Id,coupon.Code,customer,snapshot,"test",excludeOrderId:sameOrder?order:null);
        Assert.Equal(valid,result.IsValid);
        if(!valid) {Assert.Equal(CouponErrorCodes.ReservationExists,result.ErrorCode);Assert.Contains("remove the coupon",result.ErrorMessage);}
        if(valid) Assert.Equal(27.5m,result.CalculatedDiscountAmount);
        Assert.True((await authority.ValidateCouponByIdAsync(tenant,coupon.Id,coupon.Code,Guid.NewGuid(),snapshot,"other-customer")).IsValid);
        Assert.False((await authority.ValidateCouponByIdAsync(Guid.NewGuid(),coupon.Id,coupon.Code,customer,snapshot,"foreign-tenant")).IsValid);
        Assert.Single(await db.CouponReservations.ToListAsync());
        Assert.Empty(await db.CouponUsages.ToListAsync());
    }
}
