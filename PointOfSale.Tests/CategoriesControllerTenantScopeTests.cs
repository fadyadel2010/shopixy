using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.Infrastructure;

namespace PointOfSale.Tests;

public sealed class CategoriesControllerTenantScopeTests
{
    [Fact]
    public async Task GetCategory_ForeignTenantId_ReturnsNotFound()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var foreignCategoryId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Categories.Add(new Category
        {
            Id = foreignCategoryId,
            TenantId = foreignTenantId,
            Name = "Foreign",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, tenantId);

        var result = await controller.GetCategory(foreignCategoryId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task UpdateCategory_ForeignTenantId_ReturnsNotFoundWithoutMutation()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();
        var foreignCategoryId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Categories.Add(new Category
        {
            Id = foreignCategoryId,
            TenantId = foreignTenantId,
            Name = "Foreign",
            Description = "Original",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, tenantId);

        var result = await controller.UpdateCategory(
            foreignCategoryId,
            new UpdateCategoryRequest("Mutated", "Changed", null),
            CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);

        var stored = await dbContext.Categories.IgnoreQueryFilters()
            .SingleAsync(c => c.Id == foreignCategoryId);
        Assert.Equal("Foreign", stored.Name);
        Assert.Equal("Original", stored.Description);
    }

    [Fact]
    public async Task GetCategories_WhenAnotherTenantHasCategories_SeedsDefaultsOnlyForCurrentTenant()
    {
        var tenantId = Guid.NewGuid();
        var foreignTenantId = Guid.NewGuid();

        await using var dbContext = CreateDbContext(tenantId);
        dbContext.Categories.Add(new Category
        {
            Id = Guid.NewGuid(),
            TenantId = foreignTenantId,
            Name = "Foreign",
            CreatedAt = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, tenantId);

        var result = await controller.GetCategories(false, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var categories = Assert.IsAssignableFrom<IEnumerable<CategoryDto>>(ok.Value);
        Assert.Equal(2, categories.Count());

        var allCategories = await dbContext.Categories.IgnoreQueryFilters()
            .OrderBy(c => c.Name)
            .ToListAsync();

        Assert.Equal(3, allCategories.Count);
        Assert.Single(allCategories.Where(c => c.TenantId == foreignTenantId));
        Assert.Equal(2, allCategories.Count(c => c.TenantId == tenantId));
        Assert.Contains(allCategories, c => c.TenantId == tenantId && c.Name == "General");
        Assert.Contains(allCategories, c => c.TenantId == tenantId && c.Name == "Uncategorized");
    }

    private static ApplicationDbContext CreateDbContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new TestTenantContext(tenantId));

    private static CategoriesController CreateController(ApplicationDbContext dbContext, Guid tenantId) =>
        new(
            dbContext,
            new TestTenantContext(tenantId),
            NullLogger<CategoriesController>.Instance);
}
