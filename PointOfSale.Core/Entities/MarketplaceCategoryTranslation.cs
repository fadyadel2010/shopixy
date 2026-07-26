namespace PointOfSale.Core.Entities;

public sealed class MarketplaceCategoryTranslation
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public string Language { get; set; } = "en";
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Category? Category { get; set; }
}
