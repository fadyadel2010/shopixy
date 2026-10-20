using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Marketplace;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Infrastructure.Observability;
using PointOfSale.Infrastructure.Payments;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Payments;

public sealed class ManualWalletConfiguredLifecycleTests
{
    private static readonly Guid Tenant = Guid.Parse("c9c5c1ae-949e-498e-bc63-07242daeb49d");

    [ManualWalletSeedFact]
    public async Task SeedOneExplicitUnderpayment_ThroughCanonicalRuntimeAndReviewAdmission_NoExistingRecordChanged()
    {
        var evidence = Environment.GetEnvironmentVariable("MANUAL_WALLET_EVIDENCE")!;
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options;
        await using var db = new ApplicationDbContext(options, new TestTenantContext(Tenant));
        Assert.False(await db.Orders.AnyAsync(o => o.CustomerNotes == "MWQA20261019-UNDERPAY-HARNESS"));
        var original = await db.Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Id == Guid.Parse("1ddbaaff-0ef9-49fa-8d6b-fac625f03942"));
        Assert.Null(original.CouponId); Assert.Equal(0m, original.DiscountAmount); Assert.Equal(35m, original.Total);
        var proof = await db.PaymentIntents.AsNoTracking().Where(p => p.OrderId == original.Id).Select(p => p.ProofImageUrl).SingleAsync();
        var id = Guid.NewGuid(); var now = DateTime.UtcNow;
        var order = new Order {
            Id=id, TenantId=Tenant, OrderNumber=$"MWQA-20261019-UNDERPAY-{id.ToString("N")[..8]}",
            CustomerNotes="MWQA20261019-UNDERPAY-HARNESS", GuestName="MWQA20261019 UNDERPAY HARNESS TEST ONLY",
            GuestEmail="manual-wallet-qa@example.test", GuestPhone="+201000000099", IsGuestCheckout=true,
            ShippingAddress="TEST ONLY no delivery",ShippingCity="QA City",ShippingCountry="EG",
            Subtotal=25m,ShippingCost=10m,Total=35m,Currency="EGP",Source=OrderSource.Ecommerce,
            Status=OrderStatus.Pending,PaymentStatus=PaymentStatus.Pending,PaymentMethod=PaymentMethod.BankTransfer,
            PaymentMethodCode="MANUALWALLET",PaymentMethodName="TEST ONLY Manual Wallet",CreatedAt=now,
            Items=original.Items.Select(i => new OrderItem {Id=Guid.NewGuid(),OrderId=id,ProductId=i.ProductId,VariantId=i.VariantId,
                ProductName=i.ProductName,SKU=i.SKU,Quantity=i.Quantity,UnitPrice=i.UnitPrice,Total=i.Total,CostPrice=i.CostPrice,WarehouseId=i.WarehouseId}).ToList()
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var lifecycle = Lifecycle(db);
        var pi = await lifecycle.CreateReviewablePaymentIntentAsync(new ReviewablePaymentIntentCreateRequest {
            Id=Guid.NewGuid(),TenantId=Tenant,OrderId=id,Amount=25m,Currency="EGP",PaymentMethod=PaymentMethod.BankTransfer,
            ProviderCode="ManualWallet",PaymentReference=$"MWQA-UNDERPAY-{id:N}",IdempotencyKey=$"MWQA-UNDERPAY-{id:N}",
            CreatedAtUtc=now,WalletType="VODAFONECASH",WalletPhoneNumber="01000000099",ProofImageUrl=proof,
            ProofImageRetentionExpiresAtUtc=now.AddDays(7)
        });
        await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().SetBasePath(Path.Combine(TestConfiguration.GetSolutionRoot(),"PointOfSale.API"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json",true).AddJsonFile("appsettings.Development.local.json",true).Build();
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton(db); services.AddManualPaymentFoundation(config);
        await using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<IManualPaymentRuntimeApplicationService>();
        var submitted = DateTimeOffset.UtcNow;
        var initialized = await runtime.InitializeReferencePaymentAsync(new ManualPaymentRuntimeApplicationRequest(
            Tenant,id,pi.Id,order.OrderNumber,pi.Amount,pi.Currency,pi.PaymentReference,pi.IdempotencyKey!,pi.IdempotencyKey!,now,submitted,ManualPaymentChannel.VodafoneCash,"MWQA",order.GuestEmail,order.GuestPhone));
        var metadata = new ManualPaymentProofMetadata(new Dictionary<string,string> { ["proof_identifier"]=proof!,["proof_source"]="MWQA20261019 configured underpayment harness",["payment_intent_id"]=pi.Id.ToString("N") });
        var admitted = await provider.GetRequiredService<IManualPaymentEvidenceApplicationService>().AdmitEvidenceAsync(new ManualPaymentEvidenceApplicationRequest(
            Tenant,id,pi.Id,pi.PaymentReference,pi.Amount,pi.Currency,pi.IdempotencyKey!,now,submitted,ManualPaymentChannel.VodafoneCash,"01000000099",proof,metadata,initialized.ExpiresAtUtc));
        pi.ProviderMetadata=provider.GetRequiredService<IManualPaymentDurableEvidenceAuthority>().WriteReceiptEvidence(pi,null,initialized,admitted,metadata);
        var result=await lifecycle.AdmitPaymentReviewAsync(new PaymentReviewAdmissionRequest {
            TenantId=Tenant,OrderId=id,PaymentIntentId=pi.Id,ReferenceCode=initialized.InstructionIdentifier,ProviderIntentId=initialized.InstructionIdentifier,
            ExpiresAtUtc=initialized.ExpiresAtUtc.UtcDateTime,ProofImageRetentionExpiresAtUtc=now.AddDays(7),Actor="MWQA20261019-harness"
        });
        Assert.True(result.IsSuccess,result.ErrorMessage);
        await File.WriteAllTextAsync(Path.Combine(evidence,"underpay-manifest.json"),JsonSerializer.Serialize(new {orderId=id,order.OrderNumber,paymentIntentId=pi.Id,submittedAmount=pi.Amount,authoritativePayable=order.Total,proof,
            creation="New synthetic fixture through canonical CreateReviewablePaymentIntent, real runtime initialization, receipt admission and AdmitPaymentReview. Storefront has no amount entry field; no original payment amount overwritten."},new JsonSerializerOptions{WriteIndented=true}));
    }

    [Fact]
    public void LegacyItemGuard_IndependentlyDemonstratesNonCouponShippingGap()
    {
        var order=new Order {Total=35m,ShippingCost=10m,Currency="EGP",Items=[new OrderItem {UnitPrice=25m,Quantity=1,Total=25m}]};
        var method=typeof(PaymentLifecycleService).GetMethod("ValidateSuccessfulAmount",BindingFlags.NonPublic|BindingFlags.Static)!;
        // Existing item guard alone accepts 25 against payable 35; new Manual Wallet
        // guard complements it, rather than changing coupon arithmetic/allocation.
        Assert.Null(Record.Exception(()=>method.Invoke(null,[order,25m])));
    }

    [Theory]
    [InlineData(24, false)] [InlineData(25,false)] [InlineData(34,false)] [InlineData(35,true)]
    public async Task ManualSettlementGuard_RequiresAcceptedChargesWithoutChangingItemOrCouponRules(decimal submitted,bool valid)
    {
        var tenant=Guid.NewGuid();
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db=new ApplicationDbContext(options,new TestTenantContext(tenant));
        var order=new Order {Id=Guid.NewGuid(),TenantId=tenant,Total=35m,ShippingCost=10m,Currency="EGP",Items=[new OrderItem {UnitPrice=25m,Quantity=1,Total=25m}]};
        var method=typeof(PaymentLifecycleService).GetMethod("ValidateManualWalletSettlementAsync",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var result=await (Task<PaymentTransitionResult?>)method.Invoke(Lifecycle(db),[order,null,submitted,CancellationToken.None])!;
        Assert.Equal(valid,result is null);
        if(!valid) { Assert.Equal("PAYMENT_INSUFFICIENT_AMOUNT",result!.FailureCode);Assert.Contains("shortfall",result.ErrorMessage); }
        Assert.Equal(0m,order.TotalPaidAmount);
    }

    internal static PaymentLifecycleService Lifecycle(ApplicationDbContext db)
    {
        var publisher=new Mock<IEventPublisher>();publisher.SetReturnsDefault(Task.CompletedTask);
        return new(db,Mock.Of<IAccountingOutboxService>(),Mock.Of<ICommerceAccountingService>(),publisher.Object,
            Mock.Of<IMarketplaceEventOutboxService>(),Mock.Of<ICorrelationIdAccessor>(),Mock.Of<IProductVariantStockRepository>(),
            Mock.Of<IInventoryReservationMetricsService>(),Mock.Of<ICouponValidationService>(),NullLogger<PaymentLifecycleService>.Instance,Options.Create(new PaymentLifecycleSettings()));
    }
}

public sealed class ManualWalletSeedFactAttribute : FactAttribute
{
    public ManualWalletSeedFactAttribute() { if(Environment.GetEnvironmentVariable("MANUAL_WALLET_SEED_UNDERPAY")!="1") Skip="Explicit current-database new QA fixture opt-in required."; }
}
