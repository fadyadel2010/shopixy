namespace PointOfSale.Core.Enums;

/// <summary>
/// Known marketing platforms for campaign evidence.
/// Stored as string in database for forward-compatibility.
/// </summary>
public enum MarketingPlatform
{
    Facebook = 0,
    Instagram = 1,
    Google = 2,
    TikTok = 3,
    Twitter = 4,
    LinkedIn = 5,
    YouTube = 6,
    Snapchat = 7,
    Email = 8,
    WhatsApp = 9,
    Other = 99
}
