import type { CartDto, CartItemDto } from '@/types';

type RawCartItemDto = Omit<CartItemDto, 'productName' | 'productNameAr' | 'imageUrl'> & {
    productName?: string | null;
    productNameAr?: string | null;
    imageUrl?: string | null;
    name?: string | null;
    nameAr?: string | null;
    thumbnailImage?: string | null;
};

type RawCartDto = Omit<CartDto, 'items'> & {
    items?: RawCartItemDto[] | null;
};

function firstNonBlankString(...values: Array<string | null | undefined>): string | undefined {
    for (const value of values) {
        if (typeof value !== 'string') {
            continue;
        }

        const trimmed = value.trim();
        if (trimmed.length > 0) {
            return trimmed;
        }
    }

    return undefined;
}

export function normalizeCartItemResponse(item: RawCartItemDto): CartItemDto {
    const productName = firstNonBlankString(item.productName, item.name);
    const productNameAr = firstNonBlankString(item.productNameAr, item.nameAr);
    const imageUrl = firstNonBlankString(item.imageUrl, item.thumbnailImage);

    return {
        ...item,
        productName: productName ?? '',
        productNameAr,
        imageUrl,
    };
}

export function normalizeCartResponse(cart: RawCartDto): CartDto {
    return {
        ...cart,
        items: (cart.items ?? []).map(normalizeCartItemResponse),
    };
}
