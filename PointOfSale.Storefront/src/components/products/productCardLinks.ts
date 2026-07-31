import type { ProductListItemDto } from '@/types';

export function getProductCardHref(product: Pick<ProductListItemDto, 'id' | 'slug'>): string {
    return product.slug ? `/products/${product.slug}` : `/products/${product.id}`;
}
