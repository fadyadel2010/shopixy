using System.Reflection;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Models;
using PointOfSale.API.Services;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Elasticsearch.Configuration;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;

namespace PointOfSale.Tests.Affiliate;

/// <summary>Executable proof for the extracted SQL/category abstraction, not live delivery evidence.</summary>
public sealed class AffiliatePhase4ClosureAuthorityTests
{
    [Theory]
    [InlineData("active", true)]
    [InlineData("inactive-category", false)]
    [InlineData("deleted-category", false)]
    [InlineData("inactive-parent", false)]
    [InlineData("deleted-parent", false)]
    [InlineData("missing-parent", false)]
    public async Task DiscoveryAndLegacyDetail_RevalidateCurrentCategoryLineageDespiteStaleCandidate(string mutation, bool admitted)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var tenant = new Tenant { Id = Guid.NewGuid(), Name = "Closure isolated", Slug = "closure",
            Currency = "EGP", IsActive = true, Status = TenantStatus.Active, StorefrontBaseUrl = "https://closure.example.test" };
        var category = new Category { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Merchandise" };
        var parent = AffiliateCategory.CreateMainCategory("Root", "root"); parent.Id = Guid.NewGuid();
        var child = AffiliateCategory.CreateSubcategory("Child", "child", parent.Id); child.Id = Guid.NewGuid();
        var product = new Product { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Closure product", Slug = "closure-product",
            CategoryId = category.Id, IsActive = true, IsOnlineVisible = true, IsSearchable = true,
            PublishStatus = PublishStatus.Published, TrackStock = false, HasVariants = false };
        var unit = new PointOfSale.Core.Entities.Unit { Id = Guid.NewGuid(), TenantId = tenant.Id, Name = "Piece", Symbol = "pc", IsActive = true };
        db.AddRange(tenant, category, parent, child, product, unit,
            new ProductUnit { Id = Guid.NewGuid(), ProductId = product.Id, UnitId = unit.Id, Unit = unit,
                SellingPrice = 100m, ConversionFactor = 1m, IsDefault = true, IsBaseUnit = true },
            AffiliateProductPolicy.Create(tenant.Id, product.Id, .10m, categoryId: child.Id));
        await db.SaveChangesAsync();
        switch (mutation)
        {
            case "inactive-category": child.Deactivate(); break;
            case "deleted-category": child.IsDeleted = true; break;
            case "inactive-parent": parent.Deactivate(); break;
            case "deleted-parent": parent.IsDeleted = true; break;
            case "missing-parent": db.Remove(parent); break;
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var configuration = new ConfigurationBuilder().Build();
        var discovery = new AffiliateProductDiscoveryService(null!, db, configuration,
            Options.Create(new ElasticsearchSettings()), NullLogger<AffiliateProductDiscoveryService>.Instance);
        // Exercise the actual read-side recomposition used after Elastic candidate retrieval.
        // No Elastic/Mongo response can supply financial or category admission authority.
        var method = typeof(AffiliateProductDiscoveryService).GetMethod("RecomposeDiscoveryItemsFromSqlAuthorityAsync",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var items = await (Task<List<AffiliateDiscoveryItem>>)method.Invoke(discovery,
            new object?[] { new List<PublicAffiliateProductSearchDocument> { new() { TenantId = tenant.Id, ProductId = product.Id } }, null, CancellationToken.None })!;
        Assert.Equal(admitted ? 1 : 0, items.Count);

        var resolver = new StorefrontUrlResolver(configuration, NullLogger<StorefrontUrlResolver>.Instance, Mock.Of<IServiceScopeFactory>());
        var portal = new AffiliatePortalController(db, null!, null!, null!, null!, resolver, NullLogger<AffiliatePortalController>.Instance);
        var detail = await portal.GetProductDetail(product.Id, CancellationToken.None);
        if (admitted)
        {
            var value = Assert.IsType<AffiliateProductDetailResponse>(Assert.IsType<OkObjectResult>(detail.Result).Value);
            Assert.Equal(100m, value.Price);
            Assert.Equal(.10m, value.CommissionRate);
            Assert.Equal(tenant.Id, value.TenantId);
        }
        else Assert.IsType<NotFoundObjectResult>(detail.Result);
        Assert.Empty(await db.AffiliateCommissionLedger.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public void ActualAffiliateTokenGenerator_EmitsGlobalProfileClaimsWithoutTenantAuthority()
    {
        var settings = new AffiliateJwtSettings { Secret = new string('x', 64), Issuer = "closure-test",
            Audience = "closure-affiliate", AccessTokenExpirationMinutes = 5 };
        var controller = new AffiliateAuthController(null!, null!, Options.Create(settings), null!, null!,
            new ConfigurationBuilder().Build(), NullLogger<AffiliateAuthController>.Instance);
        var profile = AffiliateAccount.Create(Guid.Empty, Guid.NewGuid(), "CLOSUREQA", "Isolated closure"); profile.Id = Guid.NewGuid();
        var user = AffiliateUser.Create(Guid.Empty, "closure@example.invalid", "fixture-only", profile.Id); user.Id = profile.UserId;
        var result = ((string accessToken, string refreshToken, string refreshTokenHash))typeof(AffiliateAuthController)
            .GetMethod("GenerateTokens", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(controller, new object[] { user, profile })!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.accessToken);
        Assert.DoesNotContain(jwt.Claims, c => c.Type.Contains("tenant", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(jwt.Claims, c => c.Type == "affiliate_account_id" && c.Value == profile.Id.ToString());
        Assert.Contains(jwt.Claims, c => c.Type == "sub" && c.Value == user.Id.ToString());
        Assert.Equal("closure-test", jwt.Issuer);
        Assert.Contains("closure-affiliate", jwt.Audiences);
    }
}
