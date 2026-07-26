namespace PointOfSale.API.Services.CommerceHub;

public abstract record CommerceHubSearchAnalyticsEvent;

public sealed record CommerceHubTrackedSearchClick(
    string QueryText,
    Guid ProductId,
    Guid? VariantId,
    int Position,
    DateTime ClickedAtUtc,
    string? SessionId,
    Guid? TenantId) : CommerceHubSearchAnalyticsEvent;

public sealed record CommerceHubTrackedSearchImpression(
    string QueryText,
    IReadOnlyCollection<Guid> ProductIds,
    DateTime ImpressedAtUtc,
    string? SessionId,
    Guid? TenantId) : CommerceHubSearchAnalyticsEvent;

public sealed record CommerceHubTrackedSearchFilterEvent(
    string FilterKey,
    string FilterValue,
    string QueryText,
    DateTime OccurredAtUtc,
    string? SessionId,
    Guid? TenantId) : CommerceHubSearchAnalyticsEvent;

public sealed record CommerceHubTrackedSearchQuery(
    string QueryText,
    long ResultCount,
    string Intent,
    IReadOnlyCollection<string> AppliedExpansions,
    DateTime SearchedAtUtc) : CommerceHubSearchAnalyticsEvent;
