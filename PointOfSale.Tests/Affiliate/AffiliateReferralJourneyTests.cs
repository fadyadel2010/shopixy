using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Helpers;
using PointOfSale.API.Middleware;
using PointOfSale.API.Security;
using PointOfSale.API.Services.Affiliate;
using PointOfSale.API.Services.Checkout;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events.Affiliate;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.MultiTenancy;

namespace PointOfSale.Tests.Affiliate;

public sealed class AffiliateReferralJourneyTests
{
    [Theory]
    [InlineData("id")]
    [InlineData("slug")]
    [InlineData("legacy-landing")]
    public async Task MatchingProduct_CapturesCanonicalGlobalIdentityAndProtectedEvidence(string entry)
    {
        await using var f = await Fixture.Create();
        var path = entry switch { "slug" => "/api/storefront/products/first", "legacy-landing" => "/api/storefront/landing/LINKFIRST1/first", _ => f.ProductPath };
        var c = await f.Request(path, " linkfirst1 ");
        Assert.Equal(f.Link.Id, AffiliateModeResolver.GetPromotionLinkId(c));
        Assert.Equal(f.Account.Id, AffiliateModeResolver.GetAffiliateAccountId(c));
        Assert.Equal(f.User.Id, AffiliateModeResolver.GetAffiliateUserId(c));
        Assert.True(f.Tokens.TryValidate(f.Cookie(c), f.Product.TenantId, out var evidence));
        Assert.Equal(f.Link.Id, evidence.PromotionLinkId);
        Assert.Equal("LINKFIRST1", c.Response.Headers["X-Affiliate-Ref"]);
        Assert.Contains("httponly", c.Response.Headers.SetCookie.ToString(), StringComparison.OrdinalIgnoreCase);
        var click = Assert.Single(f.Clicks);
        Assert.Equal(f.Product.Id, click.ProductId);
        Assert.Equal(f.Product.TenantId, click.TenantId);
        Assert.Empty(await f.Db.AffiliateCommissionLedger.ToListAsync());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("unverified")]
    [InlineData("credential-deleted")]
    [InlineData("bridge-mismatch")]
    [InlineData("owner-mismatch")]
    [InlineData("suspended")]
    [InlineData("product-unpublished")]
    [InlineData("product-unsearchable")]
    [InlineData("policy-disabled")]
    [InlineData("category-inactive")]
    [InlineData("merchant-inactive")]
    [InlineData("profile-code")]
    [InlineData("malformed")]
    public async Task InvalidAuthority_NeverCapturesOrPublishes(string state)
    {
        await using var f = await Fixture.Create();
        var code = "LINKFIRST1";
        switch (state)
        {
            case "unknown": code = "UNKNOWN"; break;
            case "inactive": f.Link.Deactivate(); break;
            case "deleted": f.Link.IsDeleted = true; break;
            case "unverified": f.User.SetEmail("changed@example.test"); break;
            case "credential-deleted": f.User.IsDeleted = true; break;
            case "bridge-mismatch": typeof(AffiliatePromotionLink).GetProperty(nameof(AffiliatePromotionLink.AffiliateAccountId))!.SetValue(f.Link, Guid.NewGuid()); break;
            case "owner-mismatch": typeof(AffiliateAccount).GetProperty(nameof(AffiliateAccount.UserId))!.SetValue(f.Account, Guid.NewGuid()); break;
            case "suspended": f.Account.Suspend("QA"); break;
            case "product-unpublished": f.Product.PublishStatus = PublishStatus.Draft; break;
            case "product-unsearchable": f.Product.IsSearchable = false; break;
            case "policy-disabled": f.Policy.Disable(); break;
            case "category-inactive": f.Category.Deactivate(); break;
            case "merchant-inactive": f.Merchant.IsActive = false; break;
            case "profile-code": code = f.Account.ReferralCode; break;
            case "malformed": code = "<script>"; break;
        }
        if (state == "owner-mismatch")
        {
            // Existing canonical identity guards already prevent persisting this attack.
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
            Assert.Empty(f.Clicks);
            return;
        }
        await f.Db.SaveChangesAsync();
        var c = await f.Request(f.ProductPath, code);
        Assert.False(AffiliateModeResolver.IsAffiliateMode(c));
        Assert.Empty(f.Clicks);
        Assert.DoesNotContain("AFF_ATTR=", c.Response.Headers.SetCookie.ToString());
        Assert.Equal(1, f.DownstreamCalls);
    }

    [Theory]
    [InlineData("/api/storefront/store-config")]
    [InlineData("/api/storefront/categories")]
    [InlineData("/api/storefront/products/other-product")]
    [InlineData("/api/storefront/checkout")]
    [InlineData("/api/admin/orders")]
    public async Task NewCandidate_OnUnrelatedRequestCannotCapture(string path)
    {
        await using var f = await Fixture.Create();
        var c = await f.Request(path, "LINKFIRST1");
        Assert.False(AffiliateModeResolver.IsAffiliateMode(c));
        Assert.Empty(f.Clicks);
    }

