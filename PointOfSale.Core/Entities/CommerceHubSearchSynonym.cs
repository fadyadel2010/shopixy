namespace PointOfSale.Core.Entities;

public class CommerceHubSearchSynonym : BaseEntity
{
    public const string OneWayDirection = "one_way";
    public const string TwoWayDirection = "two_way";

    public string Term { get; set; } = string.Empty;
    public string SynonymsJson { get; set; } = "[]";
    public string Direction { get; set; } = OneWayDirection;
}
