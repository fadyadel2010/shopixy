namespace PointOfSale.Infrastructure.Elasticsearch.Configuration;

/// <summary>
/// Configuration settings for Elasticsearch connection.
/// Bound from appsettings.json section "Elasticsearch".
/// </summary>
public class ElasticsearchSettings
{
    /// <summary>
    /// Configuration section name in appsettings.json
    /// </summary>
    public const string SectionName = "Elasticsearch";

    /// <summary>
    /// Elasticsearch server URL (e.g., http://localhost:9200)
    /// </summary>
    public string Url { get; set; } = "http://localhost:9200";

    /// <summary>
    /// Whether Elasticsearch integration is enabled.
    /// When false, indexing operations are skipped gracefully.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Index name for products.
    /// Using single index with tenant isolation via TenantId field.
    /// </summary>
    public string ProductsIndexName { get; set; } = "products";

    /// <summary>
    /// AF-11: Index name for public affiliate products.
    /// </summary>
    public string PublicAffiliateProductsIndexName { get; set; } = "public_affiliate_products";

    /// <summary>
    /// CommerceHub global listing index.
    /// </summary>
    public string CommerceHubProductsIndexName { get; set; } = "commercehub_products";

    /// <summary>
    /// Current version of the CommerceHub search index schema.
    /// Versioned indices are created as commercehub_products_v{n}.
    /// </summary>
    public int CommerceHubProductsIndexVersion { get; set; } = 2;

    /// <summary>
    /// Number of shards for the products index.
    /// </summary>
    public int NumberOfShards { get; set; } = 1;

    /// <summary>
    /// Number of replicas for the products index.
    /// </summary>
    public int NumberOfReplicas { get; set; } = 0;

    /// <summary>
    /// Request timeout in seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to enable debug mode (verbose logging).
    /// </summary>
    public bool EnableDebugMode { get; set; } = false;
}