    [Fact]
    public async Task FirstValidTouch_SurvivesNavigationCheckoutAndForeignOrInvalidHints()
    {
        await using var f = await Fixture.Create();
        var initial = await f.Request(f.ProductPath, "LINKFIRST1");
        var cookie = f.Cookie(initial);
        foreach (var path in new[] { "/api/storefront/categories", "/api/storefront/cart", "/api/storefront/checkout-session", "/api/storefront/auth/login", "/api/storefront/checkout" })
        {
            var c = await f.Request(path, "OTHERLINK2", cookie);
            Assert.Equal(f.Link.Id, AffiliateModeResolver.GetPromotionLinkId(c));
            Assert.Equal(f.Account.Id, AffiliateModeResolver.GetAffiliateAccountId(c));
            Assert.DoesNotContain("AFF_ATTR=", c.Response.Headers.SetCookie.ToString());
        }
        var refresh = await f.Request("/api/storefront/cart", null, cookie);
        Assert.Equal(f.Link.Id, AffiliateModeResolver.GetPromotionLinkId(refresh));
        var foreign = await f.Request("/api/storefront/cart", "LINKFIRST1", cookie, Guid.NewGuid());
        Assert.False(AffiliateModeResolver.IsAffiliateMode(foreign));
    }

    [Fact]
    public async Task TamperedCookie_CanBeReplacedOnValidProductLanding_NotCheckout()
    {
        await using var f = await Fixture.Create();
        var checkout = await f.Request("/api/storefront/checkout", "LINKFIRST1", "tampered");
        Assert.False(AffiliateModeResolver.IsAffiliateMode(checkout));
        var product = await f.Request(f.ProductPath, "LINKFIRST1", "tampered");
        Assert.True(f.Tokens.TryValidate(f.Cookie(product), f.Product.TenantId, out _));
    }

