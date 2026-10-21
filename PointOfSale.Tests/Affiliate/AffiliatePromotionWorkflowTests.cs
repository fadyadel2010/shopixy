using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MongoDB.Documents;
using PointOfSale.Infrastructure.MongoDB.Documents.Affiliate;

namespace PointOfSale.Tests.Affiliate;

public sealed class AffiliatePromotionWorkflowTests
{
    [Fact]
    public async Task OneGlobalIdentity_CreatesAndReadsTwoMerchantLinks_Idempotently()
    {
        await using var fixture = await Fixture.Create();
        var profileCode = fixture.Account.ReferralCode;
        foreach (var product in fixture.Products)
        {
            var created = Assert.IsType<CreatedAtActionResult>(await fixture.Controller.Create(new() { TenantId = product.TenantId, ProductId = product.Id }, default));
            var response = Assert.IsType<PromotionLinkResponse>(created.Value);
            Assert.NotEqual(Guid.Empty, response.Id);
            Assert.NotEqual(profileCode, response.ReferralCode);
            Assert.Matches("^[A-Z0-9]{10}$", response.ReferralCode);
            Assert.Equal($"https://{product.TenantId:N}.example.test/products/{product.Slug}?ref={response.ReferralCode}", response.PromotionUrl);
            Assert.Equal(product.Name, response.ProductName);
            var repeated = Assert.IsType<OkObjectResult>(await fixture.Controller.Create(new() { TenantId = product.TenantId, ProductId = product.Id }, default));
            Assert.Equal(response.Id, Assert.IsType<PromotionLinkResponse>(repeated.Value).Id);
        }
        var links = await fixture.Db.AffiliatePromotionLinks.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(2, links.Count);
        Assert.All(links, link => { Assert.Equal(fixture.User.Id, link.AffiliateUserId); Assert.Equal(fixture.Account.Id, link.AffiliateAccountId); });
        Assert.Equal(2, links.Select(l => l.TenantId).Distinct().Count());
        Assert.Equal(2, Assert.IsType<List<PromotionLinkResponse>>(Assert.IsType<OkObjectResult>(await fixture.Controller.List(default)).Value).Count);
        Assert.Equal(profileCode, fixture.Account.ReferralCode);
        Assert.Empty(await fixture.Db.AffiliateCommissionLedger.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await fixture.Db.AffiliatePayoutRequests.IgnoreQueryFilters().ToListAsync());
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("draft")]
    [InlineData("hidden")]
    [InlineData("unsearchable")]
    [InlineData("policy")]
    [InlineData("category")]
    [InlineData("merchant")]
    [InlineData("price")]
    [InlineData("stock")]
    [InlineData("url")]
    public async Task CurrentEligibility_RejectsStaleCardsWithoutCreatingRecords(string mutation)
    {
        await using var fixture = await Fixture.Create();
        var product = fixture.Products[0];
        switch (mutation)
        {
            case "inactive": product.IsActive = false; break;
            case "deleted": product.IsDeleted = true; break;
            case "draft": product.PublishStatus = PublishStatus.Draft; break;
            case "hidden": product.IsOnlineVisible = false; break;
            case "unsearchable": product.IsSearchable = false; break;
            case "policy": (await fixture.Db.AffiliateProductPolicies.IgnoreQueryFilters().FirstAsync(p => p.ProductId == product.Id)).Disable(); break;
            case "category": (await fixture.Db.AffiliateCategories.IgnoreQueryFilters().SingleAsync()).Deactivate(); break;
            case "merchant": (await fixture.Db.Tenants.SingleAsync(t => t.Id == product.TenantId)).IsActive = false; break;
            case "price": (await fixture.Db.ProductUnits.IgnoreQueryFilters().SingleAsync(u => u.ProductId == product.Id)).SellingPrice = 0; break;
            case "stock": product.TrackStock = true; product.AllowBackorder = false; break;
            case "url": (await fixture.Db.Tenants.SingleAsync(t => t.Id == product.TenantId)).StorefrontBaseUrl = null; break;
        }
        await fixture.Db.SaveChangesAsync();
        var result = await fixture.Controller.Create(new() { TenantId = product.TenantId, ProductId = product.Id }, default);
        Assert.True(result is BadRequestObjectResult or NotFoundObjectResult);
        Assert.Empty(await fixture.Db.AffiliatePromotionLinks.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task WrongProductOwner_AndForgedProfile_LeaveNoLinks()
    {
        await using var fixture = await Fixture.Create();
        var product = fixture.Products[0];
        Assert.IsType<NotFoundObjectResult>(await fixture.Controller.Create(new() { TenantId = fixture.Products[1].TenantId, ProductId = product.Id }, default));
        fixture.Controller.User.FindFirst("affiliate_account_id")!.Subject!.RemoveClaim(fixture.Controller.User.FindFirst("affiliate_account_id")!);
        ((ClaimsIdentity)fixture.Controller.User.Identity!).AddClaim(new("affiliate_account_id", Guid.NewGuid().ToString()));
        Assert.IsType<UnauthorizedObjectResult>(await fixture.Controller.Create(new() { TenantId = product.TenantId, ProductId = product.Id }, default));
        Assert.Empty(await fixture.Db.AffiliatePromotionLinks.IgnoreQueryFilters().ToListAsync());
    }

    [Theory]
    [InlineData(AffiliateStatus.Pending, true)]
    [InlineData(AffiliateStatus.Suspended, true)]
    [InlineData(AffiliateStatus.Closed, true)]
    [InlineData(AffiliateStatus.Active, false)]
    public async Task UnverifiedOrNonActiveIdentity_CannotCreate(AffiliateStatus status, bool verified)
    {
        await using var fixture = await Fixture.Create(status, verified);
        var product = fixture.Products[0];
        Assert.IsType<UnauthorizedObjectResult>(await fixture.Controller.Create(new() { TenantId = product.TenantId, ProductId = product.Id }, default));
        Assert.Empty(await fixture.Db.AffiliatePromotionLinks.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task OtherAffiliateCannotInspectDisableOrListOwnedLinks()
    {
        await using var fixture = await Fixture.Create();
        var foreign = AffiliatePromotionLink.Create(fixture.Products[0].TenantId, Guid.NewGuid(), fixture.Products[0].Id, "FOREIGN1234", Guid.NewGuid());
        fixture.Db.AffiliatePromotionLinks.Add(foreign);
        await fixture.Db.SaveChangesAsync();
        Assert.IsType<NotFoundObjectResult>(await fixture.Controller.GetById(foreign.Id, default));
        Assert.IsType<NotFoundObjectResult>(await fixture.Controller.Deactivate(foreign.Id, default));
        Assert.Empty(Assert.IsType<List<PromotionLinkResponse>>(Assert.IsType<OkObjectResult>(await fixture.Controller.List(default)).Value));
        Assert.True(foreign.IsActive);
    }

    [Fact]
    public async Task DisabledLink_ReactivatesSameRecord_AndIneligibleLinkIsHiddenNotDeleted()
    {
        await using var fixture = await Fixture.Create();
        var product = fixture.Products[0];
        var first = Assert.IsType<PromotionLinkResponse>(Assert.IsType<CreatedAtActionResult>(await fixture.Controller.Create(new() { TenantId = product.TenantId, ProductId = product.Id }, default)).Value);
        Assert.IsType<NoContentResult>(await fixture.Controller.Deactivate(first.Id, default));
        var again = Assert.IsType<PromotionLinkResponse>(Assert.IsType<OkObjectResult>(await fixture.Controller.Create(new() { TenantId = product.TenantId, ProductId = product.Id }, default)).Value);
        Assert.Equal(first.Id, again.Id);
        Assert.Equal(first.ReferralCode, again.ReferralCode);
        Assert.True(again.IsActive);
        product.IsActive = false;
        await fixture.Db.SaveChangesAsync();
        Assert.IsType<NotFoundObjectResult>(await fixture.Controller.GetById(first.Id, default));
        Assert.Empty(Assert.IsType<List<PromotionLinkResponse>>(Assert.IsType<OkObjectResult>(await fixture.Controller.List(default)).Value));
        Assert.Single(await fixture.Db.AffiliatePromotionLinks.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task DetailWithoutMongoProjection_UsesSqlOwnerAndCurrentMetadata()
    {
        await using var fixture = await Fixture.Create();
        var portal = new AffiliatePortalController(fixture.Db, null!, null!, null!, null!, fixture.Resolver, NullLogger<AffiliatePortalController>.Instance);
        foreach (var product in fixture.Products)
        {
            var detail = Assert.IsType<AffiliateProductDetailResponse>(Assert.IsType<OkObjectResult>((await portal.GetProductDetail(product.Id, default)).Result).Value);
            Assert.Equal(product.TenantId, detail.TenantId);
            Assert.Equal(product.Name, detail.Name);
            Assert.Equal(25.75m, detail.Price);
            Assert.Empty(detail.Variants);
        }
    }

    [Fact]
    public async Task SeparatePricedAndStockedVariants_DoNotCombineIntoEligibility()
    {
        await using var fixture = await Fixture.Create();
        var product = fixture.Products[0]; product.HasVariants = true; product.TrackStock = true;
        var priced = new ProductVariant { Id = Guid.NewGuid(), ProductId = product.Id, TenantId = product.TenantId, SKU = "PRICED", Price = 25, IsActive = true, IsDefault = true };
        var unpriced = new ProductVariant { Id = Guid.NewGuid(), ProductId = product.Id, TenantId = product.TenantId, SKU = "UNPRICED", Price = 0, IsActive = true };
        var warehouse = new Warehouse { Id = Guid.NewGuid(), TenantId = product.TenantId, Name = "QA", IsActive = true };
        fixture.Db.ProductVariants.AddRange(priced, unpriced);
        fixture.Db.Warehouses.Add(warehouse);
        fixture.Db.ProductVariantStocks.Add(new ProductVariantStock { Id = Guid.NewGuid(), TenantId = product.TenantId, VariantId = unpriced.Id, Variant = unpriced, WarehouseId = warehouse.Id, Warehouse = warehouse, Quantity = 10 });
        await fixture.Db.SaveChangesAsync();
        Assert.IsType<BadRequestObjectResult>(await fixture.Controller.Create(new() { TenantId = product.TenantId, ProductId = product.Id }, default));
        Assert.Empty(await fixture.Db.AffiliatePromotionLinks.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public void DetailVariantRecomposition_DropsStaleValuesAndInactiveVariants()
    {
        var doc = new AffiliateProductIntelligenceDocument { Variants = [new() { Price = 999, Stock = 999 }], TotalAvailableStock = 4 };
        var current = new ProductDocument { HasVariants = true, TrackStock = true, Variants = [new() { VariantId = Guid.NewGuid(), Price = 12.50m, TotalStock = 4, IsActive = true }, new() { Price = 10, TotalStock = 999, IsActive = false }] };
        AffiliatePortalController.ApplyCurrentAffiliateVariants(doc, current);
        var variant = Assert.Single(doc.Variants);
        Assert.Equal(12.50m, variant.Price);
        Assert.Equal(4, variant.Stock);
        Assert.True(doc.LowStock);
    }

    [Fact]
    public void LowStockReadback_PreservesConfiguredThresholdAndBackorderPolicy()
    {
        var doc = new AffiliateProductIntelligenceDocument { TotalAvailableStock = 15 };
        var product = new ProductDocument { TrackStock = true };
        AffiliatePortalController.ApplyCurrentAffiliateVariants(doc, product, 20);
        Assert.True(doc.LowStock);
        AffiliatePortalController.ApplyCurrentAffiliateVariants(doc, product);
        Assert.False(doc.LowStock);
        product.AllowBackorder = true;
        AffiliatePortalController.ApplyCurrentAffiliateVariants(doc, product, 20);
        Assert.False(doc.LowStock);
    }

    [Fact]
    public void DiscoveryFilteringAndPaging_UsesMerchantKeys_NotAffiliateScope()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        var docs = new[] { new AffiliateProductIntelligenceDocument { TenantId = a, ProductId = Guid.NewGuid(), ProductName = "Phone", BasePrice = 20 }, new AffiliateProductIntelligenceDocument { TenantId = b, ProductId = Guid.NewGuid(), ProductName = "PHONE", BasePrice = 30 } };
        Assert.Equal(2, AffiliatePortalController.FilterAffiliateDocuments(docs, "phone", null, null, null).Count());
        Assert.Equal(b, Assert.Single(AffiliatePortalController.FilterAffiliateDocuments(docs, "phone", null, b, null)).TenantId);
        Assert.Equal(20, AffiliatePortalController.OrderAffiliateDocuments(docs, "price_asc").First().BasePrice);
        Assert.Empty(AffiliatePortalController.FilterAffiliateDocuments(docs, null, null, Guid.NewGuid(), null));
    }

    [Theory]
    [InlineData("UQ_AffiliatePromotionLinks_User_Store_Product", true)]
    [InlineData("UQ_AffiliatePromotionLinks_ReferralCode", true)]
    [InlineData("unrelated_constraint", false)]
    public void ConflictRecovery_IsLimitedToKnownUniqueConstraints(string constraint, bool expected)
    {
        var exception = new DbUpdateException("Synthetic", new PostgresException("Synthetic", "ERROR", "ERROR", "23505", constraintName: constraint));
        Assert.Equal(expected, AffiliatePromotionLinksController.IsPromotionUniqueConflict(exception));
    }

    [Fact]
    public async Task CodeConflict_RetriesAtomicInsertWithoutOrphanReservation()
    {
        var failure = new InsertFailure(true);
        await using var fixture = await Fixture.Create(failure: failure);
        var product = fixture.Products[0];
        Assert.IsType<CreatedAtActionResult>(await fixture.Controller.Create(new() { TenantId = product.TenantId, ProductId = product.Id }, default));
        Assert.Single(await fixture.Db.AffiliatePromotionLinks.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(2, failure.LinkAttempts);
        Assert.DoesNotContain(fixture.Db.ChangeTracker.Entries<AffiliatePromotionLink>(), e => e.State == EntityState.Added);
    }

    [Fact]
    public async Task FailedPersistence_DoesNotCreateLink_AndFreshRequestCanRetry()
    {
        var failure = new InsertFailure(false);
        await using var fixture = await Fixture.Create(failure: failure);
        var product = fixture.Products[0];
        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Controller.Create(new() { TenantId = product.TenantId, ProductId = product.Id }, default));
        Assert.Empty(await fixture.Db.AffiliatePromotionLinks.IgnoreQueryFilters().ToListAsync());
        // A real failed request disposes its DbContext. Detach its uncommitted
        // entry here to emulate the fresh request without discarding persisted data.
        foreach (var entry in fixture.Db.ChangeTracker.Entries<AffiliatePromotionLink>().ToList()) entry.State = EntityState.Detached;
        Assert.IsType<CreatedAtActionResult>(await fixture.Controller.Create(new() { TenantId = product.TenantId, ProductId = product.Id }, default));
        Assert.Single(await fixture.Db.AffiliatePromotionLinks.IgnoreQueryFilters().ToListAsync());
    }

    private sealed class InsertFailure(bool unique) : SaveChangesInterceptor
    {
        public int LinkAttempts { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AffiliatePromotionLink>().Any(e => e.State == EntityState.Added) && ++LinkAttempts == 1)
                throw new DbUpdateException("Synthetic persistence failure", unique
                    ? new PostgresException("Synthetic collision", "ERROR", "ERROR", "23505", constraintName: "UQ_AffiliatePromotionLinks_ReferralCode")
                    : new InvalidOperationException("Synthetic storage outage"));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required ApplicationDbContext Db { get; init; }
        public required AffiliateUser User { get; init; }
        public required AffiliateAccount Account { get; init; }
        public required Product[] Products { get; init; }
        public required AffiliatePromotionLinksController Controller { get; init; }
        public required StorefrontUrlResolver Resolver { get; init; }

        public static async Task<Fixture> Create(AffiliateStatus status = AffiliateStatus.Active, bool verified = true, SaveChangesInterceptor? failure = null)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString());
            if (failure != null) options.AddInterceptors(failure);
            var db = new ApplicationDbContext(options.Options);
            var userId = Guid.NewGuid();
            var account = AffiliateAccount.Create(Guid.Empty, userId, "GLOBAL1234", "Global QA");
            account.Id = Guid.NewGuid();
            if (status != AffiliateStatus.Pending) account.Activate();
            if (status == AffiliateStatus.Suspended) account.Suspend("QA");
            if (status == AffiliateStatus.Closed) account.Close("QA");
            var user = AffiliateUser.Create(Guid.Empty, "qa@example.test", "hash", account.Id); user.Id = userId;
            if (verified) user.VerifyEmail();
            db.AffiliateAccounts.Add(account); db.AffiliateUsers.Add(user);
            var category = AffiliateCategory.CreateMainCategory("Electronics", "electronics");
            category.Id = Guid.NewGuid();
            db.AffiliateCategories.Add(category);
            var products = new List<Product>();
            foreach (var name in new[] { "First", "Second" })
            {
                var tenant = new Tenant { Id = Guid.NewGuid(), Name = name, IsActive = true, Status = TenantStatus.Active };
                tenant.StorefrontBaseUrl = $"https://{tenant.Id:N}.example.test";
                db.Tenants.Add(tenant);
                var product = new Product { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = name, Slug = name.ToLowerInvariant(), SKU = name, CategoryId = Guid.Empty, IsActive = true, IsOnlineVisible = true, IsSearchable = true, PublishStatus = PublishStatus.Published, TrackStock = false };
                db.Products.Add(product); products.Add(product);
                var unit = new PointOfSale.Core.Entities.Unit { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Piece", IsActive = true };
                db.Units.Add(unit);
                db.ProductUnits.Add(new ProductUnit { Id = Guid.NewGuid(), ProductId = product.Id, UnitId = unit.Id, Unit = unit, IsDefault = true, IsBaseUnit = true, SellingPrice = 25.75m, ConversionFactor = 1 });
                db.AffiliateProductPolicies.Add(AffiliateProductPolicy.Create(tenant.Id, product.Id, .1m, categoryId: category.Id));
            }
            await db.SaveChangesAsync();
            var services = new ServiceCollection().BuildServiceProvider();
            var resolver = new StorefrontUrlResolver(new ConfigurationBuilder().Build(), NullLogger<StorefrontUrlResolver>.Instance, services.GetRequiredService<IServiceScopeFactory>());
            var controller = new AffiliatePromotionLinksController(db, resolver, NullLogger<AffiliatePromotionLinksController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("affiliate_account_id", account.Id.ToString())], "Test")) } }
            };
            return new Fixture { Db = db, Account = account, User = user, Products = products.ToArray(), Controller = controller, Resolver = resolver };
        }
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
