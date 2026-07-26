const PLACEHOLDER_SVG = encodeURIComponent(`
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 96 96" fill="none">
  <rect width="96" height="96" rx="18" fill="#F1F5F9"/>
  <path d="M24 61.5l13.4-13.4a4 4 0 0 1 5.7 0L56 61.1l5.7-5.7a4 4 0 0 1 5.7 0L72 60" stroke="#64748B" stroke-width="4" stroke-linecap="round" stroke-linejoin="round"/>
  <circle cx="37" cy="35" r="6" fill="#94A3B8"/>
  <rect x="20" y="20" width="56" height="56" rx="12" stroke="#94A3B8" stroke-width="4"/>
</svg>
`);

export const PRODUCT_PLACEHOLDER_IMAGE = `data:image/svg+xml;charset=UTF-8,${PLACEHOLDER_SVG}`;

interface ProductMediaLike {
    id?: string | null;
    url?: string | null;
    mediaType?: string | null;
    type?: string | null;
    position?: number | null;
    isPrimary?: boolean | null;
}

interface ProductImageLike {
    media?: ProductMediaLike[] | null;
    images?: Array<string | null | undefined> | null;
    thumbnailImage?: string | null;
    image?: string | null;
    imageUrl?: string | null;
    mainImage?: string | null;
}

interface VariantImageLike {
    mediaId?: string | null;
}

function getCleanUrl(value?: string | null): string | null {
    if (typeof value !== 'string') {
        return null;
    }

    const trimmed = value.trim();
    return trimmed.length > 0 ? trimmed : null;
}

function isImageMedia(item?: ProductMediaLike | null): boolean {
    const mediaType = (item?.mediaType ?? item?.type ?? '').toLowerCase();
    return mediaType === 'image' && !!getCleanUrl(item?.url);
}

function getOrderedImageMedia(product?: ProductImageLike | null): ProductMediaLike[] {
    if (!product?.media?.length) {
        return [];
    }

    return [...product.media]
        .filter(isImageMedia)
        .sort((left, right) => {
            if ((left.isPrimary ?? false) !== (right.isPrimary ?? false)) {
                return left.isPrimary ? -1 : 1;
            }

            const leftPosition = left.position ?? Number.MAX_SAFE_INTEGER;
            const rightPosition = right.position ?? Number.MAX_SAFE_INTEGER;

            if (leftPosition !== rightPosition) {
                return leftPosition - rightPosition;
            }

            return (left.url ?? '').localeCompare(right.url ?? '');
        });
}

export function resolveProductImage(
    product?: ProductImageLike | null,
    variant?: VariantImageLike | null
): string {
    const orderedMedia = getOrderedImageMedia(product);

    if (variant?.mediaId) {
        const mediaMatch = orderedMedia.find((item) => item.id === variant.mediaId);
        const mediaUrl = getCleanUrl(mediaMatch?.url);
        if (mediaUrl) {
            return mediaUrl;
        }
    }

    const primaryMediaUrl = getCleanUrl(orderedMedia[0]?.url);
    if (primaryMediaUrl) {
        return primaryMediaUrl;
    }

    const firstLegacyImage =
        product?.images?.map((url) => getCleanUrl(url)).find((url): url is string => !!url) ?? null;
    const fallbackCandidates = [
        product?.thumbnailImage,
        product?.image,
        product?.imageUrl,
        product?.mainImage,
        firstLegacyImage,
    ];

    for (const candidate of fallbackCandidates) {
        const cleanCandidate = getCleanUrl(candidate);
        if (cleanCandidate) {
            return cleanCandidate;
        }
    }

    return PRODUCT_PLACEHOLDER_IMAGE;
}
