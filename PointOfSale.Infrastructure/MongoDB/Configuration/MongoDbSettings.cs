namespace PointOfSale.Infrastructure.MongoDB.Configuration;

/// <summary>
/// Configuration settings for MongoDB connection.
/// Should be populated from appsettings.json "MongoDB" section.
/// </summary>
public class MongoDbSettings
{
    /// <summary>
    /// The configuration section name in appsettings.json
    /// </summary>
    public const string SectionName = "MongoDB";

    /// <summary>
    /// MongoDB connection string (e.g., mongodb://localhost:27017)
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Database name for the e-commerce read store
    /// </summary>
    public string DatabaseName { get; set; } = "ecommerce_read_db";

    /// <summary>
    /// Collection name for products
    /// </summary>
    public string ProductsCollectionName { get; set; } = "products";

    /// <summary>
    /// Collection name for categories
    /// </summary>
    public string CategoriesCollectionName { get; set; } = "categories";
}
