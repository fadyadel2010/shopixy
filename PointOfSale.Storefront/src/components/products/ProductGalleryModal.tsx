import React from 'react';
import { createPortal } from 'react-dom';
import type { ProductMediaDto } from '@/types';
import { OptimizedImage } from '@/components/media';
import {
    getMediaLabel,
    isImageMedia,
    isVideoMedia,
} from './productMediaGalleryUtils';

interface ProductGalleryModalProps {
    isOpen: boolean;
    mediaItems: ProductMediaDto[];
    activeIndex: number;
    productName: string;
    fallbackPoster?: string | null;
    onClose: () => void;
    onSelect: (index: number) => void;
    onPrevious: () => void;
    onNext: () => void;
}

export const ProductGalleryModal: React.FC<ProductGalleryModalProps> = ({
    isOpen,
    mediaItems,
    activeIndex,
    productName,
    fallbackPoster = null,
    onClose,
    onSelect,
    onPrevious,
    onNext,
}) => {
    const dialogRef = React.useRef<HTMLDivElement | null>(null);
    const closeButtonRef = React.useRef<HTMLButtonElement | null>(null);
    const activeMedia = mediaItems[activeIndex] ?? null;
    const activeProvider = (activeMedia?.provider || '').toLowerCase();
    const activePlaybackUrl = activeMedia?.presentationUrl || (activeProvider === 'youtube' ? null : activeMedia?.url);

    React.useEffect(() => {
        if (!isOpen || typeof document === 'undefined') {
            return undefined;
        }

        const previousOverflow = document.body.style.overflow;
        document.body.style.overflow = 'hidden';
        closeButtonRef.current?.focus();

        const handleKeyDown = (event: KeyboardEvent) => {
            if (event.key === 'Escape') {
                event.preventDefault();
                onClose();
                return;
            }

            if (event.key === 'ArrowLeft') {
                event.preventDefault();
                onPrevious();
                return;
            }

            if (event.key === 'ArrowRight') {
                event.preventDefault();
                onNext();
                return;
            }

            if (event.key !== 'Tab' || !dialogRef.current) {
                return;
            }

            const focusable = dialogRef.current.querySelectorAll<HTMLElement>(
                'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
            );

            if (!focusable.length) {
                event.preventDefault();
                return;
            }

            const first = focusable[0];
            const last = focusable[focusable.length - 1];
            const activeElement = document.activeElement as HTMLElement | null;

            if (event.shiftKey && activeElement === first) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        };

        document.addEventListener('keydown', handleKeyDown);

        return () => {
            document.body.style.overflow = previousOverflow;
            document.removeEventListener('keydown', handleKeyDown);
        };
    }, [isOpen, onClose, onNext, onPrevious]);

    if (!isOpen || typeof document === 'undefined' || !activeMedia) {
        return null;
    }

    const body = (
        <div
            className="fixed inset-0 z-[90] flex items-center justify-center bg-black/90 p-4 md:p-6"
            aria-modal="true"
            role="dialog"
            aria-label={`${productName} media gallery`}
            onClick={onClose}
        >
            <div
                ref={dialogRef}
                className="relative flex h-full max-h-[92vh] w-full max-w-6xl flex-col overflow-hidden rounded-3xl border border-white/10 bg-slate-950 shadow-2xl"
                onClick={(event) => event.stopPropagation()}
            >
                <div className="flex items-center justify-between border-b border-white/10 px-4 py-3 text-white md:px-6">
                    <div>
                        <div className="text-sm font-semibold">{productName}</div>
                        <div className="text-xs text-white/60">
                            Item {activeIndex + 1} of {mediaItems.length}
                        </div>
                    </div>

                    <button
                        ref={closeButtonRef}
                        type="button"
                        onClick={onClose}
                        className="rounded-full border border-white/15 px-3 py-2 text-sm font-medium text-white transition hover:bg-white/10 focus:outline-none focus:ring-2 focus:ring-white/60"
                    >
                        Close
                    </button>
                </div>

                <div className="relative min-h-0 flex-1 overflow-hidden bg-black">
                    {isImageMedia(activeMedia) ? (
                        <OptimizedImage
                            src={activeMedia.url}
                            alt={getMediaLabel(activeMedia, productName, activeIndex)}
                            loading="eager"
                            sizes="100vw"
                            className="absolute inset-0 h-full w-full object-contain"
                        />
                    ) : activeProvider === 'youtube' && activePlaybackUrl ? (
                        <iframe
                            src={activePlaybackUrl}
                            title={getMediaLabel(activeMedia, productName, activeIndex)}
                            allow="accelerometer; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
                            allowFullScreen
                            className="h-full w-full border-0"
                        />
                    ) : activeProvider === 'youtube' ? (
                        <div className="flex h-full w-full items-center justify-center px-6 text-center text-sm text-white/70">
                            Video preview unavailable
                        </div>
                    ) : (
                        <video
                            src={activePlaybackUrl || undefined}
                            controls
                            playsInline
                            preload="metadata"
                            poster={fallbackPoster || undefined}
                            className="h-full w-full bg-black object-contain"
                        />
                    )}

                    {mediaItems.length > 1 ? (
                        <>
                            <button
                                type="button"
                                onClick={onPrevious}
                                className="absolute left-4 top-1/2 -translate-y-1/2 rounded-full bg-white/10 p-3 text-white transition hover:bg-white/20 focus:outline-none focus:ring-2 focus:ring-white/60"
                                aria-label="Previous media"
                            >
                                <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                    <path d="M15 18l-6-6 6-6" strokeLinecap="round" strokeLinejoin="round" />
                                </svg>
                            </button>

                            <button
                                type="button"
                                onClick={onNext}
                                className="absolute right-4 top-1/2 -translate-y-1/2 rounded-full bg-white/10 p-3 text-white transition hover:bg-white/20 focus:outline-none focus:ring-2 focus:ring-white/60"
                                aria-label="Next media"
                            >
                                <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                    <path d="M9 6l6 6-6 6" strokeLinecap="round" strokeLinejoin="round" />
                                </svg>
                            </button>
                        </>
                    ) : null}
                </div>

                {mediaItems.length > 1 ? (
                    <div className="flex gap-3 overflow-x-auto border-t border-white/10 px-4 py-4 md:px-6">
                        {mediaItems.map((item, index) => {
                            const isActive = index === activeIndex;
                            const thumbnailUrl = item.thumbnailUrl || null;

                            return (
                                <button
                                    key={item.id}
                                    type="button"
                                    onClick={() => onSelect(index)}
                                    className={`relative h-20 w-20 shrink-0 overflow-hidden rounded-2xl border transition ${
                                        isActive
                                            ? 'border-primary ring-2 ring-primary/25'
                                            : 'border-white/10 hover:border-white/30'
                                    }`}
                                    aria-label={`View ${getMediaLabel(item, productName, index)}`}
                                >
                                    {isImageMedia(item) ? (
                                        <OptimizedImage
                                            src={item.url}
                                            alt={getMediaLabel(item, productName, index)}
                                            loading="lazy"
                                            sizes="80px"
                                            className="h-full w-full object-contain"
                                        />
                                    ) : thumbnailUrl ? (
                                        <OptimizedImage
                                            src={thumbnailUrl}
                                            alt={getMediaLabel(item, productName, index)}
                                            loading="lazy"
                                            sizes="80px"
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
                                        <div className="absolute inset-0 flex items-center justify-center bg-black/30">
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
                ) : null}
            </div>
        </div>
    );

    return createPortal(body, document.body);
};

export default ProductGalleryModal;
