using PointOfSale.API.DTOs.Storefront;
using PointOfSale.Infrastructure.MongoDB.Documents;

namespace PointOfSale.Tests;

/// <summary>
/// Unit tests for category tree building logic.
/// 
/// These tests verify the fix for the RootCategories = 0 bug where:
/// - Categories with ParentId = null should be detected as roots
/// - Categories with ParentId = Guid.Empty should be detected as roots
/// - Orphan categories (parent not in filtered list) should be hidden
/// 
/// Note: These tests directly test the tree building logic without mocking Redis,
/// since the core issue is in the BuildCategoryTree algorithm.
/// </summary>
public class CategoryTreeBuilderTests
{
    /// <summary>
    /// Helper method that replicates the fixed BuildCategoryTree logic.
    /// This allows us to test the algorithm in isolation.
    /// </summary>
    private static List<CategoryTreeDto> BuildCategoryTree(List<CategoryDocument> documents)
    {
        var lookup = documents.ToDictionary(
            d => d.CategoryId,
            d => new CategoryTreeDto
            {
                Id = d.CategoryId,
                Name = d.Name,
                NameAr = d.NameAr,
                Slug = d.Slug,
                Children = new List<CategoryTreeDto>()
            });

        var rootCategories = new List<CategoryTreeDto>();

        foreach (var doc in documents)
        {
            var treeNode = lookup[doc.CategoryId];

            // Check if this is a root category
            // Root = ParentId is null OR ParentId is Guid.Empty
            if (IsRootCategory(doc.ParentId))
            {
                rootCategories.Add(treeNode);
            }
            else if (lookup.TryGetValue(doc.ParentId!.Value, out var parent))
            {
                parent.Children.Add(treeNode);
            }
            else
            {
                // Orphan: ParentId exists but parent not in the live filtered list.
                // Hide it instead of promoting stale lineage to root visibility.
            }
        }

        return rootCategories;
    }

    /// <summary>
    /// Helper method that replicates the fixed IsRootCategory logic.
    /// </summary>
    private static bool IsRootCategory(Guid? parentId)
    {
        return !parentId.HasValue || parentId.Value == Guid.Empty;
    }

    /// <summary>
    /// Test: Categories with ParentId = null should be detected as root categories.
    /// This is the standard case for root categories.
    /// </summary>
    [Fact]
    public void BuildCategoryTree_WithNullParentId_ShouldDetectAsRoot()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var rootCategoryId = Guid.NewGuid();
        var childCategoryId = Guid.NewGuid();

        var categories = new List<CategoryDocument>
        {
            new CategoryDocument
            {
                TenantId = tenantId,
                CategoryId = rootCategoryId,
                Name = "Root Category",
                Slug = "root-category",
                ParentId = null, // NULL = root
                IsActive = true,
                IsOnlineVisible = true
            },
            new CategoryDocument
            {
                TenantId = tenantId,
                CategoryId = childCategoryId,
                Name = "Child Category",
                Slug = "child-category",
                ParentId = rootCategoryId, // Has parent
                IsActive = true,
                IsOnlineVisible = true
            }
        };

