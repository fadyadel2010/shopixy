using Nest;
using PointOfSale.Infrastructure.ReadModels.Evolution;

namespace PointOfSale.Infrastructure.Elasticsearch.Documents;

public class CommerceHubProductSearchDocument : IReadModelEvolutionDocument
{
    public const int CurrentReadModelSchemaVersion = ReadModelEvolutionRegistry.CommerceHubProductSearchSchemaVersion;
    public const int CurrentProjectionVersion = ReadModelEvolutionRegistry.CommerceHubProductSearchProjectionVersion;
    public const int CurrentIndexVersion = ReadModelEvolutionRegistry.CommerceHubProductSearchIndexVersion;

    [Keyword]
    public string ReadModelName { get; set; } = ReadModelEvolutionRegistry.CommerceHubProduct;

    [Number(NumberType.Integer)]
    public int ReadModelSchemaVersion { get; set; } = CurrentReadModelSchemaVersion;

    [Number(NumberType.Integer)]
    public int ProjectionVersion { get; set; } = CurrentProjectionVersion;

    [Date(Format = "strict_date_optional_time||epoch_millis")]
    public DateTime? ProjectionConvergedAtUtc { get; set; }

    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid StoreId { get; set; }
    public Guid ProductId { get; set; }
    public Guid VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ProductNameNormalized { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public Guid CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public List<string> CategoryPath { get; set; } = new();
    public string Currency { get; set; } = "EGP";
    public double Price { get; set; }
    public string PriceSource { get; set; } = "ProductVariantEffectiveOnlinePrice";
    public bool OverridePresent { get; set; }
    public bool InStock { get; set; }
    public double TotalStock { get; set; }
    public double Rating { get; set; }
    public int ReviewCount { get; set; }
    public double PopularityScore { get; set; }
    public DateTime? LastClickAtUtc { get; set; }
    public Dictionary<string, object> Attributes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsPublished { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }

    public string GetDocumentId() => $"{TenantId:N}:{Id:N}";

    public string GetLegacyDocumentId() => Id.ToString("N");
}
