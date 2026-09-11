using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Core.Helpers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Reliability;
using PointOfSale.Infrastructure.Utilities;

namespace PointOfSale.API.Services.Inventory;

/// <summary>
/// PRODUCTION HARDENING — INVENTORY SYSTEM
/// 
/// Shared support for ProductVariantStock mutations with ZERO-TOLERANCE data integrity guarantees.
///
/// CONCURRENCY STRATEGY:
/// 1. Mutations MUST execute inside an explicit transaction on relational providers.
/// 2. Missing rows are materialized inside that transaction using provider-safe SQL.
/// 3. The stock row is then reloaded with a pessimistic write lock before quantity changes.
/// 4. Multi-row operations lock in DETERMINISTIC order (WarehouseId, VariantId) to prevent deadlocks.
///
/// TASK 1 — SAFE ROW MATERIALIZATION:
/// - UNIQUE constraint on (TenantId, VariantId, WarehouseId) enforced at DB level
/// - INSERT ... ON CONFLICT DO NOTHING pattern for PostgreSQL
/// - IF NOT EXISTS ... INSERT pattern for SQL Server with UPDLOCK/HOLDLOCK
/// - Exactly ONE row per (TenantId, VariantId, WarehouseId) - NO duplicates possible
///
/// TASK 2 — NEGATIVE STOCK PROTECTION:
/// - Validation happens AFTER lock acquisition, BEFORE mutation
/// - Callers MUST call ValidateStockAvailability or ValidateQuantityChange
///
/// TASK 3 — DEADLOCK PREVENTION:
/// - Use LockMultipleStocksAsync for multi-row operations
/// - Locks acquired in deterministic order: WarehouseId, then VariantId
///
/// TASK 4 — EVENT CONSISTENCY:
/// - Events built AFTER SaveChanges using BuildCommittedStockEventsAsync
/// - Durable stock-sync outbox rows staged BEFORE transaction commit
/// </summary>
internal static class ProductVariantStockMutationSupport
{
    public static async Task<ProductVariantStock?> LockStockForMutationAsync(
        ApplicationDbContext context,
        Guid tenantId,
        Guid warehouseId,
        Guid variantId,
        bool createIfMissing,
        CancellationToken cancellationToken = default)
    {
        EnsureMutationTransaction(context);

        await ProductVariantInventoryLineageAuthority.RequireCurrentStockMutationOwnerAsync(
            context,
            tenantId,
            variantId,
            warehouseId,
            "stock mutation",
            cancellationToken);

        if (createIfMissing)
        {
            await EnsureStockRowExistsAsync(
                context,
                tenantId,
                warehouseId,
                variantId,
                cancellationToken);
        }

        if (!context.Database.IsRelational())
        {
            var trackedStock = TryGetTrackedStock(context, tenantId, warehouseId, variantId);
            if (trackedStock != null)
            {
                return trackedStock;
            }
        }

        return await LoadStockForUpdateAsync(
            context,
            tenantId,
            warehouseId,
            variantId,
            cancellationToken);
    }

    public static async Task<ProductVariant?> ResolveVariantAsync(
        ApplicationDbContext context,
        Guid productId,
        Guid? productVariantId,
        CancellationToken cancellationToken = default)
    {
        if (productVariantId.HasValue)
        {
            return await context.ProductVariants
                .IgnoreQueryFilters()
                .WhereCurrentSemanticOwner()
                .FirstOrDefaultAsync(
                    variant =>
                        variant.Id == productVariantId.Value &&
                        variant.ProductId == productId &&
                        variant.IsActive &&
                        !variant.IsDeleted &&
                        !variant.Product.IsDeleted &&
                        variant.Product.IsActive,
                    cancellationToken);
        }

        return await context.ProductVariants
            .IgnoreQueryFilters()
            .Where(variant =>
                !variant.Product.IsDeleted &&
                variant.Product.IsActive)
            .WhereCurrentSemanticOwner()
            .FirstOrDefaultAsync(
                variant =>
                    variant.ProductId == productId &&
                    variant.IsDefault &&
                    variant.IsActive &&
                    !variant.IsDeleted,
                cancellationToken);
    }

