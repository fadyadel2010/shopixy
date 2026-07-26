using PointOfSale.Core.Entities;
using Xunit;

namespace PointOfSale.Tests.Unit;

/// <summary>
/// Tests for ProductVariant entity validation.
/// </summary>
public sealed class ProductVariantTests
{
    [Fact]
    public void Weight_WhenSetToNegativeValue_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var variant = new ProductVariant
        {
            TenantId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            Price = 100m,
            IsDefault = true
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            variant.Weight = -1.5m;
        });

        Assert.Equal("Weight", exception.ParamName);
        Assert.Contains("Weight must be non-negative", exception.Message);
    }

    [Fact]
    public void Weight_WhenSetToZero_DoesNotThrow()
    {
        // Arrange
        var variant = new ProductVariant
        {
            TenantId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            Price = 100m,
            IsDefault = true
        };

        // Act & Assert
        variant.Weight = 0m;
        Assert.Equal(0m, variant.Weight);
    }

    [Fact]
    public void Weight_WhenSetToPositiveValue_AcceptsValue()
    {
        // Arrange
        var variant = new ProductVariant
        {
            TenantId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            Price = 100m,
            IsDefault = true
        };

        // Act
        variant.Weight = 2.5m;

        // Assert
        Assert.Equal(2.5m, variant.Weight);
    }

    [Fact]
    public void Weight_WhenSetToNull_AcceptsValue()
    {
        // Arrange
        var variant = new ProductVariant
        {
            TenantId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            Price = 100m,
            IsDefault = true,
            Weight = 5m
        };

        // Act
        variant.Weight = null;

        // Assert
        Assert.Null(variant.Weight);
    }
}
