using PointOfSale.API.Controllers;

namespace PointOfSale.Tests.Architecture;

public sealed class OrderFinancialIntegrityHardeningTests
{
    [Fact]
    public void OrdersController_GetOrder_MustNotContainLegacyFallbackBranch()
    {
        var source = File.ReadAllText(GetOrdersControllerPath());
        var methodSource = ExtractMethodSource(source, "public async Task<ActionResult<OrderDetailDto>> GetOrder(");

        Assert.DoesNotContain("var order = await _context.Orders", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Ensure fresh data", methodSource, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdersController_GetOrderByNumber_MustNotContainLegacyFallbackBranch()
    {
        var source = File.ReadAllText(GetOrdersControllerPath());
        var methodSource = ExtractMethodSource(source, "public async Task<ActionResult<OrderDetailDto>> GetOrderByNumber(");

        Assert.DoesNotContain("var order = await _context.Orders", methodSource, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdersController_GetOrderStatistics_MustNotContainLegacySingleSourceFallbackBranch()
    {
        var source = File.ReadAllText(GetOrdersControllerPath());
        var methodSource = ExtractMethodSource(source, "public async Task<ActionResult<OrderStatisticsDto>> GetOrderStatistics(");

        Assert.DoesNotContain("var query = _context.Orders.AsQueryable()", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentStatus.Paid", methodSource, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdersController_CreateOrder_MustUseServerCalculatedFinancials()
    {
        var source = File.ReadAllText(GetOrdersControllerPath());
        var methodSource = ExtractMethodSource(source, "public async Task<ActionResult<OrderDetailDto>> CreateOrder(");

        Assert.Contains("CalculateAuthoritativeCreateOrderFinancialsAsync", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Subtotal = dto.Subtotal", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ShippingCost = dto.ShippingCost", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("TaxAmount = dto.TaxAmount", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DiscountAmount = couponDiscount + dto.DiscountAmount", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var item in dto.Items)", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Total = (item.UnitPrice * item.Quantity) - item.DiscountAmount + item.TaxAmount", methodSource, StringComparison.Ordinal);
    }

    private static string GetOrdersControllerPath()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Controllers",
            "OrdersController.cs"));
    }

    private static string ExtractMethodSource(string source, string signature)
    {
        var startIndex = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Could not locate method signature '{signature}'.");

        var braceIndex = source.IndexOf('{', startIndex);
        if (braceIndex < 0)
        {
            return source[startIndex..source.Length];
        }

        var braceCount = 1;
        var index = braceIndex + 1;
        var inString = false;
        var inVerbatimString = false;
        var inRawString = false;
        var inChar = false;
        var inSingleLineComment = false;
        var inMultiLineComment = false;

        while (index < source.Length && braceCount > 0)
        {
            var c = source[index];
            var prev = index > 0 ? source[index - 1] : '\0';

            if ((inString || inChar) && !inVerbatimString && prev == '\\')
            {
                index++;
                continue;
            }

            if (!inString && !inVerbatimString && !inRawString && !inChar && !inMultiLineComment &&
                c == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                inSingleLineComment = true;
                index += 2;
                continue;
            }

            if (inSingleLineComment && c == '\n')
            {
                inSingleLineComment = false;
                index++;
                continue;
            }

            if (!inString && !inVerbatimString && !inRawString && !inChar && !inSingleLineComment &&
                c == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                inMultiLineComment = true;
                index += 2;
                continue;
            }

            if (inMultiLineComment && c == '*' && index + 1 < source.Length && source[index + 1] == '/')
            {
                inMultiLineComment = false;
                index += 2;
                continue;
            }

            if (inSingleLineComment || inMultiLineComment)
            {
                index++;
                continue;
            }

            if (!inString && !inVerbatimString && !inRawString && !inChar && c == '"')
            {
                var quoteCount = 0;
                var tempIndex = index;
                while (tempIndex < source.Length && source[tempIndex] == '"')
                {
                    quoteCount++;
                    tempIndex++;
                }

                if (quoteCount >= 3)
                {
                    inRawString = true;
                    index = tempIndex;
                    continue;
                }
            }

            if (inRawString)
            {
                if (c == '"' && index + 2 < source.Length &&
                    source[index + 1] == '"' &&
                    source[index + 2] == '"')
                {
                    inRawString = false;
                    index += 3;
                    continue;
                }

                index++;
                continue;
            }

            if (!inString && !inVerbatimString && !inChar && c == '@' && index + 1 < source.Length && source[index + 1] == '"')
            {
                inVerbatimString = true;
                index += 2;
                continue;
            }

            if (inVerbatimString)
            {
                if (c == '"' && !(index + 1 < source.Length && source[index + 1] == '"'))
                {
                    inVerbatimString = false;
                }
                else if (c == '"' && index + 1 < source.Length && source[index + 1] == '"')
                {
                    index += 2;
                    continue;
                }

                index++;
                continue;
            }

            if (!inString && !inChar && c == '"')
            {
                inString = true;
                index++;
                continue;
            }

            if (inString && c == '"')
            {
                inString = false;
                index++;
                continue;
            }

            if (!inString && !inChar && c == '\'')
            {
                inChar = true;
                index++;
                continue;
            }

            if (inChar && c == '\'')
            {
                inChar = false;
                index++;
                continue;
            }

            if (inString || inChar)
            {
                index++;
                continue;
            }

            if (c == '{')
            {
                braceCount++;
            }
            else if (c == '}')
            {
                braceCount--;
            }

            index++;
        }

        return source[startIndex..Math.Min(index, source.Length)];
    }
}
