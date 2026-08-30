using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Affiliate;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Affiliate;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Seeding;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.Services.Affiliate;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests.Affiliate;

/// <summary>
/// AF-30B: Real-PostgreSQL regression matrix for the affiliate taxonomy
/// bootstrap (insert-only, transactional, multi-instance safe).
///
/// Follows the repository convention: when no disposable PostgreSQL facility is
/// available these tests return silently instead of faking PostgreSQL behavior
/// with SQLite. The main development database is never touched — every test runs
/// against an isolated disposable database created by TestDatabaseScope.
///
/// Known gap (intentional): the real HTTP/API hosting stack is not exercised;
/// the reset-recovery regression drives the real AdminAffiliateCategoryController
/// read path directly against real PostgreSQL instead.
/// </summary>
public sealed class AffiliateTaxonomySeederPostgresTests
{
    private static readonly Guid ElectronicsId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid HomeAndLivingId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid BooksAndMediaId = new("66666666-6666-6666-6666-666666666666");
    private static readonly Guid SportsAndOutdoorsId = new("55555555-5555-5555-5555-555555555555");
    private static readonly Guid WatchesId = new("22222222-2222-2222-2222-222222222227");

    [Fact]
    public async Task SeedAsync_OnEmptyDatabase_InsertsEntireBaselineWithCanonicalIdentity()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) || databaseScope is null)
        {
            return;
        }

        await using var scope = databaseScope;
        await using var context = scope.CreateDbContext();

        await CreateSeeder(context).SeedAsync();

        var rows = await context.AffiliateCategories.AsNoTracking().ToListAsync();
        Assert.Equal(AffiliateTaxonomyCatalog.Entries.Count, rows.Count);

        foreach (var entry in AffiliateTaxonomyCatalog.Entries)
        {
            var row = Assert.Single(rows.Where(candidate => candidate.Id == entry.Id));
            Assert.Equal(entry.Name, row.Name);
            Assert.Equal(entry.Slug, row.Slug);
            Assert.Equal(entry.ParentId, row.ParentId);
            Assert.Equal(entry.Level, row.Level);
            Assert.Equal(entry.DisplayOrder, row.DisplayOrder);
            Assert.True(row.IsActive);
            Assert.False(row.IsDeleted);
            Assert.Null(row.UpdatedAt);
            Assert.Null(row.CreatedBy);
        }
    }

    [Fact]
    public async Task SeedAsync_RepeatedRun_PerformsNoDataChangesAndPreservesTimestamps()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) || databaseScope is null)
        {
            return;
        }

        await using var scope = databaseScope;

        await using (var context = scope.CreateDbContext())
        {
            await CreateSeeder(context).SeedAsync();
        }

        List<CategoryRowSnapshot> firstPass;
        await using (var context = scope.CreateDbContext())
        {
            firstPass = await SnapshotAsync(context);
            Assert.Equal(AffiliateTaxonomyCatalog.Entries.Count, firstPass.Count);
        }

        await using (var context = scope.CreateDbContext())
        {
            await CreateSeeder(context).SeedAsync();
        }

        await using var verifyContext = scope.CreateDbContext();
        var secondPass = await SnapshotAsync(verifyContext);

        Assert.Equal(firstPass, secondPass);
    }

    [Fact]
    public async Task SeedAsync_ExistingModifiedBaselineRow_IsPreservedInsertOnly()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) || databaseScope is null)
        {
            return;
        }

        await using var scope = databaseScope;
        await using (var context = scope.CreateDbContext())
        {
            await CreateSeeder(context).SeedAsync();
        }

        var modifiedAt = DateTime.UtcNow;
        DateTime? updatedAtAfterEdit;
        await using (var context = scope.CreateDbContext())
        {
            // Simulate fields the existing Super Admin API may legitimately edit:
            // rename, re-slug, deactivate, and reorder.
            await context.Database.ExecuteSqlAsync(
                $"""
                UPDATE "AffiliateCategories"
                SET "Name" = 'Electronics Renamed By Operator', "Slug" = 'operator-electronics',
                    "IsActive" = FALSE, "DisplayOrder" = 99, "UpdatedAt" = {modifiedAt}
                WHERE "Id" = {ElectronicsId}
                """,
                CancellationToken.None);

            updatedAtAfterEdit = await context.AffiliateCategories
                .AsNoTracking()
                .Where(c => c.Id == ElectronicsId)
                .Select(c => c.UpdatedAt)
                .SingleAsync();
        }

        await using (var context = scope.CreateDbContext())
        {
            await CreateSeeder(context).SeedAsync();
        }

        await using var verifyContext = scope.CreateDbContext();
        var row = await verifyContext.AffiliateCategories.AsNoTracking().SingleAsync(c => c.Id == ElectronicsId);
        Assert.Equal("Electronics Renamed By Operator", row.Name);
        Assert.False(row.IsActive);
        Assert.Equal(99, row.DisplayOrder);
        // Timestamps untouched by the rerun (insert-only).
        Assert.Equal(updatedAtAfterEdit, row.UpdatedAt);
        // Canonical identity untouched even though content drifted.
        Assert.Equal(ElectronicsId, row.Id);
        Assert.Equal("operator-electronics", row.Slug);
    }

    [Fact]
    public async Task SeedAsync_UnrelatedFutureManagedCategories_ArePreserved()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) || databaseScope is null)
        {
            return;
        }

        await using var scope = databaseScope;
        await using (var context = scope.CreateDbContext())
        {
            await CreateSeeder(context).SeedAsync();
        }

        var futureRootId = Guid.NewGuid();
        await using (var context = scope.CreateDbContext())
        {
            var futureRoot = AffiliateCategory.CreateMainCategory("Future Managed", "future-managed-root", 99);
            futureRoot.Id = futureRootId;
            var futureChild = AffiliateCategory.CreateSubcategory("Future Child", "future-managed-child", futureRoot.Id, 1);
            context.AffiliateCategories.AddRange(futureRoot, futureChild);
            await context.SaveChangesAsync();
        }

        await using (var context = scope.CreateDbContext())
        {
            await CreateSeeder(context).SeedAsync();
        }

        await using var verifyContext = scope.CreateDbContext();
        var rowCount = await verifyContext.AffiliateCategories.CountAsync();
        Assert.Equal(AffiliateTaxonomyCatalog.Entries.Count + 2, rowCount);

        var preservedRoot = await verifyContext.AffiliateCategories.AsNoTracking().SingleAsync(c => c.Id == futureRootId);
        Assert.Equal("Future Managed", preservedRoot.Name);
        Assert.Equal(99, preservedRoot.DisplayOrder);

        var preservedChild = await verifyContext.AffiliateCategories
            .AsNoTracking()
            .SingleAsync(c => c.Slug == "future-managed-child" && c.ParentId == futureRootId);
        Assert.Equal(2, preservedChild.Level);
    }

    [Fact]
    public async Task SeedAsync_TenantProductPolicyCategoryReference_IsNotRewrittenOrDeleted()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        await using var scope = databaseScope;
        await using (var context = scope.CreateDbContext())
        {
            await CreateSeeder(context).SeedAsync();
        }

        var posCategoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        await using (var tenantDb = scope.CreateDbContext(tenantContext))
        {
            var utcNow = DateTime.UtcNow;
            tenantDb.Categories.Add(new Category
            {
                Id = posCategoryId,
                TenantId = tenantId,
                Name = "POS Source Category",
                CreatedAt = utcNow
            });
            tenantDb.Products.Add(new Product
            {
                Id = productId,
                TenantId = tenantId,
                Name = "Affiliate Product",
                CategoryId = posCategoryId,
                SalePrice = 100m,
                OnlinePrice = 100m,
                PurchasePrice = 60m,
                IsActive = true,
                CreatedAt = utcNow
            });
            tenantDb.AffiliateProductPolicies.Add(
                AffiliateProductPolicy.Create(tenantId, productId, 0.15m, null, WatchesId));
            await tenantDb.SaveChangesAsync();
        }

        PolicySnapshot before;
        await using (var tenantDb = scope.CreateDbContext(tenantContext))
        {
            before = await SnapshotPolicyAsync(tenantDb);
        }

        await using (var context = scope.CreateDbContext())
        {
            await CreateSeeder(context).SeedAsync();
        }

        await using var verifyTenantDb = scope.CreateDbContext(tenantContext);
        var after = await SnapshotPolicyAsync(verifyTenantDb);

        Assert.Equal(before, after);
        Assert.Equal(WatchesId, after.AffiliateCategoryId);

        var watchesStillPresent = await verifyTenantDb.AffiliateCategories
            .AsNoTracking()
            .AnyAsync(c => c.Id == WatchesId && !c.IsDeleted && c.IsActive);
        Assert.True(watchesStillPresent);
    }

    [Fact]
    public async Task SeedAsync_MissingParent_IsRestoredBeforeItsChildren()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) || databaseScope is null)
        {
            return;
        }

        await using var scope = databaseScope;
        await using (var context = scope.CreateDbContext())
        {
            await CreateSeeder(context).SeedAsync();
        }

        await using (var context = scope.CreateDbContext())
        {
            // Children first: FK_AffiliateCategories_Parent is ON DELETE RESTRICT.
            await context.Database.ExecuteSqlAsync(
                $"DELETE FROM \"AffiliateCategories\" WHERE \"ParentId\" = {HomeAndLivingId}",
                CancellationToken.None);
            await context.Database.ExecuteSqlAsync(
                $"DELETE FROM \"AffiliateCategories\" WHERE \"Id\" = {HomeAndLivingId}",
                CancellationToken.None);
        }

        await using (var context = scope.CreateDbContext())
        {
            Assert.Equal(AffiliateTaxonomyCatalog.Entries.Count - 6, await context.AffiliateCategories.CountAsync());
        }

        await using (var context = scope.CreateDbContext())
        {
            await CreateSeeder(context).SeedAsync();
        }

        await using var verifyContext = scope.CreateDbContext();
        Assert.Equal(AffiliateTaxonomyCatalog.Entries.Count, await verifyContext.AffiliateCategories.CountAsync());

        var restoredParent = await verifyContext.AffiliateCategories.AsNoTracking()
            .SingleAsync(c => c.Id == HomeAndLivingId);
        Assert.Equal(1, restoredParent.Level);
        Assert.Null(restoredParent.ParentId);

        var restoredChildren = await verifyContext.AffiliateCategories.AsNoTracking()
            .Where(c => c.ParentId == HomeAndLivingId)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync();
        Assert.Equal(5, restoredChildren.Count);
        Assert.All(restoredChildren, child => Assert.Equal(2, child.Level));
        Assert.Equal(
            new[] { "furniture", "kitchen-dining", "bedding", "home-decor", "appliances" },
            restoredChildren.Select(child => child.Slug).ToArray());
    }

    [Fact]
    public async Task SeedAsync_TwoSimultaneousSeeds_CreateNoDuplicatesOrPartialHierarchy()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) || databaseScope is null)
        {
            return;
        }

        await using var scope = databaseScope;

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var firstTask = RunConcurrentSeedAsync(scope, startGate.Task);
        var secondTask = RunConcurrentSeedAsync(scope, startGate.Task);

        startGate.SetResult();
        await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(120));

        await using var verifyContext = scope.CreateDbContext();
        var rows = await verifyContext.AffiliateCategories.AsNoTracking().ToListAsync();

        Assert.Equal(AffiliateTaxonomyCatalog.Entries.Count, rows.Count);
        Assert.Equal(AffiliateTaxonomyCatalog.Entries.Count, rows.Select(row => row.Id).Distinct().Count());
        Assert.Equal(AffiliateTaxonomyCatalog.Entries.Count, rows.Select(row => row.Slug).Distinct().Count());

        var levelOneIds = rows.Where(row => row.Level == 1).Select(row => row.Id).ToHashSet();
        Assert.All(rows.Where(row => row.Level == 2), child => Assert.Contains(child.ParentId!.Value, levelOneIds));
    }

    [Fact]
    public async Task SeedAsync_CanonicalSlugCollision_FailsVisiblyAndCommitsNothing()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) || databaseScope is null)
        {
            return;
        }

        await using var scope = databaseScope;
        var rogueId = Guid.NewGuid();

        await using (var context = scope.CreateDbContext())
        {
            // A foreign row squats the canonical slug 'electronics' with another identity.
            await context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "AffiliateCategories"
                    ("Id", "Name", "Slug", "ParentId", "Level", "IsActive", "IsDeleted", "DisplayOrder", "CreatedAt")
                VALUES
                    ({rogueId}, 'Rogue Electronics', 'electronics', NULL, 1, TRUE, FALSE, 1, {DateTime.UtcNow})
                """,
                CancellationToken.None);
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunStartupInitializerAsync(scope));

        Assert.Contains("'electronics'", exception.Message);

        await using var verifyContext = scope.CreateDbContext();
        var remainingRows = await verifyContext.AffiliateCategories.AsNoTracking().ToListAsync();

        // Atomicity: none of the other 32 canonical inserts leaked past the failure.
        var row = Assert.Single(remainingRows);
        Assert.Equal(rogueId, row.Id);
        Assert.DoesNotContain(remainingRows, candidate => candidate.Id == ElectronicsId);
        Assert.DoesNotContain(remainingRows, candidate => candidate.Slug == "mobile-phones");
    }

    [Fact]
    public async Task SeedAsync_ResetRecovery_RestoresSameIdsCountOrder_AndKeepsPolicySelections()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        await using var scope = databaseScope;

        // Disposable clean DB -> migrations/schema (done by the scope) -> initializer.
        await RunStartupInitializerAsync(scope);

        var baselineTree = await ReadCategoryTreeAsync(scope);
        Assert.Equal(8, baselineTree.Count);
        Assert.Equal(AffiliateTaxonomyCatalog.Entries.Count, Flatten(baselineTree).Count);

        // Tenant product policy selects a baseline category.
        var posCategoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await using (var tenantDb = scope.CreateDbContext(tenantContext))
        {
            var utcNow = DateTime.UtcNow;
            tenantDb.Categories.Add(new Category
            {
                Id = posCategoryId,
                TenantId = tenantId,
                Name = "POS Source Category",
                CreatedAt = utcNow
            });
            tenantDb.Products.Add(new Product
            {
                Id = productId,
                TenantId = tenantId,
                Name = "Affiliate Product",
                CategoryId = posCategoryId,
                SalePrice = 80m,
                OnlinePrice = 80m,
                PurchasePrice = 40m,
                IsActive = true,
                CreatedAt = utcNow
            });
            tenantDb.AffiliateProductPolicies.Add(
                AffiliateProductPolicy.Create(tenantId, productId, 0.20m, null, WatchesId));
            await tenantDb.SaveChangesAsync();
        }

        // Operational drift: lose a whole subtree plus one childless root.
        await using (var context = scope.CreateDbContext())
        {
            await context.Database.ExecuteSqlAsync(
                $"DELETE FROM \"AffiliateCategories\" WHERE \"ParentId\" = {SportsAndOutdoorsId}",
                CancellationToken.None);
            await context.Database.ExecuteSqlAsync(
                $"DELETE FROM \"AffiliateCategories\" WHERE \"Id\" IN ({SportsAndOutdoorsId}, {BooksAndMediaId})",
                CancellationToken.None);
        }

        // Rerun initializer -> recovery, then rerun again for stability.
        for (var pass = 0; pass < 2; pass++)
        {
            await RunStartupInitializerAsync(scope);
        }

        var recoveredTree = await ReadCategoryTreeAsync(scope);
        Assert.Equal(Flatten(baselineTree), Flatten(recoveredTree));

        await using var verifyTenantDb = scope.CreateDbContext(tenantContext);
        var policy = await verifyTenantDb.AffiliateProductPolicies.AsNoTracking()
            .SingleAsync(p => p.ProductId == productId);
        Assert.Equal(WatchesId, policy.AffiliateCategoryId);
        Assert.True(policy.IsEnabled);

        var watches = await verifyTenantDb.AffiliateCategories.AsNoTracking()
            .SingleAsync(c => c.Id == WatchesId);
        Assert.True(watches.IsActive);
        Assert.False(watches.IsDeleted);
    }

    [Fact]
    public async Task SeedAsync_TaxonomyRemainsGlobal_NoTenantScopedRowsWritten()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out _, out _) || databaseScope is null)
        {
            return;
        }

        await using var scope = databaseScope;
        await using var context = scope.CreateDbContext();

        await CreateSeeder(context).SeedAsync();

        await using var verifyContext = scope.CreateDbContext();
        // The AffiliateCategories table carries no tenant column ownership: the
        // entity is not ITenantOwned (asserted in AffiliateTaxonomyCatalogTests),
        // and here we prove the seeded rows are readable from a context with no
        // tenant resolution at all (plain scope context, no tenant filter).
        var rowCount = await verifyContext.AffiliateCategories.IgnoreQueryFilters().CountAsync();
        Assert.Equal(AffiliateTaxonomyCatalog.Entries.Count, rowCount);
    }

    [Fact]
    public async Task ProductCategorySelection_RejectsActiveChildUnderInactiveParent()
    {
        if (!RealPostgresTestSupport.TryCreateScope(out var databaseScope, out var tenantId, out var tenantContext) ||
            databaseScope is null ||
            tenantContext is null)
        {
            return;
        }

        await using var scope = databaseScope;
        await using (var context = scope.CreateDbContext())
        {
            await CreateSeeder(context).SeedAsync();
            var fashion = await context.AffiliateCategories.SingleAsync(category => category.Id == new Guid("22222222-2222-2222-2222-222222222222"));
            fashion.Deactivate();
            await context.SaveChangesAsync();
        }

        var posCategoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        await using var tenantDb = scope.CreateDbContext(tenantContext);
        var utcNow = DateTime.UtcNow;
        tenantDb.Categories.Add(new Category
        {
            Id = posCategoryId,
            TenantId = tenantId,
            Name = "POS Source Category",
            CreatedAt = utcNow
        });
        tenantDb.Products.Add(new Product
        {
            Id = productId,
            TenantId = tenantId,
            Name = "Affiliate Product",
            CategoryId = posCategoryId,
            SalePrice = 100m,
            OnlinePrice = 100m,
            PurchasePrice = 60m,
            IsActive = true,
            CreatedAt = utcNow
        });
        tenantDb.AffiliateProductPolicies.Add(
            AffiliateProductPolicy.Create(tenantId, productId, 0.15m, null, WatchesId));
        await tenantDb.SaveChangesAsync();

        var service = new AffiliateService(
            tenantDb,
            tenantContext,
            new Mock<IEventPublisher>().Object,
            new AffiliateTelemetry(),
            NullLogger<AffiliateService>.Instance,
            new Mock<IAffiliatePayoutService>().Object,
            new Mock<IAffiliateSettlementOrchestrator>().Object,
            Options.Create(new AffiliateSettlementOptions()));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.UpdateCategoryForProductAsync(productId, WatchesId));

        Assert.Contains("hierarchy is inactive or invalid", exception.Message);
    }

    private static AffiliateTaxonomySeeder CreateSeeder(ApplicationDbContext context) =>
        new(context, NullLogger<AffiliateTaxonomySeeder>.Instance);

    private static async Task RunStartupInitializerAsync(TestDatabaseScope scope)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ApplicationDbContext>(_ => scope.CreateDbContext());
        services.AddAffiliateTaxonomyBootstrap();

        await using var provider = services.BuildServiceProvider();
        var initializer = provider.GetServices<IHostedService>()
            .OfType<AffiliateTaxonomyStartupInitializer>()
            .Single();

        await initializer.StartAsync(CancellationToken.None);
    }

    private static async Task RunConcurrentSeedAsync(TestDatabaseScope scope, Task startGate)
    {
        await startGate;

        await using var context = scope.CreateDbContext();
        await CreateSeeder(context).SeedAsync();
    }

    private static async Task<List<CategoryTreeNode>> ReadCategoryTreeAsync(TestDatabaseScope scope)
    {
        await using var context = scope.CreateDbContext();
        var controller = new AdminAffiliateCategoryController(
            context,
            NullLogger<AdminAffiliateCategoryController>.Instance,
            new Mock<IEventPublisher>().Object);

        var actionResult = await controller.GetCategoryTree(CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        return Assert.IsType<List<CategoryTreeNode>>(okResult.Value);
    }

    private static List<(Guid Id, Guid? ParentId)> Flatten(List<CategoryTreeNode> tree) =>
        tree
            .SelectMany(root => new[] { (root.Id, (Guid?)null) }
                .Concat(root.Subcategories.Select(sub => (sub.Id, (Guid?)root.Id))))
            .ToList();

    private static async Task<List<CategoryRowSnapshot>> SnapshotAsync(ApplicationDbContext context) =>
        await context.AffiliateCategories
            .AsNoTracking()
            .OrderBy(row => row.Id)
            .Select(row => new CategoryRowSnapshot(
                row.Id,
                row.Name,
                row.Slug,
                row.ParentId,
                row.Level,
                row.IsActive,
                row.DisplayOrder,
                row.IsDeleted,
                row.CreatedAt,
                row.UpdatedAt))
            .ToListAsync();

    private static async Task<PolicySnapshot> SnapshotPolicyAsync(ApplicationDbContext context)
    {
        var policy = await context.AffiliateProductPolicies.AsNoTracking().SingleAsync();
        return new PolicySnapshot(
            policy.Id,
            policy.TenantId,
            policy.ProductId,
            policy.CommissionRate,
            policy.IsEnabled,
            policy.TemplateType,
            policy.AffiliateCategoryId,
            policy.IsDeleted,
            policy.CreatedAt,
            policy.UpdatedAt);
    }

    private sealed record CategoryRowSnapshot(
        Guid Id,
        string Name,
        string Slug,
        Guid? ParentId,
        int Level,
        bool IsActive,
        int DisplayOrder,
        bool IsDeleted,
        DateTime CreatedAt,
        DateTime? UpdatedAt);

    private sealed record PolicySnapshot(
        Guid Id,
        Guid TenantId,
        Guid ProductId,
        decimal CommissionRate,
        bool IsEnabled,
        string TemplateType,
        Guid? AffiliateCategoryId,
        bool IsDeleted,
        DateTime CreatedAt,
        DateTime? UpdatedAt);
}
