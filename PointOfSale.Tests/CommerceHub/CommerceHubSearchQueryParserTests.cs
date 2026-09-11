using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using PointOfSale.API.CommerceHub.Services;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubSearchQueryParserTests
{
    [Fact]
    public void Parse_IgnoresLanguageQueryParameter_AndPreservesAttributeFilters()
    {
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["q"] = "NOKIA-N73-BLACK-32",
            ["page"] = "1",
            ["pageSize"] = "12",
            ["lang"] = "en",
            ["attr.brand"] = "xiaomi"
        });

        var filters = new CommerceHubSearchQueryParser().Parse(query, filtersJson: null);

        var filter = Assert.Single(filters);
        Assert.Equal("brand", filter.Key);
        Assert.Equal("xiaomi", Assert.Single(filter.Terms));
    }
}
