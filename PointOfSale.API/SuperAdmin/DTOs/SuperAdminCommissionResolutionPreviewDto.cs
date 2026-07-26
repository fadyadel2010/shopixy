using PointOfSale.API.Services.CommerceHub;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.SuperAdmin.DTOs;

public sealed class SuperAdminCommissionResolutionPreviewDto
{
    public Guid? VendorId { get; init; }
    public Guid? CategoryId { get; init; }
    public decimal Rate { get; init; }
    public string Source { get; init; } = CommissionCalculator.LegacySource;
    public Guid? RuleId { get; init; }
    public int? Priority { get; init; }
    public CommissionRuleScopeType? ScopeType { get; init; }
    public Guid? ScopeId { get; init; }
    public bool UsedLegacyFallback { get; init; }
    public string CacheStrategy { get; init; } = "MemoryCache";
}
