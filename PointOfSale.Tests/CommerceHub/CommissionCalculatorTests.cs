using PointOfSale.API.Services.CommerceHub;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommissionCalculatorTests
{
    [Theory]
    [InlineData(10.005, 10.01)]
    [InlineData(10.004, 10.00)]
    [InlineData(12.345, 12.35)]
    [InlineData(-12.345, -12.35)]
    public void RoundAmount_ShouldUseAwayFromZero(double value, double expected)
    {
        var rounded = CommissionCalculator.RoundAmount((decimal)value);

        Assert.Equal((decimal)expected, rounded);
    }

    [Theory]
    [InlineData(0.1234564, 0.123456)]
    [InlineData(0.1234565, 0.123457)]
    [InlineData(0.1, 0.1)]
    public void RoundRate_ShouldUseSixDecimalPlaces(double value, double expected)
    {
        var rounded = CommissionCalculator.RoundRate((decimal)value);

        Assert.Equal((decimal)expected, rounded);
    }
}