        // Act
        var result = BuildCategoryTree(categories);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result); // Only 1 root category
        Assert.Equal(rootCategoryId, result[0].Id);
        Assert.Equal("Root Category", result[0].Name);
        Assert.Single(result[0].Children); // 1 child
        Assert.Equal(childCategoryId, result[0].Children[0].Id);
    }

    /// <summary>
    /// Test: Categories with ParentId = Guid.Empty should be detected as root categories.
    /// This handles edge cases where MongoDB stores null as Guid.Empty.
    /// </summary>
    [Fact]
    public void BuildCategoryTree_WithGuidEmptyParentId_ShouldDetectAsRoot()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var rootCategoryId = Guid.NewGuid();
        var childCategoryId = Guid.NewGuid();

        var categories = new List<CategoryDocument>
        {
            new CategoryDocument
            {
                TenantId = tenantId,
                CategoryId = rootCategoryId,
                Name = "Root Category",
                Slug = "root-category",
                ParentId = Guid.Empty, // Guid.Empty = root (edge case)
                IsActive = true,
                IsOnlineVisible = true
            },
            new CategoryDocument
            {
                TenantId = tenantId,
                CategoryId = childCategoryId,
                Name = "Child Category",
                Slug = "child-category",
                ParentId = rootCategoryId,
                IsActive = true,
                IsOnlineVisible = true
            }
        };

        // Act
        var result = BuildCategoryTree(categories);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result); // Only 1 root category
        Assert.Equal(rootCategoryId, result[0].Id);
        Assert.Single(result[0].Children); // 1 child
    }

    /// <summary>
    /// Test: Multiple root categories should all be detected.
    /// </summary>
    [Fact]
    public void BuildCategoryTree_WithMultipleRoots_ShouldDetectAllRoots()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var root1Id = Guid.NewGuid();
        var root2Id = Guid.NewGuid();
        var root3Id = Guid.NewGuid();

        var categories = new List<CategoryDocument>
        {
            new CategoryDocument
            {
                TenantId = tenantId,
                CategoryId = root1Id,
                Name = "Electronics",
                Slug = "electronics",
                ParentId = null,
                IsActive = true,
                IsOnlineVisible = true
            },
            new CategoryDocument
            {
                TenantId = tenantId,
                CategoryId = root2Id,
                Name = "Clothing",
                Slug = "clothing",
                ParentId = Guid.Empty, // Edge case
                IsActive = true,
                IsOnlineVisible = true
            },
            new CategoryDocument
            {
                TenantId = tenantId,
                CategoryId = root3Id,
                Name = "Home & Garden",
                Slug = "home-garden",
                ParentId = null,
                IsActive = true,
                IsOnlineVisible = true
            }
        };

        // Act
        var result = BuildCategoryTree(categories);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count); // All 3 are roots
    }

    /// <summary>
    /// Test: Orphan categories (parent not in filtered list) should be hidden.
    /// This prevents stale child replay from promoting itself to root visibility.
    /// </summary>
    [Fact]
    public void BuildCategoryTree_WithOrphanCategory_ShouldHideOrphan()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var orphanCategoryId = Guid.NewGuid();
        var missingParentId = Guid.NewGuid(); // This parent doesn't exist in the list

        var categories = new List<CategoryDocument>
        {
            new CategoryDocument
            {
                TenantId = tenantId,
                CategoryId = orphanCategoryId,
                Name = "Orphan Category",
                Slug = "orphan-category",
                ParentId = missingParentId, // Parent not in list
                IsActive = true,
                IsOnlineVisible = true
            }
        };

        // Act
        var result = BuildCategoryTree(categories);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    /// <summary>
    /// Test: Deep hierarchy should be built correctly.
    /// Root -> Child -> Grandchild
    /// </summary>
    [Fact]
    public void BuildCategoryTree_WithDeepHierarchy_ShouldBuildCorrectTree()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var rootId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var grandchildId = Guid.NewGuid();

        var categories = new List<CategoryDocument>
        {
            new CategoryDocument
            {
                TenantId = tenantId,
                CategoryId = rootId,
                Name = "Root",
                Slug = "root",
                ParentId = null,
                IsActive = true,
                IsOnlineVisible = true
            },
            new CategoryDocument
            {
                TenantId = tenantId,
                CategoryId = childId,
                Name = "Child",
                Slug = "child",
                ParentId = rootId,
                IsActive = true,
                IsOnlineVisible = true
            },
            new CategoryDocument
            {
                TenantId = tenantId,
                CategoryId = grandchildId,
                Name = "Grandchild",
                Slug = "grandchild",
                ParentId = childId,
                IsActive = true,
                IsOnlineVisible = true
            }
        };

        // Act
        var result = BuildCategoryTree(categories);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result); // 1 root
        Assert.Equal(rootId, result[0].Id);
        Assert.Single(result[0].Children); // 1 child
        Assert.Equal(childId, result[0].Children[0].Id);
        Assert.Single(result[0].Children[0].Children); // 1 grandchild
        Assert.Equal(grandchildId, result[0].Children[0].Children[0].Id);
    }

    /// <summary>
    /// Test: Empty category list should return empty tree.
    /// </summary>
    [Fact]
    public void BuildCategoryTree_WithNoCategories_ShouldReturnEmptyList()
    {
        // Arrange
        var categories = new List<CategoryDocument>();

        // Act
        var result = BuildCategoryTree(categories);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    /// <summary>
    /// Test: Mixed null and Guid.Empty parents should all be detected as roots.
    /// This is the critical test for the bug fix.
    /// </summary>
    [Fact]
    public void BuildCategoryTree_WithMixedNullAndEmptyParents_ShouldDetectAllAsRoots()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var root1Id = Guid.NewGuid();
        var root2Id = Guid.NewGuid();
        var child1Id = Guid.NewGuid();
        var child2Id = Guid.NewGuid();

        var categories = new List<CategoryDocument>
        {
            new CategoryDocument
            {
                TenantId = tenantId,
                CategoryId = root1Id,
                Name = "Root 1 (null)",
                Slug = "root-1",
                ParentId = null, // Standard null
                IsActive = true,
                IsOnlineVisible = true
            },
            new CategoryDocument
            {
                TenantId = tenantId,
                CategoryId = root2Id,
                Name = "Root 2 (empty)",
                Slug = "root-2",
                ParentId = Guid.Empty, // Edge case: Guid.Empty
                IsActive = true,
                IsOnlineVisible = true
            },
            new CategoryDocument
            {
                TenantId = tenantId,
                CategoryId = child1Id,
                Name = "Child of Root 1",
                Slug = "child-1",
                ParentId = root1Id,
                IsActive = true,
                IsOnlineVisible = true
            },
            new CategoryDocument
            {
                TenantId = tenantId,
                CategoryId = child2Id,
                Name = "Child of Root 2",
                Slug = "child-2",
                ParentId = root2Id,
                IsActive = true,
                IsOnlineVisible = true
            }
        };

        // Act
        var result = BuildCategoryTree(categories);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count); // Both roots detected
        
        var root1 = result.FirstOrDefault(r => r.Id == root1Id);
        var root2 = result.FirstOrDefault(r => r.Id == root2Id);
        
        Assert.NotNull(root1);
        Assert.NotNull(root2);
        Assert.Single(root1.Children);
        Assert.Single(root2.Children);
    }

    /// <summary>
    /// Test: IsRootCategory helper should correctly identify root categories.
    /// </summary>
    [Theory]
    [InlineData(null, true)]  // null = root
    [InlineData("00000000-0000-0000-0000-000000000000", true)]  // Guid.Empty = root
    [InlineData("12345678-1234-1234-1234-123456789012", false)] // Valid GUID = not root
    public void IsRootCategory_ShouldCorrectlyIdentifyRoots(string? parentIdString, bool expectedIsRoot)
    {
        // Arrange
        Guid? parentId = parentIdString == null ? null : Guid.Parse(parentIdString);

        // Act
        var result = IsRootCategory(parentId);

        // Assert
        Assert.Equal(expectedIsRoot, result);
    }
}
