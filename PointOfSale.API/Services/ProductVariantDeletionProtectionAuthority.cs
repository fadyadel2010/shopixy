using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services;

/// <summary>
/// WAVE 2A — Safe Product/Variant deletion authority.
///
/// Single source of truth for "can this Product/Variant safely disappear
/// from active use without destroying historical business truth?".
///
/// Every protected dependency below has source evidence in the domain model:
/// <list type="bullet">
/// <item>Current stock (incl. reserved/damaged): <c>ProductVariantStock</c> is the
/// inventory source of truth (<c>ProductVariantStock.cs</c>); reserved stock backs
/// open storefront/CommerceHub reservations.</item>
/// <item>Purchase history: <c>PurchaseInvoiceItem</c> (ProductId + ProductVariantId).</item>
/// <item>Sales history + COGS: <c>SalesInvoiceItem</c> carries immutable
/// <c>CostPriceSnapshot</c>/<c>CostAmountSnapshot</c>.</item>
/// <item>Returns + COGS reversal: <c>ReturnItem</c> carries immutable
/// <c>CostPriceSnapshot</c>/<c>CostAmountSnapshot</c>.</item>
/// <item>Transfers: <c>StockTransferItem</c> (ProductId + ProductVariantId).</item>
/// <item>Movements: <c>StockMovement</c> audit log (ProductId + ProductVariantId).</item>
/// <item>Storefront order history + COGS: <c>OrderItem</c> is an immutable SKU
/// snapshot scoped to its <c>Order.TenantId</c>; money and cost live on the item.</item>
/// <item>CommerceHub order history + COGS: <c>CommerceHubSubOrderItem</c> carries
/// immutable <c>UnitCostSnapshot</c>/<c>CostAmountSnapshot</c>; tenant scoping is
/// <c>SubOrder.StoreId</c> (StoreId IS the store tenant —
/// <c>CommerceHubCheckoutService</c>, <c>InventoryConsistencyService</c>).</item>
/// <item>Reservations: <c>InventoryReservation</c> with open
/// (reserved − committed − released) quantity.</item>
/// <item>Marketplace exposure: published <c>CommerceHubListing</c> rows.</item>
/// </list>
///
/// Non-blocking by design (transient or non-financial references that the
/// owning workflow revalidates): POS carts (<c>PosCartItem</c>, checkout
/// revalidates sellability), wishlists, affiliate links, promotions.
/// </summary>
public sealed record ProductVariantDeleteBlocker(
    string Code,
    string MessageEn,
    string MessageAr);

