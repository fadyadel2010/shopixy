export const mapPurchaseReturnInvoiceItem = (item, fallbackUnitName) => {
    const attributeValues = Array.isArray(item.variantAttributes)
        ? item.variantAttributes.map(attribute => attribute.value).filter(Boolean)
        : []
    let variantDisplayName = item.productName

    if (attributeValues.length > 0) {
        variantDisplayName = `${item.productName} - ${attributeValues.join(' / ')}`
    } else if (item.variantSku) {
        variantDisplayName = `${item.productName} (${item.variantSku})`
    }

    return {
        originalInvoiceItemIds: item.originalInvoiceItemIds,
        productId: item.productId,
        productVariantId: item.productVariantId,
        name: item.productName,
        variantDisplayName,
        variantSku: item.variantSku || null,
        variantAttributes: item.variantAttributes,
        unitId: item.unitId,
        unitName: item.unitName || fallbackUnitName,
        purchasedQuantity: item.purchasedQuantity,
        returnedQuantity: item.returnedQuantity,
        originalQuantity: item.remainingQuantity,
        quantity: 0,
        unitPrice: item.unitPrice,
        taxRate: item.taxRate,
        reason: '',
    }
}
