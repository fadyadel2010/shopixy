import React from 'react';
import type { ProductReadDto } from '@/types';

interface ProductVideoProps {
    product?: ProductReadDto | null;
    title?: string;
    autoplay?: boolean;
    showControls?: boolean;
}

const ProductVideo: React.FC<ProductVideoProps> = ({
    product,
    title,
    autoplay,
    showControls,
}) => {
    const productVideo = product?.media
        ?.filter((item) => item?.mediaType?.toLowerCase() === 'video' && item.url)
        .sort((left, right) => {
            if (left.position !== right.position) {
                return left.position - right.position;
            }

            return left.url.localeCompare(right.url);
        })[0];
    const resolvedVideoUrl = productVideo?.url || '';
    const provider = (productVideo?.provider || '').toLowerCase();
    const playbackUrl = productVideo?.presentationUrl || (provider === 'youtube' ? null : resolvedVideoUrl);

    if (!resolvedVideoUrl || !playbackUrl) {
        return null;
    }

    return (
        <section className="container mx-auto px-4 py-8">
            <div className="rounded-3xl border border-muted/20 bg-background p-6 shadow-sm">
                {title ? <h2 className="mb-6 text-2xl font-bold text-text">{title}</h2> : null}

                {provider === 'youtube' ? (
                    <div className="overflow-hidden rounded-3xl border border-muted/20 bg-black">
                        <div className="aspect-video">
                            <iframe
                                src={playbackUrl}
                                title={title || 'Product video'}
                                className="h-full w-full"
                                allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
                                allowFullScreen
                            />
                        </div>
                    </div>
                ) : (
                    <div className="overflow-hidden rounded-3xl border border-muted/20 bg-black">
                        <video
                            src={playbackUrl}
                            className="aspect-video h-full w-full"
                            autoPlay={Boolean(autoplay)}
                            controls={showControls !== false}
                            muted={Boolean(autoplay)}
                            playsInline
                        />
                    </div>
                )}
            </div>
        </section>
    );
};

export default ProductVideo;
