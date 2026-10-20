using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Configuration;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Payments.ManualPayment;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Tests.Infrastructure;
using PointOfSale.Tests.Shipping;

namespace PointOfSale.Tests.Payments;

public sealed class ManualWalletConfiguredTenantTests
{
    [ManualWalletTenantFact]
    public async Task CurrentPostgres_TwoTenantConfigurationAndForeignDecisions_IsolatedAndRolledBack()
    {
        await using var configured=ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        var first=Guid.NewGuid();var second=Guid.NewGuid();
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(configured.Database.GetConnectionString()).Options,new TestTenantContext(first));
        await using var tx=await db.Database.BeginTransactionAsync();
        db.Tenants.AddRange(new[]{first,second}.Select(id=>new Tenant{Id=id,Name="MWQA20261019 rollback tenant",Slug="mwqa-"+id.ToString("N"),Currency="EGP",IsActive=true,Status=TenantStatus.Active,CreatedAtUtc=DateTime.UtcNow}));
        await db.SaveChangesAsync();
        await using var other=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(db.Database.GetDbConnection()).Options,new TestTenantContext(second));
        await other.Database.UseTransactionAsync(tx.GetDbTransaction());
        using var telemetry=new CommerceReliabilityTelemetry();
        var protection=new EphemeralDataProtectionProvider();var publisher=new Mock<IEventPublisher>();publisher.SetReturnsDefault(Task.CompletedTask);
        PaymentMethodsAdminController Controller(ApplicationDbContext context,Guid tenant)=>new(context,publisher.Object,protection,telemetry,NullLogger<PaymentMethodsAdminController>.Instance,new TestTenantContext(tenant)){ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext()}};
        var a=Controller(db,first);var b=Controller(other,second);
        Assert.IsType<BadRequestObjectResult>(await a.EnablePaymentMethod("MANUALWALLET",default));
        // Failed create remains only tracked; discard it before the successful configure.
        db.ChangeTracker.Clear();
        Assert.IsType<OkObjectResult>(await a.UpdateConfiguration("MANUALWALLET",new(){Configuration=new(){["walletNumber"]="01000000011"}},default));
        Assert.IsType<OkObjectResult>(await a.EnablePaymentMethod("MANUALWALLET",default));
        Assert.IsType<NotFoundObjectResult>(await b.GetPaymentMethod("MANUALWALLET",default));
        Assert.IsType<OkObjectResult>(await b.UpdateConfiguration("MANUALWALLET",new(){Configuration=new(){["walletNumber"]="01000000022"}},default));
        Assert.IsType<OkObjectResult>(await b.EnablePaymentMethod("MANUALWALLET",default));
        var aResult=Assert.IsType<PaymentMethodResponseDto>(Assert.IsType<OkObjectResult>(await a.GetPaymentMethod("MANUALWALLET",default)).Value);
        var bResult=Assert.IsType<PaymentMethodResponseDto>(Assert.IsType<OkObjectResult>(await b.GetPaymentMethod("MANUALWALLET",default)).Value);
        Assert.NotEqual(aResult.Id,bResult.Id);Assert.Equal("01000000011",aResult.PublicConfiguration["walletNumber"]);Assert.Equal("01000000022",bResult.PublicConfiguration["walletNumber"]);
        var review=new Mock<IManualPaymentReviewApplicationService>(MockBehavior.Strict);
        var foreign=new AdminManualPaymentController(review.Object,Mock.Of<IManualPaymentCapabilityAdmissionAuthority>(),Mock.Of<IManualPaymentExpirationAuthority>(),Mock.Of<IManualPaymentRetentionAuthority>(),new TestTenantContext(second),other,Mock.Of<IR2StorageService>(),Options.Create(new R2Options()),NullLogger<AdminManualPaymentController>.Instance) {
            ControllerContext=new ControllerContext{HttpContext=new DefaultHttpContext {User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]{new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier,"MWQA-other-admin")},"test"))}}
        };
        var ownedByAhram=Guid.Parse("4c614307-2ba1-4e8e-b0f4-e38342f1807e");
        Assert.IsType<NotFoundObjectResult>(await foreign.ConfirmPayment(ownedByAhram,default));
        Assert.IsType<NotFoundObjectResult>(await foreign.RejectPayment(ownedByAhram,new(){Reason="MWQA foreign decision must not run"},default));
        review.VerifyNoOtherCalls();
        await db.Database.ExecuteSqlRawAsync("SET CONSTRAINTS ALL IMMEDIATE");
        await tx.RollbackAsync();
        await using var observer=ConfiguredShippingFinalReadOnlyTests.ResolveDevelopment();
        Assert.False(await observer.Tenants.IgnoreQueryFilters().AnyAsync(t=>t.Id==first||t.Id==second));
        Assert.False(await observer.TenantPaymentMethods.IgnoreQueryFilters().AnyAsync(t=>t.TenantId==first||t.TenantId==second));
    }
}

public sealed class ManualWalletTenantFactAttribute : FactAttribute
{
    public ManualWalletTenantFactAttribute(){if(Environment.GetEnvironmentVariable("MANUAL_WALLET_CONFIGURED")!="1")Skip="Explicit configured-database rollback opt-in required.";}
}
