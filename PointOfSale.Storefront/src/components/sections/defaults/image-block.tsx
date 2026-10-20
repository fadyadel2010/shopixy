import React from 'react';
import { MediaFrame, OptimizedImage } from '@/components/media';

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
            <MediaFrame role="content"
                className="overflow-hidden rounded-xl bg-muted/20"
                aspectRatio={['1:1', '4:3', '16:9', '21:9'].includes(aspectRatio) ? aspectRatio.replace(':', '/') : '16/9'}
            >
                {imageUrl ? (
                    <OptimizedImage src={imageUrl} alt={altText || ''} className="h-full w-full object-contain" />
                ) : (
                    <div className="flex h-full min-h-[220px] items-center justify-center text-muted">
                        Image Placeholder
                    </div>
                )}
            </MediaFrame>
            {caption ? <figcaption className="mt-3 text-center text-sm text-muted">{caption}</figcaption> : null}
        </figure>
    </section>
);

export default ImageBlock;
