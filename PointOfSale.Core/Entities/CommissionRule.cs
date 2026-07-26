using PointOfSale.Core.Enums;

namespace PointOfSale.Core.Entities;

public class CommissionRule : BaseEntity
{
    public CommissionRuleScopeType ScopeType { get; set; } = CommissionRuleScopeType.Global;
    public Guid? ScopeId { get; set; }
    public decimal Rate { get; set; }
    public bool IsActive { get; set; } = true;
    public int Priority { get; set; }
}