    public static async Task<List<ProductVariantStockChangedEvent>> BuildCommittedStockEventsAsync(
        ApplicationDbContext context,
        Guid tenantId,
        IEnumerable<PendingProductVariantStockChange> pendingChanges,
        CancellationToken cancellationToken = default)
    {
        var changes = pendingChanges
            .GroupBy(change => (change.VariantId, change.WarehouseId))
            .Select(group => group.Last() with
            {
                OldQuantity = group.First().OldQuantity
            })
            .ToList();
        if (changes.Count == 0)
        {
            return new List<ProductVariantStockChangedEvent>();
        }

        var variantIds = changes
            .Select(change => change.VariantId)
            .Distinct()
            .ToList();
        var warehouseIds = changes
            .Select(change => change.WarehouseId)
            .Distinct()
            .ToList();

        var totalStockByVariant = await context.ProductVariantStocks
            .IgnoreQueryFilters()
            .WhereCurrentInventoryStockOwner(tenantId)
            .Where(stock => variantIds.Contains(stock.VariantId))
            .GroupBy(stock => stock.VariantId)
            .Select(group => new
            {
                VariantId = group.Key,
                TotalAvailable = Math.Max(0, group.Sum(stock => stock.Quantity - stock.ReservedQuantity))
            })
            .ToDictionaryAsync(item => item.VariantId, item => item.TotalAvailable, cancellationToken);

        var variantMetadata = await context.ProductVariants
            .IgnoreQueryFilters()
            .Where(variant => variant.TenantId == tenantId && variantIds.Contains(variant.Id))
            .Select(variant => new
            {
                variant.Id,
                variant.SKU,
                variant.StockUpdatedAtUtc,
                variant.ProjectionMutationVersion
            })
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var warehouseMetadata = await context.Warehouses
            .IgnoreQueryFilters()
            .Where(warehouse => warehouse.TenantId == tenantId && warehouseIds.Contains(warehouse.Id))
            .Select(warehouse => new
            {
                warehouse.Id,
                warehouse.Name
            })
            .ToDictionaryAsync(item => item.Id, item => item.Name, cancellationToken);

        var events = new List<ProductVariantStockChangedEvent>(changes.Count);
        foreach (var change in changes)
        {
            if (!variantMetadata.TryGetValue(change.VariantId, out var variant))
            {
                throw new InvalidOperationException($"Variant {change.VariantId} metadata was not found for stock sync.");
            }

            var stockUpdatedAtUtc = NormalizeUtcTimestamp(
                variant.StockUpdatedAtUtc
                    ?? throw new InvalidOperationException($"Variant {change.VariantId} is missing StockUpdatedAtUtc."));

            events.Add(new ProductVariantStockChangedEvent
            {
                TenantId = tenantId,
                VariantId = change.VariantId,
                ProductId = change.ProductId,
                SKU = change.SKU ?? variant.SKU,
                WarehouseId = change.WarehouseId,
                WarehouseName = change.WarehouseName ?? warehouseMetadata.GetValueOrDefault(change.WarehouseId),
                OldQuantity = change.OldQuantity,
                NewQuantity = change.NewQuantity,
                Reason = change.Reason,
                ReferenceId = change.ReferenceId,
                ReferenceType = change.ReferenceType,
                MutationSource = MutationSources.POS,
                TotalStockQuantity = totalStockByVariant.GetValueOrDefault(change.VariantId),
                CorrelationId = change.ReferenceId?.ToString("N") ?? Guid.NewGuid().ToString("N"),
                StockUpdatedAtUtc = stockUpdatedAtUtc,
                EventVersion = ProductEventVersionHelper.ToProductEventVersion(variant.ProjectionMutationVersion)
            });
        }

        return events;
    }

