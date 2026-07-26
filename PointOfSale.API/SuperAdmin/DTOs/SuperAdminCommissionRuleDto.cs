using PointOfSale.Core.Enums;

namespace PointOfSale.API.SuperAdmin.DTOs;

public sealed class SuperAdminCommissionRuleDto
{
    public Guid Id { get; init; }
    public CommissionRuleScopeType ScopeType { get; init; }
    public Guid? ScopeId { get; init; }
    public decimal Rate { get; init; }
    public bool IsActive { get; init; }
    public int Priority { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
