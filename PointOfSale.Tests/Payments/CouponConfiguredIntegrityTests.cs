using System.Data;
using System.Text.Json;
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
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Payments;

public sealed class CouponConfiguredIntegrityTests
{
    [CouponCleanupFact]
    public async Task ReleaseOnlyExpiredTaskReservation_ThroughCanonicalAuthority_RetainHistory()
    {
        var tenant=Guid.Parse("c9c5c1ae-949e-498e-bc63-07242daeb49d");
        var id=Guid.Parse("4ccce167-b11c-4caa-9ac9-d09dbb494fdf");
        await using var db=ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var order=await db.Orders.IgnoreQueryFilters().AsNoTracking().SingleAsync(o=>o.Id==id&&o.TenantId==tenant);
        Assert.StartsWith("CCQA20261019-",order.CustomerNotes);
        Assert.Equal(OrderStatus.Cancelled,order.Status);Assert.Equal(PaymentStatus.Expired,order.PaymentStatus);
        await using var tx=await db.Database.BeginTransactionAsync();
        var publisher=new Mock<IEventPublisher>();publisher.SetReturnsDefault(Task.CompletedTask);
        var service=new CouponValidationService(db,publisher.Object,NullLogger<CouponValidationService>.Instance);
        var result=await service.ReleaseCouponReservationAsync(tenant,id,"CCQA20261019-cleanup","Expired task-only order; release previously stranded coupon reservation");
        Assert.False(result.HadConfirmedUsage);
        var reservation=await db.CouponReservations.IgnoreQueryFilters().SingleAsync(r=>r.OrderId==id&&r.TenantId==tenant);
        Assert.Equal(CouponReservationStatus.Released,reservation.Status);
        await tx.CommitAsync();
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("COUPON_EVIDENCE")!,"expired-qa-coupon-release.json"),JsonSerializer.Serialize(new{orderId=id,reservation.Id,result.WasReleased,order.Status,order.PaymentStatus,historyRetained=true}));
    }

    [CouponConfiguredFact]
    public async Task Configured_CapturedReplayHash_ReadOnly()
    {
        var directory=Environment.GetEnvironmentVariable("COUPON_EVIDENCE")!;
        var text=(await File.ReadAllTextAsync(Path.Combine(directory,"postfix-wallet-checkout.txt"))).Replace("\r\n","\n");
        using var result=JsonDocument.Parse(text.Split("### Result\n")[1].Split("\n###")[0]);
        var request=JsonSerializer.Deserialize<PointOfSale.API.DTOs.Checkout.CheckoutRequestDto>(result.RootElement.GetProperty("request").GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
        var hash=typeof(PointOfSale.API.Services.Checkout.CheckoutService).GetMethod("BuildCheckoutRequestHash",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
        var customer=Guid.Parse("3e6129bf-5bac-4d26-86da-c7c42cb53833");
        var computed=(string)hash.Invoke(null,[request,customer.ToString("N"),"customer",null,customer,request.ProofFileKey])!;
        await using var db=ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        var row=await db.OrderRequestIdempotencyRecords.IgnoreQueryFilters().AsNoTracking().SingleAsync(r=>r.OrderId==Guid.Parse("4ccce167-b11c-4caa-9ac9-d09dbb494fdf"));
        await File.WriteAllTextAsync(Path.Combine(directory,"replay-hash-diagnostic.json"),JsonSerializer.Serialize(new{computed,persisted=row.RequestHash,request}));
        Assert.Equal(row.RequestHash,computed);
        await tx.RollbackAsync();
    }

    [CouponConfiguredFact]
    public async Task Configured_PendingReservationBoundary_ProbeRollbackOnly()
    {
        var tenant=Guid.Parse("c9c5c1ae-949e-498e-bc63-07242daeb49d");
        await using var configured=ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
        var id=Guid.NewGuid();
        object evidence;
        await using(var db=new ApplicationDbContext(options,new TestTenantContext(tenant)))
        await using(var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            var original=await db.Orders.AsNoTracking().SingleAsync(o=>o.Id==Guid.Parse("8ec5cde8-50e9-4a51-9e99-f90e497d88d0"));
            Assert.Equal("CCQA20261019FIVE",original.CouponCode);
            var publisher=new Mock<IEventPublisher>();publisher.SetReturnsDefault(Task.CompletedTask);
            var authority=new CouponValidationService(db,publisher.Object,NullLogger<CouponValidationService>.Instance);
            var snapshot=new CouponValidationSnapshot{OrderSubtotal=550m,AppliedDiscountAmount=27.5m};
            var preview=await authority.ValidateCouponByIdAsync(tenant,original.CouponId!.Value,original.CouponCode!,original.OnlineCustomerId,snapshot,"CCQA-probe");
            db.Orders.Add(new Order{Id=id,TenantId=tenant,OrderNumber="CCQA20261019-ROLLBACK-"+id.ToString("N"),OnlineCustomerId=original.OnlineCustomerId,
                Currency="EGP",Subtotal=550m,DiscountAmount=27.5m,Total=532.5m,Status=OrderStatus.Pending});
            await db.SaveChangesAsync();
            var exception=await Record.ExceptionAsync(()=>authority.ReserveCouponAsync(tenant,original.CouponId.Value,original.CouponCode!,id,original.OnlineCustomerId,snapshot,"CCQA-probe"));
            evidence=new{previewValid=preview.IsValid,preview.ErrorCode,exception=exception?.ToString(),scope="New rollback-only order; existing reported/synthetic order unmodified"};
            Assert.False(preview.IsValid);
            Assert.Equal(CouponErrorCodes.ReservationExists,preview.ErrorCode);
            Assert.Equal(CouponErrorCodes.ReservationExists,Assert.IsType<CouponAuthorityException>(exception).ErrorCode);
            await tx.RollbackAsync();
        }
        await using var observer=new ApplicationDbContext(options,new TestTenantContext(tenant));
        Assert.False(await observer.Orders.AnyAsync(o=>o.Id==id));
        Assert.False(await observer.CouponReservations.AnyAsync(r=>r.OrderId==id));
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("COUPON_EVIDENCE")!,"reservation-postfix.json"),JsonSerializer.Serialize(evidence,new JsonSerializerOptions{WriteIndented=true}));
    }
}

public sealed class CouponConfiguredFactAttribute : FactAttribute
{
    public CouponConfiguredFactAttribute(){if(Environment.GetEnvironmentVariable("COUPON_CONFIGURED")!="1")Skip="Explicit configured-database QA opt-in required.";}
}

public sealed class CouponCleanupFactAttribute : FactAttribute
{
    public CouponCleanupFactAttribute(){if(Environment.GetEnvironmentVariable("COUPON_CLEANUP")!="1")Skip="Explicit task-only canonical cleanup opt-in required.";}
}