    public static async Task PublishCommittedStockEventsAsync(
        ApplicationDbContext context,
        Guid tenantId,
        IEnumerable<PendingProductVariantStockChange> pendingChanges,
        CancellationToken cancellationToken = default)
    {
        EnsureMutationTransaction(context);

        var changes = pendingChanges.ToList();
        if (changes.Count == 0)
        {
            return;
        }

        await TouchVariantStockTimestampsAsync(
            context,
            tenantId,
            changes.Select(change => change.VariantId),
            cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        var committedEvents = await BuildCommittedStockEventsAsync(
            context,
            tenantId,
            changes,
            cancellationToken);

        var productIds = committedEvents
            .Select(stockEvent => stockEvent.ProductId)
            .Distinct()
            .ToList();
        var products = await context.Products
            .IgnoreQueryFilters()
            .Where(product => product.TenantId == tenantId && productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        foreach (var stockEvent in committedEvents)
        {
            var payload = JsonSerializer.Serialize(stockEvent, typeof(ProductVariantStockChangedEvent));
            context.StockSyncOutboxMessages.Add(StockSyncOutboxMessage.Create(stockEvent, payload));
        }

        foreach (var productEvents in committedEvents.GroupBy(stockEvent => stockEvent.ProductId))
        {
            if (!products.TryGetValue(productEvents.Key, out var product))
            {
                throw new InvalidOperationException(
                    $"Product {productEvents.Key} metadata was not found for marketplace stock projection.");
            }

            var latestStockEvent = productEvents.MaxBy(stockEvent => stockEvent.StockUpdatedAtUtc)!;
            var marketplaceEventVersion = await MarketplaceProductProjectionVersion.AdvanceAsync(
                context,
                product,
                cancellationToken);
            var marketplaceEvent = new PointOfSale.Core.Events.Marketplace.ProductUpdatedEvent
            {
                OccurredAtUtc = latestStockEvent.StockUpdatedAtUtc,
                CorrelationId = latestStockEvent.CorrelationId ?? latestStockEvent.EventId.ToString("D"),
                EntityId = product.Id,
                AggregateRootId = product.Id,
                TenantId = tenantId,
                Version = marketplaceEventVersion,
                ChangedFields = ["Stock"]
            };
            var marketplacePayload = JsonSerializer.Serialize(
                marketplaceEvent,
                typeof(PointOfSale.Core.Events.Marketplace.ProductUpdatedEvent));
            context.MarketplaceEventOutboxMessages.Add(
                MarketplaceEventOutboxMessage.Create(marketplaceEvent, marketplacePayload));
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public static PendingProductVariantStockChange BuildPendingStockChange(
        Guid variantId,
        Guid productId,
        Guid warehouseId,
        decimal oldQuantity,
        decimal newQuantity,
        string reason,
        Guid? referenceId = null,
        string? referenceType = null,
        string? sku = null,
        string? warehouseName = null)
    {
        return new PendingProductVariantStockChange(
            variantId,
            productId,
            warehouseId,
            oldQuantity,
            newQuantity,
            reason,
            referenceId,
            referenceType,
            sku,
            warehouseName);
    }

    /// <summary>
    /// TASK 3: GLOBAL LOCK ORDERING — DEADLOCK PREVENTION
    /// 
    /// Locks multiple stock rows in DETERMINISTIC order (WarehouseId, VariantId).
    /// This prevents deadlocks when multiple transactions need to lock multiple rows.
    /// 
    /// USAGE: For transfers, multi-item sales, batch adjustments.
    /// 
    /// CRITICAL: Callers MUST use this method for ALL multi-row operations.
    /// </summary>
    /// <param name="context">Database context with active transaction</param>
    /// <param name="tenantId">Tenant identifier</param>
    /// <param name="lockTargets">Stock rows to lock, will be ordered automatically</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary mapping (WarehouseId, VariantId) to locked stock row</returns>
    public static async Task<Dictionary<(Guid WarehouseId, Guid VariantId), ProductVariantStock>> LockMultipleStocksAsync(
        ApplicationDbContext context,
        Guid tenantId,
        IEnumerable<StockLockTarget> lockTargets,
        CancellationToken cancellationToken = default)
    {
        EnsureMutationTransaction(context);

        // TASK 3: Sort by WarehouseId, then VariantId for deadlock prevention
        var orderedTargets = lockTargets
            .OrderBy(t => t.WarehouseId)
            .ThenBy(t => t.VariantId)
            .ToList();

        var result = new Dictionary<(Guid WarehouseId, Guid VariantId), ProductVariantStock>();

        foreach (var target in orderedTargets)
        {
            var stock = await LockStockForMutationAsync(
                context,
                tenantId,
                target.WarehouseId,
                target.VariantId,
                target.CreateIfMissing,
                cancellationToken);

            if (stock != null)
            {
                result[(target.WarehouseId, target.VariantId)] = stock;
            }
        }

        return result;
    }

    /// <summary>
    /// TASK 2: NEGATIVE STOCK PROTECTION
    /// 
    /// Validates that sufficient stock is available AFTER acquiring lock.
    /// This MUST be called after LockStockForMutationAsync and BEFORE any mutation.
    /// 
    /// CRITICAL: This validation happens INSIDE the transaction, AFTER lock acquisition.
    /// </summary>
    public static void ValidateAvailableQuantity(
        ProductVariantStock stock,
        decimal requestedQuantity,
        bool allowNegativeStock = false,
        string operationName = "operation")
    {
        if (allowNegativeStock)
        {
            return; // Explicitly allowed
        }

        var available = stock.AvailableQuantity; // Quantity - ReservedQuantity

        if (available < requestedQuantity)
        {
            throw new InsufficientStockException(
                stock.VariantId,
                stock.WarehouseId,
                available,
                requestedQuantity,
                operationName);
        }
    }

    /// <summary>
    /// TASK 2: NEGATIVE STOCK PROTECTION
    /// 
    /// Validates that a quantity change won't result in negative stock.
    /// </summary>
    public static void ValidateQuantityChange(
        ProductVariantStock stock,
        decimal quantityChange,
        bool allowNegativeStock = false,
        string operationName = "operation")
    {
        if (allowNegativeStock || quantityChange >= 0)
        {
            return; // No validation needed
        }

        var resultingQuantity = stock.Quantity + quantityChange;

        if (resultingQuantity < 0)
        {
            throw new InsufficientStockException(
                stock.VariantId,
                stock.WarehouseId,
                stock.Quantity,
                Math.Abs(quantityChange),
                operationName);
        }
    }

    private static void EnsureMutationTransaction(ApplicationDbContext context)
    {
        if (context.Database.IsRelational() && context.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException(
                "Inventory mutations must run inside an explicit database transaction.");
        }
    }

    private static async Task<ProductVariantStock?> LoadStockForUpdateAsync(
        ApplicationDbContext context,
        Guid tenantId,
        Guid warehouseId,
        Guid variantId,
        CancellationToken cancellationToken)
    {
        IQueryable<ProductVariantStock> query;

        if (IsPostgresProvider(context))
        {
            query = context.ProductVariantStocks
                .FromSqlInterpolated($@"
                    SELECT *, xmin FROM ""ProductVariantStocks""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""WarehouseId"" = {warehouseId}
                      AND ""VariantId"" = {variantId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                // SECURITY: IgnoreQueryFilters is intentional because the lock SQL
                // already reapplies tenant/warehouse/variant predicates explicitly.
                .IgnoreQueryFilters();
        }
        else if (IsSqlServerProvider(context))
        {
            query = context.ProductVariantStocks
                .FromSqlInterpolated($@"
                    SELECT * FROM [ProductVariantStocks] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [WarehouseId] = {warehouseId}
                      AND [VariantId] = {variantId}
                      AND [IsDeleted] = 0")
                // SECURITY: IgnoreQueryFilters is intentional because the lock SQL
                // already reapplies tenant/warehouse/variant predicates explicitly.
                .IgnoreQueryFilters();
        }
        else
        {
            query = context.ProductVariantStocks
                // SECURITY: IgnoreQueryFilters is intentional here only to bypass the
                // global filter while the explicit tenant predicate is reapplied below.
                .IgnoreQueryFilters()
                .Where(stock =>
                    stock.TenantId == tenantId &&
                    stock.WarehouseId == warehouseId &&
                    stock.VariantId == variantId &&
                    !stock.IsDeleted);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private static async Task EnsureStockRowExistsAsync(
        ApplicationDbContext context,
        Guid tenantId,
        Guid warehouseId,
        Guid variantId,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;

        if (!context.Database.IsRelational())
        {
            var existing = await context.ProductVariantStocks
                // SECURITY: IgnoreQueryFilters is intentional here so a previously
                // soft-deleted stock row can be restored for the same tenant row key.
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    stock =>
                        stock.TenantId == tenantId &&
                        stock.WarehouseId == warehouseId &&
                        stock.VariantId == variantId,
                    cancellationToken);

            if (existing == null)
            {
                context.ProductVariantStocks.Add(new ProductVariantStock
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    WarehouseId = warehouseId,
                    VariantId = variantId,
                    Quantity = 0,
                    ReservedQuantity = 0,
                    DamagedQuantity = 0,
                    CreatedAt = utcNow,
                    UpdatedAt = utcNow,
                    IsDeleted = false
                });
            }
            else if (existing.IsDeleted)
            {
                existing.IsDeleted = false;
                existing.UpdatedAt = utcNow;
            }

            return;
        }

        if (IsPostgresProvider(context))
        {
            await context.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE ""ProductVariantStocks""
                SET ""IsDeleted"" = FALSE,
                    ""UpdatedAt"" = {utcNow}
                WHERE ""TenantId"" = {tenantId}
                  AND ""WarehouseId"" = {warehouseId}
                  AND ""VariantId"" = {variantId}
                  AND ""IsDeleted"" = TRUE;",
                cancellationToken);

            await context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""ProductVariantStocks""
                    (""Id"", ""TenantId"", ""VariantId"", ""WarehouseId"", ""Quantity"", ""ReservedQuantity"", ""DamagedQuantity"", ""ProjectionMutationVersion"", ""CreatedAt"", ""UpdatedAt"", ""IsDeleted"")
                VALUES
                    ({Guid.NewGuid()}, {tenantId}, {variantId}, {warehouseId}, 0, 0, 0, 0, {utcNow}, {utcNow}, FALSE)
                ON CONFLICT (""TenantId"", ""VariantId"", ""WarehouseId"") DO NOTHING;",
                cancellationToken);

            return;
        }

        if (IsSqlServerProvider(context))
        {
            await context.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE [ProductVariantStocks]
                SET [IsDeleted] = 0,
                    [UpdatedAt] = {utcNow}
                WHERE [TenantId] = {tenantId}
                  AND [WarehouseId] = {warehouseId}
                  AND [VariantId] = {variantId}
                  AND [IsDeleted] = 1;",
                cancellationToken);

            await context.Database.ExecuteSqlInterpolatedAsync($@"
                IF NOT EXISTS (
                    SELECT 1
                    FROM [ProductVariantStocks] WITH (UPDLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [WarehouseId] = {warehouseId}
                      AND [VariantId] = {variantId}
                )
                BEGIN
                    INSERT INTO [ProductVariantStocks]
                        ([Id], [TenantId], [VariantId], [WarehouseId], [Quantity], [ReservedQuantity], [DamagedQuantity], [ProjectionMutationVersion], [CreatedAt], [UpdatedAt], [IsDeleted])
                    VALUES
                        ({Guid.NewGuid()}, {tenantId}, {variantId}, {warehouseId}, 0, 0, 0, 0, {utcNow}, {utcNow}, 0)
                END;",
                cancellationToken);

            return;
        }

        var fallbackExisting = await context.ProductVariantStocks
            // SECURITY: IgnoreQueryFilters is intentional here so a previously
            // soft-deleted stock row can be restored for the same tenant row key.
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                stock =>
                    stock.TenantId == tenantId &&
                    stock.WarehouseId == warehouseId &&
                    stock.VariantId == variantId,
                cancellationToken);

        if (fallbackExisting == null)
        {
            context.ProductVariantStocks.Add(new ProductVariantStock
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                WarehouseId = warehouseId,
                VariantId = variantId,
                Quantity = 0,
                ReservedQuantity = 0,
                DamagedQuantity = 0,
                CreatedAt = utcNow,
                UpdatedAt = utcNow,
                IsDeleted = false
            });
        }
        else if (fallbackExisting.IsDeleted)
        {
            fallbackExisting.IsDeleted = false;
            fallbackExisting.UpdatedAt = utcNow;
        }
    }

    private static bool IsPostgresProvider(ApplicationDbContext context) =>
        context.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsSqlServerProvider(ApplicationDbContext context) =>
        context.Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

    private static DateTime NormalizeUtcTimestamp(DateTime timestamp) =>
        StockProjectionTimestamp.NormalizeUtc(timestamp);

    private static async Task TouchVariantStockTimestampsAsync(
        ApplicationDbContext context,
        Guid tenantId,
        IEnumerable<Guid> variantIds,
        CancellationToken cancellationToken)
    {
        var orderedVariantIds = variantIds
            .Distinct()
            .OrderBy(id => id)
            .ToList();

        foreach (var variantId in orderedVariantIds)
        {
            var variant = await LoadVariantForStockSyncAsync(
                context,
                tenantId,
                variantId,
                cancellationToken)
                ?? throw new InvalidOperationException($"Product variant {variantId} was not found for stock sync.");

            variant.StockUpdatedAtUtc = StockProjectionTimestamp.Next(variant.StockUpdatedAtUtc);
            variant.UpdatedAt = variant.StockUpdatedAtUtc;
        }
    }

    private static async Task<ProductVariant?> LoadVariantForStockSyncAsync(
        ApplicationDbContext context,
        Guid tenantId,
        Guid variantId,
        CancellationToken cancellationToken)
    {
        var staleTrackedVariant = context.ChangeTracker
            .Entries<ProductVariant>()
            .FirstOrDefault(entry => entry.Entity.Id == variantId);
        if (staleTrackedVariant != null)
        {
            // Stock projection timestamping is a post-sale side effect. If the sale already
            // loaded the variant to capture an immutable cost snapshot, a concurrent committed
            // cost mutation can make that tracked row version stale. Reload the locked SQL row
            // instead of letting a stale tracked entity abort the sale commit.
            staleTrackedVariant.State = EntityState.Detached;
        }

        IQueryable<ProductVariant> query;

        if (IsPostgresProvider(context))
        {
            query = context.ProductVariants
                .FromSqlInterpolated($@"
                    SELECT *, xmin FROM ""ProductVariants""
                    WHERE ""TenantId"" = {tenantId}
                      AND ""Id"" = {variantId}
                      AND ""IsDeleted"" = FALSE
                    FOR UPDATE")
                .IgnoreQueryFilters();
        }
        else if (IsSqlServerProvider(context))
        {
            query = context.ProductVariants
                .FromSqlInterpolated($@"
                    SELECT * FROM [ProductVariants] WITH (UPDLOCK, ROWLOCK, HOLDLOCK)
                    WHERE [TenantId] = {tenantId}
                      AND [Id] = {variantId}
                      AND [IsDeleted] = 0")
                .IgnoreQueryFilters();
        }
        else
        {
            query = context.ProductVariants
                .IgnoreQueryFilters()
                .Where(variant =>
                    variant.TenantId == tenantId &&
                    variant.Id == variantId &&
                    !variant.IsDeleted);
        }

        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    private static ProductVariantStock? TryGetTrackedStock(
        ApplicationDbContext context,
        Guid tenantId,
        Guid warehouseId,
        Guid variantId)
    {
        return context.ChangeTracker
            .Entries<ProductVariantStock>()
            .Where(entry =>
                entry.State != EntityState.Detached &&
                entry.State != EntityState.Deleted &&
                entry.Entity.TenantId == tenantId &&
                entry.Entity.WarehouseId == warehouseId &&
                entry.Entity.VariantId == variantId &&
                !entry.Entity.IsDeleted)
            .Select(entry => entry.Entity)
            .FirstOrDefault();
    }
}

public record PendingProductVariantStockChange(
    Guid VariantId,
    Guid ProductId,
    Guid WarehouseId,
    decimal OldQuantity,
    decimal NewQuantity,
    string Reason,
    Guid? ReferenceId = null,
    string? ReferenceType = null,
    string? SKU = null,
    string? WarehouseName = null);
