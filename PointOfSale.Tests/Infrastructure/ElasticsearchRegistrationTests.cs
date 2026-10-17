using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nest;
using PointOfSale.Infrastructure.Elasticsearch;

namespace PointOfSale.Tests.Infrastructure;

public sealed class ElasticsearchRegistrationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("not-a-uri")]
    [InlineData("https://must-not-contact.example.invalid")]
    public async Task DisabledSearch_ResolvesClientWithoutEndpointOrNetwork_AndNeverClaimsSuccess(string url)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elasticsearch:Enabled"] = "false", ["Elasticsearch:Url"] = url
        }).Build();
        using var provider = new ServiceCollection().AddElasticsearch(config).BuildServiceProvider();
        var result = await provider.GetRequiredService<IElasticClient>().PingAsync();
        Assert.False(result.IsValid);
        Assert.Equal(503, result.ApiCall.HttpStatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData("file:///private")]
    public void EnabledSearch_RejectsInvalidEndpointWithoutEchoingConfiguration(string url)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Elasticsearch:Enabled"] = "true", ["Elasticsearch:Url"] = url
        }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddElasticsearch(config));
        Assert.Equal("Enabled Elasticsearch requires an absolute HTTP or HTTPS endpoint.", error.Message);
    }
}
