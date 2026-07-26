using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;

namespace PointOfSale.Tests.TestDoubles;

internal sealed class NoOpCommerceHubVendorDebtService : ICommerceHubVendorDebtService
{
    public Task<CommerceHubVendorDebtSnapshot> GetVendorDebtSnapshotAsync(
        Guid vendorId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateSnapshot(vendorId));

    public Task<IReadOnlyDictionary<Guid, CommerceHubVendorDebtSnapshot>> GetVendorDebtSnapshotsAsync(
        IReadOnlyCollection<Guid> vendorIds,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, CommerceHubVendorDebtSnapshot>>(
            vendorIds
                .Where(vendorId => vendorId != Guid.Empty)
                .Distinct()
                .ToDictionary(vendorId => vendorId, CreateSnapshot));

    private static CommerceHubVendorDebtSnapshot CreateSnapshot(Guid vendorId) =>
        new(
            vendorId,
            0m,
            0m,
            1000m,
            0,
            800m,
            null,
            null,
            false,
            false,
            false,
            false,
            0m,
            "ACTIVE",
            null,
            Array.Empty<CommerceHubVendorSettlementDto>());
}
