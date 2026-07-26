using PointOfSale.Core.Entities;
using Xunit;

namespace PointOfSale.Tests.Unit.Entities;

public class OrderRefundLineTests
{
    [Fact]
    public void Amount_ShouldAcceptZero()
    {
        // Arrange & Act
        var refundLine = new OrderRefundLine
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            OrderRefundId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            OrderItemId = Guid.NewGuid(),
            Amount = 0m
        };

        // Assert
        Assert.Equal(0m, refundLine.Amount);
    }

    [Fact]
    public void Amount_ShouldAcceptPositiveValue()
    {
        // Arrange & Act
        var refundLine = new OrderRefundLine
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            OrderRefundId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            OrderItemId = Guid.NewGuid(),
            Amount = 100.50m
        };

        // Assert
        Assert.Equal(100.50m, refundLine.Amount);
    }

    [Fact]
    public void Amount_ShouldThrowArgumentOutOfRangeException_WhenNegative()
    {
        // Arrange
        var refundLine = new OrderRefundLine
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            OrderRefundId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            OrderItemId = Guid.NewGuid()
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            refundLine.Amount = -10.00m;
        });

        Assert.Equal("Amount", exception.ParamName);
        Assert.Contains("cannot be negative", exception.Message);
    }

    [Fact]
    public void Amount_ShouldThrowArgumentOutOfRangeException_WhenSetToNegativeInConstructor()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var refundLine = new OrderRefundLine
            {
                Id = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                OrderRefundId = Guid.NewGuid(),
                OrderId = Guid.NewGuid(),
                OrderItemId = Guid.NewGuid(),
                Amount = -50.00m
            };
        });

        Assert.Equal("Amount", exception.ParamName);
        Assert.Contains("cannot be negative", exception.Message);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(-1.00)]
    [InlineData(-100.50)]
    [InlineData(-999999.99)]
    public void Amount_ShouldRejectAllNegativeValues(decimal negativeAmount)
    {
        // Arrange
        var refundLine = new OrderRefundLine
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            OrderRefundId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            OrderItemId = Guid.NewGuid()
        };

        // Act & Assert
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            refundLine.Amount = negativeAmount;
        });

        Assert.Equal("Amount", exception.ParamName);
    }

    [Theory]
    [InlineData(0.00)]
    [InlineData(0.01)]
    [InlineData(1.00)]
    [InlineData(100.50)]
    [InlineData(999999.99)]
    public void Amount_ShouldAcceptAllNonNegativeValues(decimal nonNegativeAmount)
    {
        // Arrange & Act
        var refundLine = new OrderRefundLine
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            OrderRefundId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            OrderItemId = Guid.NewGuid(),
            Amount = nonNegativeAmount
        };

        // Assert
        Assert.Equal(nonNegativeAmount, refundLine.Amount);
    }

    [Fact]
    public void Amount_ShouldAllowMultipleUpdates_WithValidValues()
    {
        // Arrange
        var refundLine = new OrderRefundLine
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            OrderRefundId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            OrderItemId = Guid.NewGuid(),
            Amount = 50.00m
        };

        // Act
        refundLine.Amount = 75.00m;
        refundLine.Amount = 100.00m;
        refundLine.Amount = 0m;

        // Assert
        Assert.Equal(0m, refundLine.Amount);
    }
}
