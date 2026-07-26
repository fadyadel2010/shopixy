using System.Reflection;
using System.Text.Json;
using PointOfSale.Infrastructure.Elasticsearch.Documents;
using PointOfSale.Infrastructure.Elasticsearch.Services;

namespace PointOfSale.Tests.CommerceHub;

public class CommerceHubSearchIndexServiceCompatibilityTests
{
    private static readonly Type ServiceType = typeof(CommerceHubSearchIndexService);

    [Fact]
    public void TryExtractStrictMappingField_ShouldReturnOffendingField()
    {
        var method = ServiceType.GetMethod(
            "TryExtractStrictMappingField",
            BindingFlags.NonPublic | BindingFlags.Static);

        var result = method!.Invoke(
            null,
            ["mapping set to strict, dynamic introduction of [rating] within [_doc] is not allowed"]);

        Assert.Equal("rating", result);
    }

    [Fact]
    public void ResolveLegacyFieldsToStrip_ShouldAddStrictField_WhenMappingDiagnosticsMissIt()
    {
        var method = ServiceType.GetMethod(
            "ResolveLegacyFieldsToStrip",
            BindingFlags.NonPublic | BindingFlags.Static);

        var result = Assert.IsType<HashSet<string>>(
            method!.Invoke(
                null,
                [Array.Empty<string>(), "mapping set to strict, dynamic introduction of [rating] within [_doc] is not allowed"]));

        Assert.Contains("rating", result);
    }

    [Fact]
    public void TryResolveMappingDefinition_ShouldHandleLegacyTypedMappings()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "commercehub_products": {
                "mappings": {
                  "_doc": {
                    "dynamic": "strict",
                    "dynamic_templates": [
                      {
                        "attributes_string": {
                          "path_match": "attributes.*",
                          "match_mapping_type": "string",
                          "mapping": { "type": "keyword" }
                        }
                      }
                    ],
                    "properties": {
                      "productName": { "type": "text" },
                      "rating": { "type": "double" }
                    }
                  }
                }
              }
            }
            """);

        var method = ServiceType.GetMethod(
            "TryResolveMappingDefinition",
            BindingFlags.NonPublic | BindingFlags.Static);

        var arguments = new object?[]
        {
            document.RootElement.GetProperty("commercehub_products"),
            null
        };

        var resolved = (bool)method!.Invoke(null, arguments)!;

        Assert.True(resolved);

        var mappingDefinition = Assert.IsType<JsonElement>(arguments[1]);
        Assert.True(mappingDefinition.TryGetProperty("properties", out var properties));
        Assert.True(properties.TryGetProperty("rating", out _));
        Assert.True(properties.TryGetProperty("productName", out _));
    }

    [Fact]
    public void BuildLegacyCompatibleDocumentPayload_ShouldStripMissingCompatibilityFields()
    {
        var method = ServiceType.GetMethod(
            "BuildLegacyCompatibleDocumentPayload",
            BindingFlags.NonPublic | BindingFlags.Static);

        var searchDocument = new CommerceHubProductSearchDocument
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            StoreId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            VariantId = Guid.NewGuid(),
            ProductName = "Galaxy Case",
            ProductNameNormalized = "galaxy case",
            Description = "Slim protective case",
            StoreName = "Shopixy",
            CategoryId = Guid.NewGuid(),
            CategoryName = "Accessories",
            CategoryPath = ["Electronics", "Accessories"],
            Currency = "EGP",
            Price = 199,
            InStock = true,
            TotalStock = 12,
            Rating = 4.8,
            ReviewCount = 24,
            PopularityScore = 9.5,
            LastClickAtUtc = DateTime.UtcNow,
            Attributes = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["color"] = "black"
            },
            IsPublished = true,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3),
            UpdatedAtUtc = DateTime.UtcNow
        };

        var payload = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            method!.Invoke(null, [searchDocument, new[] { "rating" }]));

        Assert.False(payload.ContainsKey("rating"));
        Assert.Equal(24, payload["reviewCount"]);
        Assert.Equal("Galaxy Case", payload["productName"]);
    }

    [Fact]
    public void CommerceHubProductSearchDocument_GetDocumentId_IsTenantScoped()
    {
        var tenantId = Guid.NewGuid();
        var listingId = Guid.NewGuid();
        var document = new CommerceHubProductSearchDocument
        {
            Id = listingId,
            TenantId = tenantId
        };

        Assert.Equal($"{tenantId:N}:{listingId:N}", document.GetDocumentId());
        Assert.Equal(listingId.ToString("N"), document.GetLegacyDocumentId());
    }
}