public static class ProductVariantDeletionProtectionAuthority
{
    /// <summary>
    /// Returns every protected dependency for a Product (variantId null) or a
    /// single Variant, in stable evaluation order. Empty means the entity may
    /// follow the existing safe deletion lifecycle. All checks are tenant-safe:
    /// line items scope through their authoritative parent tenant
    /// (invoice/order tenant, or <c>CommerceHubSubOrder.StoreId</c>).
    /// </summary>
    public static async Task<IReadOnlyList<ProductVariantDeleteBlocker>> GetBlockersAsync(
        ApplicationDbContext context,
        Guid tenantId,
        Guid productId,
        Guid? variantId,
        CancellationToken cancellationToken = default)
    {
        var blockers = new List<ProductVariantDeleteBlocker>();
        var isVariant = variantId.HasValue;
        var vid = variantId ?? Guid.Empty;

        // Model-mapped guards: production always uses the full
        // ApplicationDbContext model, but focused test harnesses trim
        // unrelated history tables out of their SQLite/InMemory models. A
        // check whose entity type is not mapped is skipped (nothing to
        // protect in that store) instead of throwing. A dedicated test
        // (ProductVariantDeletionSafetyTests) asserts every protected entity
        // IS mapped in the full production model, so this can never silently
        // degrade production protection.
        var model = context.Model;
        var hasStocks = model.FindEntityType(typeof(ProductVariantStock)) is not null;
        var hasPurchases = model.FindEntityType(typeof(PurchaseInvoiceItem)) is not null;
        var hasSales = model.FindEntityType(typeof(SalesInvoiceItem)) is not null;
        var hasReturns = model.FindEntityType(typeof(ReturnItem)) is not null;
        var hasTransfers = model.FindEntityType(typeof(StockTransferItem)) is not null;
        var hasMovementHistory = model.FindEntityType(typeof(StockMovement)) is not null;
        var hasStorefrontOrders = model.FindEntityType(typeof(OrderItem)) is not null;
        var hasCommerceHubOrders = model.FindEntityType(typeof(CommerceHubSubOrderItem)) is not null;
        var hasReservationHistory = model.FindEntityType(typeof(InventoryReservation)) is not null;
        var hasListings = model.FindEntityType(typeof(CommerceHubListing)) is not null;

        // 1. Current stock (sellable + reserved + damaged physical on hand).
        var hasStock = hasStocks && await context.ProductVariantStocks.AnyAsync(
            stock =>
                stock.TenantId == tenantId &&
                !stock.IsDeleted &&
                stock.Variant.TenantId == tenantId &&
                !stock.Variant.IsDeleted &&
                stock.Variant.ProductId == productId &&
                (!isVariant || stock.VariantId == vid) &&
                (stock.Quantity != 0 || stock.ReservedQuantity != 0 || stock.DamagedQuantity != 0),
            cancellationToken);
        if (hasStock)
        {
            blockers.Add(isVariant
                ? new ProductVariantDeleteBlocker(
                    "VARIANT_HAS_STOCK",
                    "Cannot delete this variant: current stock exists in warehouses. Deactivate the variant instead of deleting it.",
                    "لا يمكن حذف هذا المتغير: يوجد رصيد حالي بالمخازن. قم بتعطيل المتغير بدلاً من حذفه.")
                : new ProductVariantDeleteBlocker(
                    "PRODUCT_HAS_STOCK",
                    "Cannot delete product: stock exists in warehouses. Please set the product as inactive instead.",
                    "لا يمكن حذف الصنف لوجود رصيد بالمخازن. يرجى تعطيل الصنف بدلاً من حذفه."));
        }

        // 2. Purchase history.
        var usedInPurchase = hasPurchases && await context.PurchaseInvoiceItems.AnyAsync(
            pii =>
                pii.ProductId == productId &&
                (!isVariant || pii.ProductVariantId == vid) &&
                !pii.IsDeleted &&
                pii.PurchaseInvoice.TenantId == tenantId &&
                !pii.PurchaseInvoice.IsDeleted,
            cancellationToken);
        if (usedInPurchase)
        {
            blockers.Add(isVariant
                ? new ProductVariantDeleteBlocker(
                    "VARIANT_USED_IN_PURCHASES",
                    "Cannot delete this variant: it has purchase history. Purchase records must keep referencing it. Deactivate the variant instead of deleting it.",
                    "لا يمكن حذف هذا المتغير: لديه سجل مشتريات. يجب أن تبقى سجلات الشراء مرتبطة به. قم بتعطيل المتغير بدلاً من حذفه.")
                : new ProductVariantDeleteBlocker(
                    "PRODUCT_USED_IN_PURCHASES",
                    "Product is used in purchase transactions and cannot be deleted. Please set the product as inactive instead.",
                    "الصنف مستخدم في فواتير الشراء ولا يمكن حذفه. يرجى تعطيل الصنف بدلاً من حذفه."));
        }

        // 3. Sales history (carries immutable COGS snapshots).
        var usedInSales = hasSales && await context.SalesInvoiceItems.AnyAsync(
            sii =>
                sii.ProductId == productId &&
                (!isVariant || sii.ProductVariantId == vid) &&
                !sii.IsDeleted &&
                sii.SalesInvoice.TenantId == tenantId &&
                !sii.SalesInvoice.IsDeleted,
            cancellationToken);
        if (usedInSales)
        {
            blockers.Add(isVariant
                ? new ProductVariantDeleteBlocker(
                    "VARIANT_USED_IN_SALES",
                    "Cannot delete this variant: it has sales history with cost records. Sales records must keep referencing it. Deactivate the variant instead of deleting it.",
                    "لا يمكن حذف هذا المتغير: لديه سجل مبيعات مع سجلات التكلفة. يجب أن تبقى سجلات البيع مرتبطة به. قم بتعطيل المتغير بدلاً من حذفه.")
                : new ProductVariantDeleteBlocker(
                    "PRODUCT_USED_IN_SALES",
                    "Product is used in sales transactions and cannot be deleted. Please set the product as inactive instead.",
                    "الصنف مستخدم في فواتير البيع ولا يمكن حذفه. يرجى تعطيل الصنف بدلاً من حذفه."));
        }

        // 4. Returns (carry immutable COGS-reversal snapshots).
        var usedInReturns = hasReturns && await context.ReturnItems.AnyAsync(
            ri =>
                ri.ProductId == productId &&
                (!isVariant || ri.ProductVariantId == vid) &&
                !ri.IsDeleted &&
                ri.Return.TenantId == tenantId &&
                !ri.Return.IsDeleted,
            cancellationToken);
        if (usedInReturns)
        {
            blockers.Add(isVariant
                ? new ProductVariantDeleteBlocker(
                    "VARIANT_USED_IN_RETURNS",
                    "Cannot delete this variant: it has return history with cost records. Return records must keep referencing it. Deactivate the variant instead of deleting it.",
                    "لا يمكن حذف هذا المتغير: لديه سجل مرتجعات مع سجلات التكلفة. يجب أن تبقى سجلات المرتجعات مرتبطة به. قم بتعطيل المتغير بدلاً من حذفه.")
                : new ProductVariantDeleteBlocker(
                    "PRODUCT_USED_IN_RETURNS",
                    "Product is used in return transactions and cannot be deleted. Please set the product as inactive instead.",
                    "الصنف مستخدم في فواتير المرتجعات ولا يمكن حذفه. يرجى تعطيل الصنف بدلاً من حذفه."));
        }

        // 5. Stock transfers.
        var usedInTransfers = hasTransfers && await context.StockTransferItems.AnyAsync(
            sti =>
                sti.ProductId == productId &&
                (!isVariant || sti.ProductVariantId == vid) &&
                !sti.IsDeleted &&
                sti.StockTransfer.TenantId == tenantId &&
                !sti.StockTransfer.IsDeleted,
            cancellationToken);
        if (usedInTransfers)
        {
            blockers.Add(isVariant
                ? new ProductVariantDeleteBlocker(
                    "VARIANT_USED_IN_TRANSFERS",
                    "Cannot delete this variant: it has stock transfer history. Transfer records must keep referencing it. Deactivate the variant instead of deleting it.",
                    "لا يمكن حذف هذا المتغير: لديه سجل نقل مخزون. يجب أن تبقى سجلات النقل مرتبطة به. قم بتعطيل المتغير بدلاً من حذفه.")
                : new ProductVariantDeleteBlocker(
                    "PRODUCT_USED_IN_TRANSFERS",
                    "Product is used in stock transfers and cannot be deleted. Please set the product as inactive instead.",
                    "الصنف مستخدم في عمليات نقل المخزون ولا يمكن حذفه. يرجى تعطيل الصنف بدلاً من حذفه."));
        }

        // 6. Stock movement audit history.
        var hasMovements = hasMovementHistory && await context.StockMovements.AnyAsync(
            sm =>
                sm.ProductId == productId &&
                (!isVariant || sm.ProductVariantId == vid) &&
                sm.TenantId == tenantId &&
                !sm.IsDeleted,
            cancellationToken);
        if (hasMovements)
        {
            blockers.Add(isVariant
                ? new ProductVariantDeleteBlocker(
                    "VARIANT_HAS_MOVEMENTS",
                    "Cannot delete this variant: it has stock movement history. Movement records must keep referencing it. Deactivate the variant instead of deleting it.",
                    "لا يمكن حذف هذا المتغير: لديه سجل حركة مخزون. يجب أن تبقى سجلات الحركة مرتبطة به. قم بتعطيل المتغير بدلاً من حذفه.")
                : new ProductVariantDeleteBlocker(
                    "PRODUCT_HAS_MOVEMENTS",
                    "Product has stock movement history and cannot be deleted. Please set the product as inactive instead.",
                    "الصنف له سجل حركة مخزون ولا يمكن حذفه. يرجى تعطيل الصنف بدلاً من حذفه."));
        }

        // 7. Storefront order history (immutable SKU snapshots; money lives on items).
        var usedInStorefrontOrders = hasStorefrontOrders && await context.OrderItems.AnyAsync(
            item =>
                item.ProductId == productId &&
                (!isVariant || item.VariantId == vid) &&
                !item.IsDeleted &&
                !item.Order.IsDeleted &&
                item.Order.TenantId == tenantId,
            cancellationToken);
        if (usedInStorefrontOrders)
        {
            blockers.Add(isVariant
                ? new ProductVariantDeleteBlocker(
                    "VARIANT_HAS_STOREFRONT_ORDERS",
                    "Cannot delete this variant: it has online store orders. Orders keep an immutable record of this variant. Deactivate the variant instead of deleting it.",
                    "لا يمكن حذف هذا المتغير: لديه طلبات متجر إلكتروني. تحتفظ الطلبات بسجل دائم لهذا المتغير. قم بتعطيل المتغير بدلاً من حذفه.")
                : new ProductVariantDeleteBlocker(
                    "PRODUCT_HAS_STOREFRONT_ORDERS",
                    "Cannot delete product: it has online store orders. Orders keep an immutable record of this product. Please set the product as inactive instead.",
                    "لا يمكن حذف الصنف: لديه طلبات متجر إلكتروني. تحتفظ الطلبات بسجل دائم لهذا الصنف. يرجى تعطيل الصنف بدلاً من حذفه."));
        }

        // 8. CommerceHub order history (immutable cost snapshots; SubOrder.StoreId is the store tenant).
        var usedInCommerceHubOrders = hasCommerceHubOrders && await context.CommerceHubSubOrderItems.AnyAsync(
            item =>
                item.ProductId == productId &&
                (!isVariant || item.VariantId == vid) &&
                !item.IsDeleted &&
                !item.SubOrder.IsDeleted &&
                item.SubOrder.StoreId == tenantId,
            cancellationToken);
        if (usedInCommerceHubOrders)
        {
            blockers.Add(isVariant
                ? new ProductVariantDeleteBlocker(
                    "VARIANT_HAS_COMMERCEHUB_ORDERS",
                    "Cannot delete this variant: it has CommerceHub marketplace orders. Marketplace orders keep an immutable record of this variant. Deactivate the variant instead of deleting it.",
                    "لا يمكن حذف هذا المتغير: لديه طلبات سوق CommerceHub. تحتفظ طلبات السوق بسجل دائم لهذا المتغير. قم بتعطيل المتغير بدلاً من حذفه.")
                : new ProductVariantDeleteBlocker(
                    "PRODUCT_HAS_COMMERCEHUB_ORDERS",
                    "Cannot delete product: it has CommerceHub marketplace orders. Marketplace orders keep an immutable record of this product. Please set the product as inactive instead.",
                    "لا يمكن حذف الصنف: لديه طلبات سوق CommerceHub. تحتفظ طلبات السوق بسجل دائم لهذا الصنف. يرجى تعطيل الصنف بدلاً من حذفه."));
        }

        // 9. Open inventory reservations (still holding stock).
        var hasReservations = hasReservationHistory && await context.InventoryReservations.AnyAsync(
            reservation =>
                reservation.TenantId == tenantId &&
                !reservation.IsDeleted &&
                reservation.ProductId == productId &&
                (!isVariant || reservation.VariantId == vid) &&
                reservation.ReservedQuantity - reservation.CommittedQuantity - reservation.ReleasedQuantity > 0,
            cancellationToken);
        if (hasReservations)
        {
            blockers.Add(isVariant
                ? new ProductVariantDeleteBlocker(
                    "VARIANT_HAS_RESERVATIONS",
                    "Cannot delete this variant: it has active inventory reservations holding stock. Release or fulfill the reservations first, or deactivate the variant.",
                    "لا يمكن حذف هذا المتغير: لديه حجوزات مخزون نشطة. قم بتحرير الحجوزات أو تنفيذها أولاً، أو قم بتعطيل المتغير.")
                : new ProductVariantDeleteBlocker(
                    "PRODUCT_HAS_RESERVATIONS",
                    "Cannot delete product: it has active inventory reservations holding stock. Release or fulfill the reservations first, or set the product as inactive instead.",
                    "لا يمكن حذف الصنف: لديه حجوزات مخزون نشطة. قم بتحرير الحجوزات أو تنفيذها أولاً، أو قم بتعطيل الصنف بدلاً من حذفه."));
        }

        // 10. Published CommerceHub listing (live marketplace exposure).
        var hasPublishedListing = hasListings && await context.CommerceHubListings.AnyAsync(
            listing =>
                listing.TenantId == tenantId &&
                !listing.IsDeleted &&
                listing.ProductId == productId &&
                (!isVariant || listing.VariantId == vid) &&
                listing.IsPublished,
            cancellationToken);
        if (hasPublishedListing)
        {
            blockers.Add(isVariant
                ? new ProductVariantDeleteBlocker(
                    "VARIANT_HAS_COMMERCEHUB_LISTING",
                    "Cannot delete this variant: it is published on the CommerceHub marketplace. Unpublish the listing first, or deactivate the variant.",
                    "لا يمكن حذف هذا المتغير: منشور على سوق CommerceHub. قم بإلغاء نشر الإعلان أولاً، أو قم بتعطيل المتغير.")
                : new ProductVariantDeleteBlocker(
                    "PRODUCT_HAS_COMMERCEHUB_LISTING",
                    "Cannot delete product: it is published on the CommerceHub marketplace. Unpublish the listing first, or set the product as inactive instead.",
                    "لا يمكن حذف الصنف: منشور على سوق CommerceHub. قم بإلغاء نشر الإعلان أولاً، أو قم بتعطيل الصنف بدلاً من حذفه."));
        }

        return blockers;
    }
}
