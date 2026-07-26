using System.Runtime.CompilerServices;
using PointOfSale.Core.Enums;

namespace PointOfSale.API.Services;

/// <summary>
/// Internal-only maintenance surface for destructive balance repair operations.
/// This is intentionally separated from the public ledger mutation interface.
/// </summary>
public interface IPartyBalanceMaintenanceService
{
    Task<decimal> RebuildPartyBalanceAsync(
        PartyType partyType,
        Guid partyId,
        string reason,
        bool writeAuditTrail = true,
        [CallerMemberName] string? requestedBy = null,
        CancellationToken cancellationToken = default);
}
