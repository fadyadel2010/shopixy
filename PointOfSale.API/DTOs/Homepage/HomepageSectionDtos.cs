using System.Text.Json;

namespace PointOfSale.API.DTOs.Homepage;

public sealed class HomepageSectionDto
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public int Order { get; set; }
    public JsonElement Data { get; set; }
    public int Version { get; set; }
    public int SchemaVersion { get; set; }
    public bool IsActive { get; set; }
    public bool IsDraft { get; set; }
    public bool IsPublished { get; set; }
    public DateTime? PublishedAt { get; set; }
    public Guid? DraftGroupId { get; set; }
    public Guid? TenantId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class HomepageSectionWriteRequest
{
    public string Type { get; set; } = string.Empty;
    public int Order { get; set; }
    public JsonElement Data { get; set; }
    public int? SchemaVersion { get; set; }
    public bool? IsActive { get; set; }
    public Guid? TenantId { get; set; }
}

public sealed class ReorderHomepageSectionsRequest
{
    public Guid? TenantId { get; set; }
    public List<Guid> SectionIds { get; set; } = new();
}

public sealed class PublishHomepageSectionsRequest
{
    public Guid? TenantId { get; set; }
}

public sealed class SaveHomepageDraftRequest
{
    public Guid? TenantId { get; set; }
    public List<SaveHomepageDraftSectionRequest> Sections { get; set; } = new();
}

public sealed class SaveHomepageDraftSectionRequest
{
    public Guid? Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public int Order { get; set; }
    public JsonElement Data { get; set; }
    public int? SchemaVersion { get; set; }
    public bool? IsActive { get; set; }
}

public sealed class GenerateHomepageTemplateRequest
{
    public Guid? TenantId { get; set; }
    public bool OverwriteExisting { get; set; }
}

public sealed class HomepageSectionSchemaDto
{
    public string Type { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public List<HomepageSectionSchemaFieldDto> Fields { get; set; } = new();
}

public sealed class HomepageSectionSchemaFieldDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public bool Required { get; set; }
    public bool IsLocalized { get; set; }
    public string? HelpText { get; set; }
    public List<string> Options { get; set; } = new();
}
