namespace PointOfSale.Core.Enums;

/// <summary>
/// SQL-authoritative semantic ownership state for product variants.
/// Current variants may own SKU/barcode, inventory, reservations, pricing, media, and publication
/// visibility. Superseded variants are lineage records only and must fail closed on public surfaces.
/// </summary>
public enum ProductVariantSemanticLifecycleState
{
    Current = 0,
    Merged = 1,
    SplitParent = 2,
    Detached = 3
}
