using PointOfSale.Core.Helpers;
using Xunit;

namespace PointOfSale.Tests.Unit.Helpers;

/// <summary>
/// Unit tests for ProductEventVersionHelper.
/// 
/// CRITICAL: Event version conversion is critical for:
/// - Maintaining event ordering consistency
/// - Preventing projection race conditions
/// - Ensuring event version integrity
/// 
/// Phase: Production Hardening - Event Version Conversion Tests
/// </summary>
public class ProductEventVersionHelperTests
{
    #region ToProductEventVersion Tests

    [Fact]
    public void ToProductEventVersion_WithZero_ReturnsOne()
    {
        // Arrange
        long projectionMutationVersion = 0;

        // Act
        var result = ProductEventVersionHelper.ToProductEventVersion(projectionMutationVersion);

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public void ToProductEventVersion_WithNegativeValue_ReturnsOne()
    {
        // Arrange
        long projectionMutationVersion = -5;

        // Act
        var result = ProductEventVersionHelper.ToProductEventVersion(projectionMutationVersion);

        // Assert
        Assert.Equal(1, result);
    }

    [Fact]
    public void ToProductEventVersion_WithValidPositiveValue_ReturnsValue()
    {
        // Arrange
        long projectionMutationVersion = 42;

        // Act
        var result = ProductEventVersionHelper.ToProductEventVersion(projectionMutationVersion);

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public void ToProductEventVersion_WithIntMaxValue_ReturnsIntMaxValue()
    {
        // Arrange
        long projectionMutationVersion = int.MaxValue;

        // Act
        var result = ProductEventVersionHelper.ToProductEventVersion(projectionMutationVersion);

        // Assert
        Assert.Equal(int.MaxValue, result);
    }

    [Fact]
    public void ToProductEventVersion_WithValueExceedingIntMaxValue_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        long projectionMutationVersion = (long)int.MaxValue + 1;

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProductEventVersionHelper.ToProductEventVersion(projectionMutationVersion));

        Assert.Equal("projectionMutationVersion", ex.ParamName);
        Assert.Contains("exceeds int.MaxValue", ex.Message, StringComparison.Ordinal);
        Assert.Contains("event contract must be upgraded", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToProductEventVersion_WithLongMaxValue_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        long projectionMutationVersion = long.MaxValue;

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProductEventVersionHelper.ToProductEventVersion(projectionMutationVersion));

        Assert.Equal("projectionMutationVersion", ex.ParamName);
        Assert.Contains($"ProjectionMutationVersion ({projectionMutationVersion})", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    [InlineData(1000, 1000)]
    [InlineData(999999, 999999)]
    [InlineData(2147483647, 2147483647)] // int.MaxValue
    public void ToProductEventVersion_WithVariousValidValues_ReturnsExpectedValue(long input, int expected)
    {
        // Act
        var result = ProductEventVersionHelper.ToProductEventVersion(input);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(-999999)]
    [InlineData(long.MinValue)]
    public void ToProductEventVersion_WithVariousNegativeValues_ReturnsOne(long input)
    {
        // Act
        var result = ProductEventVersionHelper.ToProductEventVersion(input);

        // Assert
        Assert.Equal(1, result);
    }

    #endregion

    #region Boundary Tests

    [Fact]
    public void ToProductEventVersion_WithValueJustBelowIntMaxValue_Succeeds()
    {
        // Arrange
        long projectionMutationVersion = int.MaxValue - 1;

        // Act
        var result = ProductEventVersionHelper.ToProductEventVersion(projectionMutationVersion);

        // Assert
        Assert.Equal(int.MaxValue - 1, result);
    }

    [Fact]
    public void ToProductEventVersion_WithValueJustAboveIntMaxValue_Throws()
    {
        // Arrange
        long projectionMutationVersion = (long)int.MaxValue + 1;

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProductEventVersionHelper.ToProductEventVersion(projectionMutationVersion));
    }

    #endregion

    #region Error Message Tests

    [Fact]
    public void ToProductEventVersion_ExceptionMessage_ContainsAllRequiredInformation()
    {
        // Arrange
        long projectionMutationVersion = (long)int.MaxValue + 100;

        // Act & Assert
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProductEventVersionHelper.ToProductEventVersion(projectionMutationVersion));

        // Verify error message contains all critical information
        Assert.Contains($"ProjectionMutationVersion ({projectionMutationVersion})", ex.Message, StringComparison.Ordinal);
        Assert.Contains($"int.MaxValue ({int.MaxValue})", ex.Message, StringComparison.Ordinal);
        Assert.Contains("event contract must be upgraded to a wider type (e.g., long)", ex.Message, StringComparison.Ordinal);
    }

    #endregion
}
