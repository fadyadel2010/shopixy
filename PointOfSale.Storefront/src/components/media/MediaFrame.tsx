import type { CSSProperties, HTMLAttributes } from 'react';
import './media.css';

export type MediaRole = 'product-card' | 'product-detail' | 'hero' | 'banner' | 'content' | 'category';

/** The frame, never a replaced element's intrinsic dimensions, owns layout space. */
export function MediaFrame({ role, aspectRatio, fit, className = '', style, ...props }: {
    role: MediaRole;
    aspectRatio?: string;
    fit?: 'contain' | 'cover';
} & HTMLAttributes<HTMLDivElement>) {
    return <div {...props} data-media-role={role} className={`sf-media sf-media--${role} ${fit ? `sf-media--${fit}` : ''} ${className}`}
        style={{ ...style, ...(aspectRatio ? { aspectRatio, '--media-ratio': aspectRatio } : {}) } as CSSProperties} />;
}