    [Fact]
    public async Task InactiveCapturedLink_ClearsEvidenceAndCannotBeSubstituted()
    {
        await using var f = await Fixture.Create();
        var initial = await f.Request(f.ProductPath, "LINKFIRST1");
        f.Link.Deactivate(); await f.Db.SaveChangesAsync();
        var c = await f.Request("/api/storefront/checkout", "OTHERLINK2", f.Cookie(initial));
        Assert.False(AffiliateModeResolver.IsAffiliateMode(c));
        Assert.Contains("AFF_ATTR=;", c.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task ParallelInitialRequests_ReuseShoppingSessionForExistingClickDeduplication()
    {
        await using var f = await Fixture.Create();
        await f.Request(f.ProductPath, "LINKFIRST1");
        await f.Request(f.ProductPath, "LINKFIRST1");
        Assert.Equal(2, f.Clicks.Count);
        Assert.Single(f.Clicks.Select(c => (c.TenantId, c.SessionId, c.ProductId)).Distinct());
    }

    [Fact]
    public async Task ValidationInfrastructureFailure_OnCheckoutIsRetryableWithoutOrderCreation()
    {
        await using var f = await Fixture.Create();
        await f.Db.DisposeAsync();
        var c = await f.Request("/api/storefront/checkout", "LINKFIRST1", method: "POST");
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, c.Response.StatusCode);
        Assert.Equal(0, f.DownstreamCalls);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task OrderSnapshotAndCanonicalAttribution_RoundTripAndCannotBeReassigned(bool attributed, bool authenticated, bool synchronous)
    {
        await using var f = await Fixture.Create();
        var order = new Order { Id = Guid.NewGuid(), TenantId = f.Product.TenantId, OrderNumber = "QA-P3", Total = 25.75m };
        var affiliateIdentity = await f.Db.CanonicalIdentityLinks.IgnoreQueryFilters().SingleAsync(x =>
            x.SubjectType == CanonicalIdentitySubjectType.AffiliateAccount && x.SubjectId == f.Account.Id);
        var affiliateCanonicalId = affiliateIdentity.CanonicalIdentityId;
        var buyerCanonicalId = Guid.NewGuid();
        if (authenticated)
        {
            order.OnlineCustomerId = Guid.NewGuid();
            f.Db.CanonicalIdentityLinks.Add(CanonicalIdentityLink.Create(buyerCanonicalId,
                CanonicalIdentitySubjectType.OnlineCustomer, order.OnlineCustomerId.Value, order.TenantId, "Storefront", "qa-buyer"));
            await f.Db.SaveChangesAsync();
        }
        if (attributed)
        {
            var c = await f.Request(f.ProductPath, "LINKFIRST1");
            order.AffiliateAccountId = AffiliateModeResolver.GetAffiliateAccountId(c);
            order.AffiliateReferralCode = AffiliateModeResolver.GetReferralCode(c);
            order.PromotionLinkId = AffiliateModeResolver.GetPromotionLinkId(c);
            f.Db.AffiliateAttributions.Add(AffiliateAttribution.CreateV2(order.TenantId, order.Id, order.AffiliateAccountId!.Value,
                order.AffiliateReferralCode!, AttributionSource.ReferralLink, order.PromotionLinkId!.Value));
        }
        f.Db.Orders.Add(order);
        if (synchronous) f.Db.SaveChanges();
        else await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        var saved = await f.Db.Orders.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(25.75m, saved.Total);
        var records = await f.Db.AffiliateAttributions.IgnoreQueryFilters().ToListAsync();
        if (attributed) Assert.Equal(saved.PromotionLinkId, Assert.Single(records).PromotionLinkId);
        else Assert.Empty(records);
        Assert.Equal(affiliateCanonicalId, (await f.Db.CanonicalIdentityLinks.IgnoreQueryFilters().SingleAsync(x =>
            x.SubjectType == CanonicalIdentitySubjectType.AffiliateAccount && x.SubjectId == f.Account.Id)).CanonicalIdentityId);
        if (authenticated)
        {
            Assert.Equal(buyerCanonicalId, (await f.Db.CanonicalIdentityLinks.IgnoreQueryFilters().SingleAsync(x =>
                x.SubjectType == CanonicalIdentitySubjectType.OnlineCustomer && x.SubjectId == saved.OnlineCustomerId)).CanonicalIdentityId);
            Assert.NotEqual(buyerCanonicalId, affiliateCanonicalId);
        }
        Assert.Empty(await f.Db.AffiliateCommissionLedger.IgnoreQueryFilters().ToListAsync());
        saved.PromotionLinkId = Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task FinalCheckoutRevalidation_RejectsCredentialBridgeChangedAfterCapture()
    {
        await using var f = await Fixture.Create();
        var checkout = (CheckoutService)RuntimeHelpers.GetUninitializedObject(typeof(CheckoutService));
        typeof(CheckoutService).GetField("_dbContext", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(checkout, f.Db);
        var method = typeof(CheckoutService).GetMethod("ResolveMiddlewareAffiliateAuthorityAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        async Task<object?> Resolve()
        {
            var task = (Task)method.Invoke(checkout, [f.Product.TenantId, f.Account.Id, f.Link.Id, new[] { f.Product.Id }, CancellationToken.None])!;
            await task;
            return task.GetType().GetProperty("Result")!.GetValue(task);
        }
        Assert.NotNull(await Resolve());
        f.User.SetEmail("changed@example.test"); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<CheckoutException>(Resolve);
    }

    [Theory]
    [InlineData("link-inactive")]
    [InlineData("credential-unverified")]
    [InlineData("profile-suspended")]
    [InlineData("bridge-mismatch")]
    [InlineData("product-hidden")]
    [InlineData("category-inactive")]
    [InlineData("policy-disabled")]
    [InlineData("merchant-inactive")]
    public async Task FinalTransaction_RevalidatesCurrentAuthorityWithoutCreatingOrdersOrMoney(string state)
    {
        await using var f = await Fixture.Create();
        var checkout = (CheckoutService)RuntimeHelpers.GetUninitializedObject(typeof(CheckoutService));
        typeof(CheckoutService).GetField("_dbContext", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(checkout, f.Db);
        var resolve = typeof(CheckoutService).GetMethod("ResolveMiddlewareAffiliateAuthorityAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var resolvedTask = (Task)resolve.Invoke(checkout, [f.Product.TenantId, f.Account.Id, f.Link.Id, new[] { f.Product.Id }, CancellationToken.None])!;
        await resolvedTask;
        var authority = resolvedTask.GetType().GetProperty("Result")!.GetValue(resolvedTask);
        var final = typeof(CheckoutService).GetMethod("RevalidateResolvedAffiliateAuthorityInsideFinalTransactionAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Task Revalidate() => (Task)final.Invoke(checkout, [f.Product.TenantId, authority, new[] { f.Product.Id }, CancellationToken.None])!;
        await Revalidate();
        switch (state)
        {
            case "link-inactive": f.Link.Deactivate(); break;
            case "credential-unverified": f.User.SetEmail("changed@example.test"); break;
            case "profile-suspended": f.Account.Suspend("QA"); break;
            case "bridge-mismatch": typeof(AffiliatePromotionLink).GetProperty(nameof(AffiliatePromotionLink.AffiliateAccountId))!.SetValue(f.Link, Guid.NewGuid()); break;
            case "product-hidden": f.Product.IsOnlineVisible = false; break;
            case "category-inactive": f.Category.Deactivate(); break;
            case "policy-disabled": f.Policy.Disable(); break;
            case "merchant-inactive": f.Merchant.IsActive = false; break;
        }
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<CheckoutException>(Revalidate);
        Assert.Empty(await f.Db.Orders.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await f.Db.AffiliateAttributions.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await f.Db.AffiliateCommissionLedger.IgnoreQueryFilters().ToListAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required ApplicationDbContext Db { get; init; }
        public required AffiliateAccount Account { get; init; }
        public required AffiliateUser User { get; init; }
        public required Product Product { get; init; }
        public required Tenant Merchant { get; init; }
        public required AffiliatePromotionLink Link { get; init; }
        public required AffiliateProductPolicy Policy { get; init; }
        public required AffiliateCategory Category { get; init; }
        public IAffiliateAttributionEvidenceTokenService Tokens { get; } = new AffiliateAttributionEvidenceTokenService(new EphemeralDataProtectionProvider(), TimeProvider.System, NullLogger<AffiliateAttributionEvidenceTokenService>.Instance);
        public List<AffiliateLinkClickedEvent> Clicks { get; } = [];
        public int DownstreamCalls { get; private set; }
        private readonly Guid _shoppingSession = Guid.NewGuid();
        public string ProductPath => $"/api/storefront/products/by-id/{Product.Id}";

        public static async Task<Fixture> Create()
        {
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            var userId = Guid.NewGuid();
            var account = AffiliateAccount.Create(Guid.Empty, userId, "GLOBALCODE", "QA"); account.Id = Guid.NewGuid(); account.Activate();
            var user = AffiliateUser.Create(Guid.Empty, "qa@example.test", "hash", account.Id); user.Id = userId; user.VerifyEmail();
            var merchant = new Tenant { Id = Guid.NewGuid(), Name = "Merchant", IsActive = true, Status = TenantStatus.Active };
            var category = AffiliateCategory.CreateMainCategory("Electronics", "electronics"); category.Id = Guid.NewGuid();
            var product = new Product { Id = Guid.NewGuid(), TenantId = merchant.Id, Name = "First", SKU = "FIRST", Slug = "first", IsActive = true, PublishStatus = PublishStatus.Published, IsOnlineVisible = true, IsSearchable = true, TrackStock = false };
            var unit = new PointOfSale.Core.Entities.Unit { Id = Guid.NewGuid(), TenantId = merchant.Id, Name = "Piece", IsActive = true };
            var policy = AffiliateProductPolicy.Create(merchant.Id, product.Id, .1m, categoryId: category.Id);
            var link = AffiliatePromotionLink.Create(merchant.Id, userId, product.Id, "LINKFIRST1", account.Id); link.Id = Guid.NewGuid();
            db.AddRange(account, user, merchant, category, product, unit, policy, link,
                new ProductUnit { Id = Guid.NewGuid(), ProductId = product.Id, UnitId = unit.Id, Unit = unit, IsDefault = true, IsBaseUnit = true, SellingPrice = 25.75m, ConversionFactor = 1 });
            await db.SaveChangesAsync();
            return new Fixture { Db = db, Account = account, User = user, Merchant = merchant, Product = product, Policy = policy, Category = category, Link = link };
        }

        public async Task<HttpContext> Request(string path, string? code, string? cookie = null, Guid? tenant = null, string method = "GET")
        {
            var c = new DefaultHttpContext(); c.Request.Path = path; c.Request.Method = method; c.Response.Body = new MemoryStream();
            if (code != null) c.Request.Headers["X-Affiliate-Ref"] = code;
            if (cookie != null) c.Request.Headers.Cookie = "AFF_ATTR=" + cookie;
            c.Request.Headers["X-Session-Id"] = _shoppingSession.ToString();
            var tenantContext = new TenantContext(); tenantContext.SetTenantId(tenant ?? Product.TenantId);
            var publisher = new Mock<IEventPublisher>();
            publisher.Setup(p => p.PublishAsync(It.IsAny<AffiliateLinkClickedEvent>(), It.IsAny<CancellationToken>()))
                .Callback<AffiliateLinkClickedEvent, CancellationToken>((e, _) => Clicks.Add(e)).Returns(Task.CompletedTask);
            var middleware = new AffiliateReferralMiddleware(_ => { DownstreamCalls++; return Task.CompletedTask; }, NullLogger<AffiliateReferralMiddleware>.Instance);
            await middleware.InvokeAsync(c, tenantContext, Db, publisher.Object, Mock.Of<IAffiliateModeMetricsService>(), Tokens);
            return c;
        }
        public string Cookie(HttpContext c) => c.Response.Headers.SetCookie.First(x => x!.StartsWith("AFF_ATTR=") && !x.StartsWith("AFF_ATTR=;"))!.Split(';')[0]["AFF_ATTR=".Length..];
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }
}
