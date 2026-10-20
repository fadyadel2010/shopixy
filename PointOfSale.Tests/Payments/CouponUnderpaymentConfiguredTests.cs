using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.Core.Discounts;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Discounts;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Payments;

public sealed class CouponUnderpaymentConfiguredTests
{
    [CouponUnderpaymentSeedFact]
    public async Task SeedOneDiscountedUnderpayment_RealRuntimeAdmission_NoExistingAmountOverwritten()
    {
        var tenant=Guid.Parse("c9c5c1ae-949e-498e-bc63-07242daeb49d");
        await using var configured=ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options,new TestTenantContext(tenant));
        Assert.False(await db.Orders.AnyAsync(o=>o.CustomerNotes=="CCQA20261019-UNDERPAY-HARNESS"));
        var original=await db.Orders.AsNoTracking().Include(o=>o.Items).SingleAsync(o=>o.Id==Guid.Parse("c3511e27-7b06-4374-af91-4ff4a8f5ae90"));
        Assert.Equal(532.5m,original.Total);Assert.Equal(27.5m,original.DiscountAmount);Assert.Equal(PaymentStatus.Collected,original.PaymentStatus);
        var proof=await db.PaymentIntents.Where(p=>p.OrderId==original.Id).Select(p=>p.ProofImageUrl).SingleAsync();
        var id=Guid.NewGuid();var now=DateTime.UtcNow;
        var order=new Order{Id=id,TenantId=tenant,OrderNumber="CCQA-20261019-UNDERPAY-"+id.ToString("N")[..8],
            CustomerNotes="CCQA20261019-UNDERPAY-HARNESS",GuestName="CCQA20261019 TEST ONLY UNDERPAY",GuestEmail="coupon-checkout-qa@example.test",GuestPhone="+201000000099",
            OnlineCustomerId=original.OnlineCustomerId,ShippingAddress="TEST ONLY no delivery",ShippingCity="QA City",ShippingCountry="EG",
            Subtotal=550m,DiscountAmount=27.5m,TotalDiscountAmount=27.5m,ShippingCost=10m,Total=532.5m,Currency="EGP",CouponId=original.CouponId,CouponCode=original.CouponCode,
            Source=OrderSource.Ecommerce,Status=OrderStatus.Pending,PaymentStatus=PaymentStatus.Pending,PaymentMethod=PaymentMethod.BankTransfer,PaymentMethodCode="MANUALWALLET",PaymentMethodName="TEST ONLY Manual Wallet",
            Items=original.Items.Select(i=>new OrderItem{Id=Guid.NewGuid(),OrderId=id,ProductId=i.ProductId,VariantId=i.VariantId,SKU=i.SKU,ProductName=i.ProductName,Quantity=i.Quantity,
                UnitPrice=i.UnitPrice,DiscountAmount=i.DiscountAmount,TaxAmount=i.TaxAmount,Total=i.Total,CostPrice=i.CostPrice,WarehouseId=i.WarehouseId}).ToList()};
        db.Orders.Add(order);
        var publisher=new Mock<IEventPublisher>();publisher.SetReturnsDefault(Task.CompletedTask);
        var coupons=new CouponValidationService(db,publisher.Object,NullLogger<CouponValidationService>.Instance);
        await using(var tx=await db.Database.BeginTransactionAsync())
        {
            await db.SaveChangesAsync();
            await coupons.ReserveCouponAsync(tenant,order.CouponId!.Value,order.CouponCode!,id,order.OnlineCustomerId,new CouponValidationSnapshot{OrderSubtotal=550m,AppliedDiscountAmount=27.5m},"CCQA-underpay");
            await tx.CommitAsync();
        }
        var lifecycle=ManualWalletConfiguredLifecycleTests.Lifecycle(db);
        var pi=await lifecycle.CreateReviewablePaymentIntentAsync(new ReviewablePaymentIntentCreateRequest{Id=Guid.NewGuid(),TenantId=tenant,OrderId=id,Amount=522.5m,Currency="EGP",PaymentMethod=PaymentMethod.BankTransfer,
            ProviderCode="ManualWallet",PaymentReference="CCQA-UNDERPAY-"+id.ToString("N"),IdempotencyKey="CCQA-UNDERPAY-"+id.ToString("N"),CreatedAtUtc=now,WalletType="VODAFONECASH",WalletPhoneNumber="01000000099",ProofImageUrl=proof,ProofImageRetentionExpiresAtUtc=now.AddDays(7)});
        await db.SaveChangesAsync();
        var config=new ConfigurationBuilder().SetBasePath(Path.Combine(TestConfiguration.GetSolutionRoot(),"PointOfSale.API")).AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json",true).AddJsonFile("appsettings.Development.local.json",true).Build();
        var services=new ServiceCollection();services.AddLogging();services.AddSingleton(db);services.AddManualPaymentFoundation(config);
        await using var provider=services.BuildServiceProvider();var submitted=DateTimeOffset.UtcNow;
        var initialized=await provider.GetRequiredService<IManualPaymentRuntimeApplicationService>().InitializeReferencePaymentAsync(new ManualPaymentRuntimeApplicationRequest(tenant,id,pi.Id,order.OrderNumber,pi.Amount,pi.Currency,pi.PaymentReference,pi.IdempotencyKey!,pi.IdempotencyKey!,now,submitted,ManualPaymentChannel.VodafoneCash,"CCQA",order.GuestEmail,order.GuestPhone));
        var metadata=new ManualPaymentProofMetadata(new Dictionary<string,string>{{"proof_identifier",proof!},{"proof_source","CCQA20261019 TEST ONLY underpayment fixture"},{"payment_intent_id",pi.Id.ToString("N")}});
        var admitted=await provider.GetRequiredService<IManualPaymentEvidenceApplicationService>().AdmitEvidenceAsync(new ManualPaymentEvidenceApplicationRequest(tenant,id,pi.Id,pi.PaymentReference,pi.Amount,pi.Currency,pi.IdempotencyKey!,now,submitted,ManualPaymentChannel.VodafoneCash,"01000000099",proof,metadata,initialized.ExpiresAtUtc));
        pi.ProviderMetadata=provider.GetRequiredService<IManualPaymentDurableEvidenceAuthority>().WriteReceiptEvidence(pi,null,initialized,admitted,metadata);
        var review=await lifecycle.AdmitPaymentReviewAsync(new PaymentReviewAdmissionRequest{TenantId=tenant,OrderId=id,PaymentIntentId=pi.Id,ReferenceCode=initialized.InstructionIdentifier,ProviderIntentId=initialized.InstructionIdentifier,ExpiresAtUtc=initialized.ExpiresAtUtc.UtcDateTime,ProofImageRetentionExpiresAtUtc=now.AddDays(7),Actor="CCQA20261019-harness"});
        Assert.True(review.IsSuccess,review.ErrorMessage);
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("COUPON_EVIDENCE")!,"underpay-manifest.json"),JsonSerializer.Serialize(new{orderId=id,order.OrderNumber,paymentIntentId=pi.Id,amount=pi.Amount,order.Total,order.DiscountAmount,proof,admission="New synthetic underpaid intent, real runtime/evidence/review admission; no existing amount modified"},new JsonSerializerOptions{WriteIndented=true}));
    }
}

public sealed class CouponUnderpaymentSeedFactAttribute:FactAttribute
{
    public CouponUnderpaymentSeedFactAttribute(){if(Environment.GetEnvironmentVariable("COUPON_SEED_UNDERPAY")!="1")Skip="Explicit new QA underpayment fixture opt-in required.";}
}
