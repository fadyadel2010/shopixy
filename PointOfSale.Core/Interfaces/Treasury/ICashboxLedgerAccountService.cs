using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;

namespace PointOfSale.Core.Interfaces.Treasury;

/// <summary>
/// Resolves dedicated ledger accounts for physical cashboxes.
/// </summary>
public interface ICashboxLedgerAccountService
{
    Task<CashboxLedgerAccountInfo> EnsureCashboxAccountAsync(
        Cashbox cashbox,
        string? warehouseName = null,
        CancellationToken ct = default);

    Task<CashboxLedgerAccountInfo> ResolveCashboxAccountAsync(
        Guid tenantId,
        Guid cashboxId,
        CancellationToken ct = default);

    /// <summary>
    /// Attempts to resolve an existing cashbox account without creating one.
    /// Returns null if the account does not exist.
    /// </summary>
    Task<CashboxLedgerAccountInfo?> GetCashboxAccountIfExistsAsync(
        Guid tenantId,
        Guid cashboxId,
        CancellationToken ct = default);

    Task<CashboxLedgerAccountInfo> ResolveWarehouseCashboxAccountAsync(
        Guid tenantId,
        Guid warehouseId,
        CancellationToken ct = default);

    Task<CashboxLedgerAccountInfo> ResolveDefaultCashboxAccountAsync(
        Guid tenantId,
        CancellationToken ct = default);

    Task<IReadOnlyList<CashboxLedgerAccountInfo>> ListCashboxAccountsAsync(
        Guid tenantId,
        CancellationToken ct = default);
}

/// <summary>
/// Snapshot of the dedicated ledger account backing a cashbox.
/// </summary>
public sealed record CashboxLedgerAccountInfo(
    Guid? CashboxId,
    Guid AccountId,
    string AccountCode,
    string Name,
    bool IsDefault,
    Guid? WarehouseId,
    string? Location);
