using PointOfSale.Core.Entities;

namespace PointOfSale.API.Services;

public interface ISaleFinancialIntegrityService
{
    Task RevertLegacyTreasuryEffectsAsync(
        SalesInvoice invoice,
        JournalPolicy journalPolicy,
        CancellationToken cancellationToken = default);
}
