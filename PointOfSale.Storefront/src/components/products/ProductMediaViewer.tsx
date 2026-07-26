import React from 'react';
import { OptimizedImage } from '@/components/media';
import type { ProductMediaDto } from '@/types';
import {
    getMediaLabel,
    isImageMedia,
} from './productMediaGalleryUtils';

interface ProductMediaViewerProps {
    media?: ProductMediaDto | null;
    productName: string;
    mediaIndex?: number;
    aspectClassName?: string;
    fallbackPoster?: string | null;
    loading?: 'eager' | 'lazy';
    showZoomHint?: boolean;
    onOpenFullscreen?: () => void;
}

export const ProductMediaViewer: React.FC<ProductMediaViewerProps> = ({
    media,
    productName,
    mediaIndex = 0,
    aspectClassName = 'aspect-square',
    fallbackPoster = null,
    loading = 'eager',
    showZoomHint = true,
    onOpenFullscreen,
}) => {
    const [supportsHoverZoom, setSupportsHoverZoom] = React.useState(false);
    const [transformOrigin, setTransformOrigin] = React.useState('50% 50%');
    const [isZoomed, setIsZoomed] = React.useState(false);

    React.useEffect(() => {
        if (typeof window === 'undefined') {
            return undefined;
        }

        const mediaQuery = window.matchMedia('(hover: hover) and (pointer: fine)');
        const updateSupport = () => setSupportsHoverZoom(mediaQuery.matches);

        updateSupport();

        if (typeof mediaQuery.addEventListener === 'function') {
            mediaQuery.addEventListener('change', updateSupport);
            return () => mediaQuery.removeEventListener('change', updateSupport);
        }

        mediaQuery.addListener(updateSupport);
        return () => mediaQuery.removeListener(updateSupport);
    }, []);

    const handleMouseMove = React.useCallback(
        (event: React.MouseEvent<HTMLButtonElement | HTMLDivElement>) => {
            if (!supportsHoverZoom || !media || !isImageMedia(media)) {
                return;
            }

            const bounds = event.currentTarget.getBoundingClientRect();
            const x = ((event.clientX - bounds.left) / bounds.width) * 100;
            const y = ((event.clientY - bounds.top) / bounds.height) * 100;
            setTransformOrigin(`${x}% ${y}%`);
            setIsZoomed(true);
        },
        [media, supportsHoverZoom]
    );

    const handleMouseLeave = React.useCallback(() => {
        setTransformOrigin('50% 50%');
        setIsZoomed(false);
    }, []);

    const sharedClassName = `group relative block h-full w-full overflow-hidden ${aspectClassName}`;
    const label = getMediaLabel(media, productName, mediaIndex);
    const mediaProvider = (media?.provider || '').toLowerCase();
    const playbackUrl = media?.presentationUrl || (mediaProvider === 'youtube' ? null : media?.url);

    if (!media) {
        return (
            <div className={`${sharedClassName} flex items-center justify-center bg-muted/20 text-sm text-muted`}>
                No product media available
            </div>
        );
    }

    const content = isImageMedia(media) ? (
        <OptimizedImage
            src={media.url}
            alt={label}
            loading={loading}
            sizes="(max-width: 1024px) 100vw, 720px"
            className="h-full w-full object-cover transition-transform duration-300 ease-out"
            style={{
                transformOrigin,
                transform: supportsHoverZoom && isZoomed ? 'scale(1.5)' : 'scale(1)',
            }}
        />
    ) : mediaProvider === 'youtube' && playbackUrl ? (
        <iframe
            src={playbackUrl}
            title={label}
            allow="accelerometer; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
            allowFullScreen
            className="h-full w-full border-0"
        />
    ) : mediaProvider === 'youtube' ? (
        <div className="flex h-full w-full items-center justify-center bg-slate-900 px-6 text-center text-sm text-white/70">
            Video preview unavailable
        </div>
    ) : (
        <video
            src={playbackUrl || undefined}
            controls
            playsInline
            preload="metadata"
            poster={fallbackPoster || undefined}
            className="h-full w-full bg-black object-contain"
        />
    );

    if (isImageMedia(media)) {
        return (
            <button
                type="button"
                onClick={onOpenFullscreen}
                onMouseMove={handleMouseMove}
                onMouseLeave={handleMouseLeave}
                className={`${sharedClassName} cursor-zoom-in bg-muted/10 focus:outline-none focus:ring-2 focus:ring-primary/40`}
                aria-label={`Open ${label} in fullscreen`}
            >
                {content}

                {showZoomHint && supportsHoverZoom ? (
                    <div className="pointer-events-none absolute bottom-4 right-4 rounded-full bg-black/70 px-3 py-1 text-xs font-medium text-white opacity-0 transition-opacity duration-200 group-hover:opacity-100">
                        Hover to zoom
                    </div>
                ) : null}
            </button>
        );
    }

    return (
        <div
            className={`${sharedClassName} bg-muted/10`}
            onMouseMove={handleMouseMove}
            onMouseLeave={handleMouseLeave}
        >
            {content}

            {onOpenFullscreen ? (
                <button
                    type="button"
                    onClick={onOpenFullscreen}
                    className="absolute right-4 top-4 rounded-full bg-black/70 px-3 py-2 text-xs font-medium text-white transition hover:bg-black/80 focus:outline-none focus:ring-2 focus:ring-white/60"
                    aria-label={`Open ${label} in fullscreen`}
                >
                    Fullscreen
                </button>
            ) : null}
        </div>
    );
};

export default ProductMediaViewer;
