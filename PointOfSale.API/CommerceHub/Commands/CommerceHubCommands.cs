namespace PointOfSale.API.CommerceHub.Commands;

public sealed record PublishToCommerceHubCommand(
    Guid TenantId,
    Guid VariantId,
    Guid CategoryId,
    decimal? CommerceHubPrice,
    string? AttributesJson = null,
    Guid? ActorUserId = null,
    string? CorrelationId = null,
    string? RequestId = null,
    string? OverrideReason = null,
    int? ExpectedPriceOverrideMutationVersion = null);

public sealed record UpdateCommerceHubListingCommand(
    Guid TenantId,
    Guid VariantId,
    Guid CategoryId,
    decimal? CommerceHubPrice,
    string? AttributesJson = null,
    Guid? ActorUserId = null,
    string? CorrelationId = null,
    string? RequestId = null,
    string? OverrideReason = null,
    int? ExpectedPriceOverrideMutationVersion = null);

public sealed record UnpublishFromCommerceHubCommand(
    Guid TenantId,
    Guid VariantId,
    Guid? ActorUserId = null,
    string? CorrelationId = null,
    string? RequestId = null,
    string? Reason = null);
