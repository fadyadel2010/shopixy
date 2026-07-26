using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services;

public interface IPurchaseFinancialIntegrityService
{
    Task RevertLegacyTreasuryEffectsAsync(
        PurchaseInvoice invoice,
        JournalPolicy journalPolicy,
        CancellationToken cancellationToken = default);
}
