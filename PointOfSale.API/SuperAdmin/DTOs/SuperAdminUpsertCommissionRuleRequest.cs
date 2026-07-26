using System.ComponentModel.DataAnnotations;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.SuperAdmin.DTOs;

public sealed class SuperAdminUpsertCommissionRuleRequest
{
    [Required]
    public CommissionRuleScopeType? ScopeType { get; init; }

    public Guid? ScopeId { get; init; }

    [Required]
    [Range(typeof(decimal), "0", "1", ErrorMessage = "Rate must be between 0 and 1.")]
    public decimal? Rate { get; init; }

    public int Priority { get; init; }
}
