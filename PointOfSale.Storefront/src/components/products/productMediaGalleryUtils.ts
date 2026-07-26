import type { ProductMediaDto, ProductReadDto } from '@/types';

export function normalizeProductMedia(product?: ProductReadDto | null): ProductMediaDto[] {
    if (!product) {
        return [];
    }

    return (product.media ?? [])
        .filter((item) => item?.id && item?.url)
        .sort((left, right) => {
            if (left.position !== right.position) {
                return left.position - right.position;
            }

            return left.id.localeCompare(right.id);
        });
}

export function isVideoMedia(media?: ProductMediaDto | null): boolean {
    return (media?.mediaType || '').toLowerCase() === 'video';
}

export function isImageMedia(media?: ProductMediaDto | null): boolean {
    return (media?.mediaType || '').toLowerCase() === 'image';
}

export function getMediaLabel(
    media: ProductMediaDto | null | undefined,
    productName: string,
    index: number
): string {
    if (!media) {
        return productName;
    }

    if (isImageMedia(media)) {
        return `${productName} image ${index + 1}`;
    }

    if ((media.provider || '').toLowerCase() === 'youtube') {
        return `${productName} YouTube video ${index + 1}`;
    }

    return `${productName} video ${index + 1}`;
}
