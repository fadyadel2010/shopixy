import React from 'react';
import { useLocalizedContent } from '@/i18n';
import type { ProductReadDto } from '@/types';
import { OptimizedImage } from '@/components/media';
import { getPurchaseExperienceProfile } from '@/storefront/products/purchaseExperience';
import { ProductGalleryModal } from './ProductGalleryModal';
import { ProductMediaViewer } from './ProductMediaViewer';
import {
    getMediaLabel,
    isVideoMedia,
    normalizeProductMedia,
} from './productMediaGalleryUtils';

interface ProductMediaGalleryProps {
    product?: ProductReadDto | null;
    preferredMediaId?: string | null;
    preferredImageUrl?: string | null;
    showThumbnails?: boolean;
    maxThumbnails?: number;
    aspectClassName?: string;
    presentationVariant?: string;
}

export const ProductMediaGallery: React.FC<ProductMediaGalleryProps> = ({
    product,
    preferredMediaId = null,
    preferredImageUrl = null,
    showThumbnails = true,
    maxThumbnails = 8,
    aspectClassName,
    presentationVariant = 'modern',
}) => {
    const { localize } = useLocalizedContent();
    const profile = getPurchaseExperienceProfile(presentationVariant, presentationVariant);
    const productName = product ? localize(product, 'name') || 'Product' : 'Product';
    const mediaItems = React.useMemo(() => normalizeProductMedia(product), [product]);
    const [activeIndex, setActiveIndex] = React.useState(0);
    const [isModalOpen, setIsModalOpen] = React.useState(false);
    const [isMobileViewport, setIsMobileViewport] = React.useState(false);
    const touchStartXRef = React.useRef<number | null>(null);
    const touchEndXRef = React.useRef<number | null>(null);
    const fallbackPoster = React.useMemo(
        () => mediaItems.find((item) => item.mediaType?.toLowerCase() === 'image')?.url ?? null,
        [mediaItems]
    );

    React.useEffect(() => {
        if (typeof window === 'undefined') {
            return undefined;
        }

        const mediaQuery = window.matchMedia('(max-width: 767px)');
        const updateViewport = () => setIsMobileViewport(mediaQuery.matches);

        updateViewport();

        if (typeof mediaQuery.addEventListener === 'function') {
            mediaQuery.addEventListener('change', updateViewport);
            return () => mediaQuery.removeEventListener('change', updateViewport);
        }

        mediaQuery.addListener(updateViewport);
        return () => mediaQuery.removeListener(updateViewport);
    }, []);

    React.useEffect(() => {
        if (!mediaItems.length) {
            setActiveIndex(0);
            return;
        }

        if (preferredMediaId) {
            const preferredIndex = mediaItems.findIndex((item) => item.id === preferredMediaId);
            if (preferredIndex >= 0) {
                setActiveIndex(preferredIndex);
                return;
            }
        }

        if (preferredImageUrl) {
            const preferredIndex = mediaItems.findIndex((item) => item.url === preferredImageUrl);
            if (preferredIndex >= 0) {
                setActiveIndex(preferredIndex);
                return;
            }
        }

        setActiveIndex((current) => Math.min(current, mediaItems.length - 1));
    }, [mediaItems, preferredImageUrl, preferredMediaId]);

    const activeMedia = mediaItems[activeIndex] ?? null;
    const resolvedMaxThumbnails = maxThumbnails || profile.galleryMaxThumbnails;
    const thumbnailItems = showThumbnails ? mediaItems.slice(0, Math.max(1, resolvedMaxThumbnails)) : [];
    const resolvedAspectClassName = aspectClassName || profile.galleryAspectClassName;

    const selectIndex = React.useCallback(
        (index: number) => {
            if (!mediaItems.length) {
                setActiveIndex(0);
                return;
            }

            const bounded = (index + mediaItems.length) % mediaItems.length;
            setActiveIndex(bounded);
        },
        [mediaItems.length]
    );

    const showPrevious = React.useCallback(() => {
        selectIndex(activeIndex - 1);
    }, [activeIndex, selectIndex]);

    const showNext = React.useCallback(() => {
        selectIndex(activeIndex + 1);
    }, [activeIndex, selectIndex]);

    const handleTouchStart = React.useCallback(
        (event: React.TouchEvent<HTMLDivElement>) => {
            if (!isMobileViewport || mediaItems.length <= 1) {
                return;
            }

            touchStartXRef.current = event.touches[0]?.clientX ?? null;
            touchEndXRef.current = null;
        },
        [isMobileViewport, mediaItems.length]
    );

    const handleTouchMove = React.useCallback(
        (event: React.TouchEvent<HTMLDivElement>) => {
            if (!isMobileViewport || mediaItems.length <= 1) {
                return;
            }

            touchEndXRef.current = event.touches[0]?.clientX ?? null;
        },
        [isMobileViewport, mediaItems.length]
    );

    const handleTouchEnd = React.useCallback(() => {
        if (!isMobileViewport || mediaItems.length <= 1) {
            return;
        }

        const startX = touchStartXRef.current;
        const endX = touchEndXRef.current;

        if (startX === null || endX === null) {
            return;
        }

        const delta = startX - endX;
        if (Math.abs(delta) < 48) {
            return;
        }

        if (delta > 0) {
            showNext();
        } else {
            showPrevious();
        }
    }, [isMobileViewport, mediaItems.length, showNext, showPrevious]);

    if (!product) {
        return null;
    }

    return (
        <>
            <div className="min-w-0 space-y-4" data-purchase-experience={profile.variant}>
                <div className={profile.galleryShellClassName}>
                    <div
                        className="group relative overflow-hidden border border-muted/20 bg-muted/10"
                        onTouchStart={handleTouchStart}
                        onTouchMove={handleTouchMove}
                        onTouchEnd={handleTouchEnd}
                    >
                        <ProductMediaViewer
                            media={activeMedia}
                            productName={productName}
                            mediaIndex={activeIndex}
                            aspectClassName={resolvedAspectClassName}
                            fallbackPoster={fallbackPoster}
                            loading="eager"
                            onOpenFullscreen={() => setIsModalOpen(true)}
                        />

                        {mediaItems.length > 1 ? (
                            <>
                                <button
                                    type="button"
                                    onClick={showPrevious}
                                    className="absolute left-4 top-1/2 hidden -translate-y-1/2 rounded-full border border-white/20 bg-black/55 p-3 text-white shadow-sm transition hover:bg-black/70 focus:outline-none focus:ring-2 focus:ring-white/60 md:flex"
                                    aria-label="Previous media"
                                >
                                    <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                        <path d="M15 18l-6-6 6-6" strokeLinecap="round" strokeLinejoin="round" />
                                    </svg>
                                </button>

                                <button
                                    type="button"
                                    onClick={showNext}
                                    className="absolute right-4 top-1/2 hidden -translate-y-1/2 rounded-full border border-white/20 bg-black/55 p-3 text-white shadow-sm transition hover:bg-black/70 focus:outline-none focus:ring-2 focus:ring-white/60 md:flex"
                                    aria-label="Next media"
                                >
                                    <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                        <path d="M9 6l6 6-6 6" strokeLinecap="round" strokeLinejoin="round" />
                                    </svg>
                                </button>
                            </>
                        ) : null}

                        <div className="pointer-events-none absolute inset-x-0 bottom-0 flex items-center justify-between bg-gradient-to-t from-black/70 to-transparent px-4 pb-4 pt-10 text-white">
                            <div>
                                <div className="text-xs font-semibold uppercase tracking-[0.28em] text-white/70">
                                    {profile.galleryEyebrow}
                                </div>
                                <div className="mt-1 text-sm font-medium">
                                    {activeMedia
                                        ? getMediaLabel(activeMedia, productName, activeIndex)
                                        : 'Browse product media'}
                                </div>
                            </div>

                            {mediaItems.length > 1 ? (
                                <div className="rounded-full bg-white/10 px-3 py-1 text-xs font-medium text-white/90">
                                    {activeIndex + 1} / {mediaItems.length}
                                </div>
                            ) : null}
                        </div>
                    </div>
                </div>

                {thumbnailItems.length > 1 ? (
                    <div className="overflow-x-auto pb-2">
                        <div className="flex min-w-full gap-3">
                            {thumbnailItems.map((item, index) => {
                                const isActive = index === activeIndex;
                                const thumbnailUrl = item.thumbnailUrl || null;

                                return (
                                    <button
                                        key={item.id}
                                        type="button"
                                        onClick={() => selectIndex(index)}
                                        className={`relative h-24 w-24 shrink-0 overflow-hidden rounded-2xl border bg-muted/10 text-left transition focus:outline-none focus:ring-2 focus:ring-primary/30 ${
                                            isActive
                                                ? 'border-primary shadow-[0_0_0_3px_rgba(37,99,235,0.12)]'
                                                : 'border-muted/20 hover:border-primary/40'
                                        }`}
                                        aria-label={`Show ${getMediaLabel(item, productName, index)}`}
                                        aria-pressed={isActive}
                                    >
                                        {item.mediaType === 'image' ? (
                                            <OptimizedImage
                                                src={item.url}
                                                alt={getMediaLabel(item, productName, index)}
                                                sizes="96px"
                                                loading="lazy"
                                                 className="h-full w-full object-contain"
                                            />
                                        ) : thumbnailUrl ? (
                                            <OptimizedImage
                                                src={thumbnailUrl}
                                                alt={getMediaLabel(item, productName, index)}
                                                sizes="96px"
                                                loading="lazy"
                                                 className="h-full w-full object-contain"
                                            />
                                        ) : (
                                            <video
                                                src={item.url}
                                                muted
                                                playsInline
                                                preload="metadata"
                                                poster={fallbackPoster || undefined}
                                                className="h-full w-full object-cover"
                                            />
                                        )}

                                        {isVideoMedia(item) ? (
                                            <div className="absolute inset-0 flex items-center justify-center bg-black/20">
                                                <div className="rounded-full bg-white/90 p-2 text-gray-900 shadow-sm">
                                                    <svg className="h-4 w-4" viewBox="0 0 24 24" fill="currentColor">
                                                        <path d="M8 5.14v14l11-7-11-7z" />
                                                    </svg>
                                                </div>
                                            </div>
                                        ) : null}
                                    </button>
                                );
                            })}
                        </div>
                    </div>
                ) : null}
            </div>

            <ProductGalleryModal
                isOpen={isModalOpen}
                mediaItems={mediaItems}
                activeIndex={activeIndex}
                productName={productName}
                fallbackPoster={fallbackPoster}
                onClose={() => setIsModalOpen(false)}
                onSelect={selectIndex}
                onPrevious={showPrevious}
                onNext={showNext}
            />
        </>
    );
};

export default ProductMediaGallery;
