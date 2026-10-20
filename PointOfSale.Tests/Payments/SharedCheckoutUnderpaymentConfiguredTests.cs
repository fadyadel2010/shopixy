using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.API.Configuration;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Payments;

public sealed class SharedCheckoutUnderpaymentConfiguredTests
{
    [SharedUnderpaymentSeedFact]
    public async Task AdmitOneExplicitSyntheticUnderpayment_WithoutChangingExistingPayments()
    {
        var tenant = Guid.Parse("c9c5c1ae-949e-498e-bc63-07242daeb49d");
        await using var configured = ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(configured.Database.GetConnectionString()).Options, new TestTenantContext(tenant));
        Assert.False(await db.Orders.AnyAsync(o => o.CustomerNotes == "SHQA20261019-UNDERPAY-HARNESS"));
        var original = await db.Orders.AsNoTracking().Include(o => o.Items)
            .SingleAsync(o => o.Id == Guid.Parse("599e4784-1b0a-4630-b5b4-d08b4a8732fb"));
        Assert.Equal(560m, original.Total); Assert.Null(original.CouponId);
        var proof = await db.PaymentIntents.AsNoTracking().Where(p => p.OrderId == original.Id).Select(p => p.ProofImageUrl).SingleAsync();
        var id = Guid.NewGuid(); var now = DateTime.UtcNow;
        var order = new Order {
            Id=id,TenantId=tenant,OrderNumber="SHQA-20261019-UNDERPAY-"+id.ToString("N")[..8],
            CustomerNotes="SHQA20261019-UNDERPAY-HARNESS",GuestName="SHQA20261019 TEST ONLY UNDERPAY",
            GuestEmail="shared-checkout-qa@example.test",GuestPhone="+201000000098",OnlineCustomerId=original.OnlineCustomerId,
            ShippingAddress="TEST ONLY no delivery",ShippingCity="QA City",ShippingCountry="EG",Subtotal=550m,ShippingCost=10m,Total=560m,Currency="EGP",
            Source=OrderSource.Ecommerce,Status=OrderStatus.Pending,PaymentStatus=PaymentStatus.Pending,
            PaymentMethod=PaymentMethod.BankTransfer,PaymentMethodCode="MANUALWALLET",PaymentMethodName="TEST ONLY Manual Wallet",
            Items=original.Items.Select(i => new OrderItem {Id=Guid.NewGuid(),OrderId=id,ProductId=i.ProductId,VariantId=i.VariantId,
                SKU=i.SKU,ProductName=i.ProductName,Quantity=i.Quantity,UnitPrice=i.UnitPrice,Total=i.Total,CostPrice=i.CostPrice,WarehouseId=i.WarehouseId}).ToList()
        };
        db.Orders.Add(order); await db.SaveChangesAsync();
        var lifecycle = ManualWalletConfiguredLifecycleTests.Lifecycle(db);
        var pi = await lifecycle.CreateReviewablePaymentIntentAsync(new ReviewablePaymentIntentCreateRequest {
            Id=Guid.NewGuid(),TenantId=tenant,OrderId=id,Amount=550m,Currency="EGP",PaymentMethod=PaymentMethod.BankTransfer,
            ProviderCode="ManualWallet",PaymentReference="SHQA-UNDERPAY-"+id.ToString("N"),IdempotencyKey="SHQA-UNDERPAY-"+id.ToString("N"),
            CreatedAtUtc=now,WalletType="VODAFONECASH",WalletPhoneNumber="01000000098",ProofImageUrl=proof,ProofImageRetentionExpiresAtUtc=now.AddDays(7)
        });
        await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().SetBasePath(Path.Combine(TestConfiguration.GetSolutionRoot(),"PointOfSale.API"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json",true).AddJsonFile("appsettings.Development.local.json",true).Build();
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton(db); services.AddManualPaymentFoundation(config);
        await using var provider = services.BuildServiceProvider(); var submitted = DateTimeOffset.UtcNow;
        var initialized = await provider.GetRequiredService<IManualPaymentRuntimeApplicationService>().InitializeReferencePaymentAsync(
            new ManualPaymentRuntimeApplicationRequest(tenant,id,pi.Id,order.OrderNumber,pi.Amount,pi.Currency,pi.PaymentReference,pi.IdempotencyKey!,pi.IdempotencyKey!,now,submitted,ManualPaymentChannel.VodafoneCash,"SHQA",order.GuestEmail,order.GuestPhone));
        var metadata = new ManualPaymentProofMetadata(new Dictionary<string,string> {
            ["proof_identifier"]=proof!,["proof_source"]="SHQA20261019 TEST ONLY genuine underpayment fixture",["payment_intent_id"]=pi.Id.ToString("N") });
        var admitted = await provider.GetRequiredService<IManualPaymentEvidenceApplicationService>().AdmitEvidenceAsync(
            new ManualPaymentEvidenceApplicationRequest(tenant,id,pi.Id,pi.PaymentReference,pi.Amount,pi.Currency,pi.IdempotencyKey!,now,submitted,ManualPaymentChannel.VodafoneCash,"01000000098",proof,metadata,initialized.ExpiresAtUtc));
        pi.ProviderMetadata = provider.GetRequiredService<IManualPaymentDurableEvidenceAuthority>().WriteReceiptEvidence(pi,null,initialized,admitted,metadata);
        var result = await lifecycle.AdmitPaymentReviewAsync(new PaymentReviewAdmissionRequest {
            TenantId=tenant,OrderId=id,PaymentIntentId=pi.Id,ReferenceCode=initialized.InstructionIdentifier,ProviderIntentId=initialized.InstructionIdentifier,
            ExpiresAtUtc=initialized.ExpiresAtUtc.UtcDateTime,ProofImageRetentionExpiresAtUtc=now.AddDays(7),Actor="SHQA20261019-harness" });
        Assert.True(result.IsSuccess,result.ErrorMessage);
        await File.WriteAllTextAsync(Path.Combine(Environment.GetEnvironmentVariable("SHARED_CHECKOUT_EVIDENCE")!,"underpay-manifest.json"),
            JsonSerializer.Serialize(new {orderId=id,order.OrderNumber,paymentIntentId=pi.Id,amount=pi.Amount,order.Total,proof,
                admission="New intent at 550 against 560; real runtime/evidence admission. No existing amount changed; no Storefront amount field invented."}));
    }
}

public sealed class SharedUnderpaymentSeedFactAttribute : FactAttribute
{
    public SharedUnderpaymentSeedFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SHARED_CHECKOUT_SEED_UNDERPAY") != "1")
            Skip = "Explicit authorization for one new configured-development synthetic underpayment required.";
    }
}
