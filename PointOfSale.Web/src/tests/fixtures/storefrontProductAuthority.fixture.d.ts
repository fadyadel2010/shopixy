import type { ComponentType } from 'react'
import type { ProductReadDto, ProductVariantDto } from '../../../../PointOfSale.Storefront/src/types/product'

export declare const ProductDetailsPage: ComponentType
export declare function useVariantSelector(product: ProductReadDto | null): {
    selectedVariant: ProductVariantDto | null
    isResolvingVariant: boolean
    hasVariantResolutionError: boolean
    selectAttribute(name: string, value: string): void
}
