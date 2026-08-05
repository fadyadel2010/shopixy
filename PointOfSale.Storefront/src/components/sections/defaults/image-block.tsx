import React from 'react';

interface ImageBlockProps {
    imageUrl?: string;
    altText?: string;
    caption?: string;
    aspectRatio?: string;
    maxWidth?: string;
}

const maxWidthClasses: Record<string, string> = {
    narrow: 'max-w-2xl',
    default: 'max-w-4xl',
    wide: 'max-w-6xl',
    full: 'max-w-none',
};

const ImageBlock: React.FC<ImageBlockProps> = ({
    imageUrl,
    altText,
    caption,
    aspectRatio = '16:9',
    maxWidth = 'default',
}) => (
    <section className="px-4 py-12">
        <figure className={`mx-auto ${maxWidthClasses[maxWidth] || maxWidthClasses.default}`}>
            <div
                className="overflow-hidden rounded-xl bg-muted/20"
                style={{ aspectRatio: aspectRatio.replace(':', '/') }}
            >
                {imageUrl ? (
                    <img src={imageUrl} alt={altText || ''} className="h-full w-full object-cover" />
                ) : (
                    <div className="flex h-full min-h-[220px] items-center justify-center text-muted">
                        Image Placeholder
                    </div>
                )}
            </div>
            {caption ? <figcaption className="mt-3 text-center text-sm text-muted">{caption}</figcaption> : null}
        </figure>
    </section>
);

export default ImageBlock;
