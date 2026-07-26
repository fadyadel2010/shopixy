import React from 'react';

function normalizeImages(images: any[] | undefined) {
    if (!Array.isArray(images) || images.length === 0) {
        return [{ url: '' }];
    }

    return images.map((image) => (
        typeof image === 'string' ? { url: image } : { ...image, url: image?.url || '' }
    ));
}

const ImageBlock: React.FC<any> = ({ images, layout, aspectRatio, fullWidth }) => {
    const resolvedLayout = layout || 'single';
    const imageItems = normalizeImages(images);
    const columns = resolvedLayout === 'single' ? 1 : resolvedLayout === 'side-by-side' ? 2 : 3;

    return (
        <section className={`py-12 px-4 ${fullWidth ? '' : 'container mx-auto'}`}>
            <div className="grid gap-4" style={{ gridTemplateColumns: `repeat(${columns}, 1fr)` }}>
                {Array.from({ length: Math.max(columns, imageItems.length) }, (_, index) => {
                    const image = imageItems[index];

                    return (
                        <div
                            key={index}
                            className="overflow-hidden rounded-xl bg-muted/20"
                            style={{
                                aspectRatio: aspectRatio && aspectRatio !== 'auto' ? aspectRatio.replace(':', '/') : undefined,
                                minHeight: aspectRatio === 'auto' ? 220 : undefined,
                            }}
                        >
                            {image?.url ? (
                                <img src={image.url} alt="" className="h-full w-full object-cover" />
                            ) : (
                                <div className="flex h-full min-h-[220px] items-center justify-center text-muted">
                                    Image Placeholder
                                </div>
                            )}
                        </div>
                    );
                })}
            </div>
        </section>
    );
};

export default ImageBlock;
