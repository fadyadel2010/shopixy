using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using PointOfSale.Core.Enums;

namespace PointOfSale.Infrastructure.MongoDB.Documents;

public sealed class StorefrontFooterDocument
{
    [BsonId]
    public string Id { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.String)]
    public Guid? TenantId { get; set; }

    public DateTime UpdatedAt { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid? LastEventId { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastEventOccurredAtUtc { get; set; }

    public int LastEventVersion { get; set; }

    public List<StorefrontFooterSectionDocument> Sections { get; set; } = new();

    public List<StorefrontSocialLinkDocument> SocialLinks { get; set; } = new();
}

public sealed class StorefrontFooterSectionDocument
{
    public string Title { get; set; } = string.Empty;

    public string? TitleAr { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime UpdatedAt { get; set; }

    public List<StorefrontFooterLinkDocument> Links { get; set; } = new();
}

public sealed class StorefrontFooterLinkDocument
{
    public string Label { get; set; } = string.Empty;

    public string? LabelAr { get; set; }

    public FooterLinkType Type { get; set; } = FooterLinkType.Internal;

    public string? Url { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid? CmsPageGroupId { get; set; }

    public string? CmsPageSlug { get; set; }

    public bool OpenInNewTab { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime UpdatedAt { get; set; }
}

public sealed class StorefrontSocialLinkDocument
{
    public string Platform { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? Icon { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime UpdatedAt { get; set; }
}
