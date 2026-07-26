using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Reliability;

/// <summary>
/// SQL-authoritative semantic ownership guard for variant merge/split lineage.
/// Merge and split payloads are operation intent only; current PostgreSQL variant rows decide
/// which variant owns SKU/barcode, inventory, reservations, media, publication, and exports.
/// </summary>
public static class ProductVariantMergeSplitAuthority
{
    public const string MergeOperationKind = "variant-merge";
    public const string SplitOperationKind = "variant-split";

    public static IQueryable<ProductVariant> WhereCurrentSemanticOwner(this IQueryable<ProductVariant> query)
    {
        return query.Where(variant =>
            variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
            variant.SemanticSupersededAtUtc == null &&
            variant.SemanticReplacementVariantId == null);
    }

    public static bool IsCurrentSemanticOwner(ProductVariant variant)
    {
        return variant.SemanticLifecycleState == ProductVariantSemanticLifecycleState.Current &&
               variant.SemanticSupersededAtUtc == null &&
               variant.SemanticReplacementVariantId == null;
    }

    public static Task<bool> IsCurrentSemanticOwnerAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid variantId,
        CancellationToken cancellationToken = default)
    {
        return dbContext.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .WhereCurrentSemanticOwner()
            .AnyAsync(variant =>
                variant.TenantId == tenantId &&
                variant.Id == variantId &&
                !variant.IsDeleted,
                cancellationToken);
    }

    public static async Task<VariantMergeSplitSemanticSnapshot?> LoadSemanticSnapshotAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid variantId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.ProductVariants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(variant => variant.TenantId == tenantId && variant.Id == variantId)
            .Select(variant => new VariantMergeSplitSemanticSnapshot(
                variant.Id,
                variant.ProductId,
                variant.SemanticLifecycleState,
                variant.SemanticReplacementVariantId,
                variant.SemanticLineageRootVariantId ?? variant.Id,
                variant.SemanticOperationId,
                variant.SemanticOperationKind,
                variant.SemanticSupersededAtUtc,
                variant.IsDeleted,
                variant.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public static async Task RequireCurrentSemanticOwnerAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid variantId,
        string operation,
        CancellationToken cancellationToken = default)
    {
        if (await IsCurrentSemanticOwnerAsync(dbContext, tenantId, variantId, cancellationToken))
        {
            return;
        }

        var snapshot = await LoadSemanticSnapshotAsync(dbContext, tenantId, variantId, cancellationToken);
        throw new InvalidOperationException(
            $"VARIANT_MERGE_SPLIT_SEMANTIC_OWNER_REQUIRED: Variant {variantId} is not the current semantic owner for {operation}. " +
            $"State={snapshot?.SemanticLifecycleState.ToString() ?? "Missing"}; " +
            $"Replacement={snapshot?.SemanticReplacementVariantId?.ToString("D") ?? "none"}; " +
            $"OperationId={snapshot?.SemanticOperationId?.ToString("D") ?? "none"}.");
    }

    public static async Task<VariantMergeSplitApplyResult> ApplyMergeAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid sourceVariantId,
        Guid targetVariantId,
        Guid operationId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        await RejectOpenReservationsForSupersededVariantsAsync(
            dbContext,
            tenantId,
            [sourceVariantId],
            "variant merge",
            cancellationToken);

        var variants = await dbContext.ProductVariants
            .IgnoreQueryFilters()
            .Where(variant =>
                variant.TenantId == tenantId &&
                (variant.Id == sourceVariantId || variant.Id == targetVariantId) &&
                !variant.IsDeleted)
            .ToListAsync(cancellationToken);

        var source = variants.SingleOrDefault(variant => variant.Id == sourceVariantId)
            ?? throw new InvalidOperationException($"VARIANT_MERGE_SPLIT_SOURCE_MISSING: Source variant {sourceVariantId} was not found.");
        var target = variants.SingleOrDefault(variant => variant.Id == targetVariantId)
            ?? throw new InvalidOperationException($"VARIANT_MERGE_SPLIT_TARGET_MISSING: Target variant {targetVariantId} was not found.");

        var wasAlreadySuperseded = !IsCurrentSemanticOwner(source);
        MarkMergedInto(source, target, operationId, utcNow);

        return new VariantMergeSplitApplyResult(
            operationId,
            MergeOperationKind,
            source.Id,
            [target.Id],
            wasAlreadySuperseded);
    }

    public static async Task<VariantMergeSplitApplyResult> ApplySplitAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        Guid parentVariantId,
        IReadOnlyCollection<Guid> childVariantIds,
        Guid operationId,
        DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        await RejectOpenReservationsForSupersededVariantsAsync(
            dbContext,
            tenantId,
            [parentVariantId],
            "variant split",
            cancellationToken);

        var normalizedChildIds = childVariantIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        var allIds = normalizedChildIds
            .Append(parentVariantId)
            .Distinct()
            .ToArray();

        var variants = await dbContext.ProductVariants
            .IgnoreQueryFilters()
            .Where(variant =>
                variant.TenantId == tenantId &&
                allIds.Contains(variant.Id) &&
                !variant.IsDeleted)
            .ToListAsync(cancellationToken);

        var parent = variants.SingleOrDefault(variant => variant.Id == parentVariantId)
            ?? throw new InvalidOperationException($"VARIANT_MERGE_SPLIT_PARENT_MISSING: Parent variant {parentVariantId} was not found.");
        var children = normalizedChildIds
            .Select(childId => variants.SingleOrDefault(variant => variant.Id == childId)
                ?? throw new InvalidOperationException($"VARIANT_MERGE_SPLIT_CHILD_MISSING: Child variant {childId} was not found."))
            .ToArray();

        var wasAlreadySuperseded = !IsCurrentSemanticOwner(parent);
        MarkSplitParent(parent, children, operationId, utcNow);

        return new VariantMergeSplitApplyResult(
            operationId,
            SplitOperationKind,
            parent.Id,
            children.Select(child => child.Id).ToArray(),
            wasAlreadySuperseded);
    }

    public static void MarkMergedInto(
        ProductVariant source,
        ProductVariant target,
        Guid operationId,
        DateTime utcNow)
    {
        if (source.Id == target.Id)
        {
            throw new InvalidOperationException("VARIANT_MERGE_SPLIT_SELF_MERGE_REJECTED: A variant cannot merge into itself.");
        }

        EnsureSameTenant(source, target, "merge");

        if (!IsCurrentSemanticOwner(target))
        {
            throw new InvalidOperationException(
                "VARIANT_MERGE_SPLIT_TARGET_NOT_CURRENT: A merge target must be the current semantic owner.");
        }

        if (!IsCurrentSemanticOwner(source))
        {
            CollapseIdempotentReplay(source, operationId, MergeOperationKind, target.Id);
            return;
        }

        source.SemanticLifecycleState = ProductVariantSemanticLifecycleState.Merged;
        source.SemanticReplacementVariantId = target.Id;
        source.SemanticLineageRootVariantId = source.SemanticLineageRootVariantId ?? source.Id;
        source.SemanticOperationId = operationId;
        source.SemanticOperationKind = MergeOperationKind;
        source.SemanticSupersededAtUtc = utcNow;
        source.IsActive = false;
        source.SKU = null;
        source.Barcode = null;
        source.MediaId = null;
        source.UpdatedAt = utcNow;

        target.SemanticLineageRootVariantId ??= source.SemanticLineageRootVariantId ?? source.Id;
        target.UpdatedAt = utcNow;
    }

    public static void MarkSplitParent(
        ProductVariant parent,
        IReadOnlyCollection<ProductVariant> children,
        Guid operationId,
        DateTime utcNow)
    {
        if (children.Count == 0)
        {
            throw new InvalidOperationException("VARIANT_MERGE_SPLIT_CHILDREN_REQUIRED: A split must produce at least one child variant.");
        }

        foreach (var child in children)
        {
            if (child.Id == parent.Id)
            {
                throw new InvalidOperationException("VARIANT_MERGE_SPLIT_SELF_SPLIT_REJECTED: A split child cannot be the parent variant.");
            }

            EnsureSameTenant(parent, child, "split");

            if (!IsCurrentSemanticOwner(child))
            {
                throw new InvalidOperationException(
                    $"VARIANT_MERGE_SPLIT_CHILD_NOT_CURRENT: Split child {child.Id} is not a current semantic owner.");
            }

            child.SemanticLineageRootVariantId = parent.SemanticLineageRootVariantId ?? parent.Id;
            child.SemanticOperationId ??= operationId;
            child.SemanticOperationKind ??= SplitOperationKind;
            child.UpdatedAt = utcNow;
        }

        if (!IsCurrentSemanticOwner(parent))
        {
            CollapseIdempotentReplay(parent, operationId, SplitOperationKind, null);
            return;
        }

        parent.SemanticLifecycleState = ProductVariantSemanticLifecycleState.SplitParent;
        parent.SemanticLineageRootVariantId = parent.SemanticLineageRootVariantId ?? parent.Id;
        parent.SemanticOperationId = operationId;
        parent.SemanticOperationKind = SplitOperationKind;
        parent.SemanticSupersededAtUtc = utcNow;
        parent.IsActive = false;
        parent.SKU = null;
        parent.Barcode = null;
        parent.MediaId = null;
        parent.UpdatedAt = utcNow;
    }

    public static async Task RejectOpenReservationsForSupersededVariantsAsync(
        ApplicationDbContext dbContext,
        Guid tenantId,
        IEnumerable<Guid> variantIds,
        string operation,
        CancellationToken cancellationToken = default)
    {
        var ids = variantIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return;
        }

        var openReservation = await dbContext.InventoryReservations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(reservation =>
                reservation.TenantId == tenantId &&
                ids.Contains(reservation.VariantId) &&
                !reservation.IsDeleted &&
                (reservation.Status == InventoryReservationStatus.Active ||
                 reservation.Status == InventoryReservationStatus.PartiallyCommitted ||
                 reservation.Status == InventoryReservationStatus.PartiallyReleased) &&
                reservation.RemainingReservedQuantity > 0m)
            .Select(reservation => new
            {
                reservation.Id,
                reservation.VariantId,
                reservation.OrderId,
                reservation.OrderItemId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (openReservation == null)
        {
            return;
        }

        throw new InvalidOperationException(
            $"VARIANT_MERGE_SPLIT_OPEN_RESERVATION_REJECTED: {operation} cannot supersede variant {openReservation.VariantId} " +
            $"while reservation {openReservation.Id} for order {openReservation.OrderId}/item {openReservation.OrderItemId} remains open.");
    }

    private static void CollapseIdempotentReplay(
        ProductVariant variant,
        Guid operationId,
        string operationKind,
        Guid? expectedReplacementVariantId)
    {
        if (variant.SemanticOperationId == operationId &&
            string.Equals(variant.SemanticOperationKind, operationKind, StringComparison.Ordinal) &&
            (expectedReplacementVariantId == null || variant.SemanticReplacementVariantId == expectedReplacementVariantId))
        {
            return;
        }

        throw new InvalidOperationException(
            $"VARIANT_MERGE_SPLIT_STALE_REPLAY_REJECTED: Variant {variant.Id} was already superseded by a different semantic operation.");
    }

    private static void EnsureSameTenant(ProductVariant left, ProductVariant right, string operation)
    {
        if (left.TenantId == right.TenantId)
        {
            return;
        }

        throw new InvalidOperationException(
            $"VARIANT_MERGE_SPLIT_TENANT_MISMATCH: Cannot {operation} variants across tenants.");
    }
}

public sealed record VariantMergeSplitSemanticSnapshot(
    Guid VariantId,
    Guid ProductId,
    ProductVariantSemanticLifecycleState SemanticLifecycleState,
    Guid? SemanticReplacementVariantId,
    Guid SemanticLineageRootVariantId,
    Guid? SemanticOperationId,
    string? SemanticOperationKind,
    DateTime? SemanticSupersededAtUtc,
    bool IsDeleted,
    bool IsActive);

public sealed record VariantMergeSplitApplyResult(
    Guid OperationId,
    string OperationKind,
    Guid SupersededVariantId,
    IReadOnlyCollection<Guid> CurrentOwnerVariantIds,
    bool WasReplay);
