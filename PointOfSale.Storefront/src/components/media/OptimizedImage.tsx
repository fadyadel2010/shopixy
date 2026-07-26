import React from 'react';

const INLINE_PLACEHOLDER = `data:image/svg+xml;charset=UTF-8,${encodeURIComponent(`
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 96 96" fill="none">
  <rect width="96" height="96" rx="18" fill="#F1F5F9"/>
  <path d="M24 61.5l13.4-13.4a4 4 0 0 1 5.7 0L56 61.1l5.7-5.7a4 4 0 0 1 5.7 0L72 60" stroke="#64748B" stroke-width="4" stroke-linecap="round" stroke-linejoin="round"/>
  <circle cx="37" cy="35" r="6" fill="#94A3B8"/>
  <rect x="20" y="20" width="56" height="56" rx="12" stroke="#94A3B8" stroke-width="4"/>
</svg>
`)}`;

const DEFAULT_PLACEHOLDER_SRC = '/images/placeholder.png';

type NativeImageProps = Omit<
    React.ImgHTMLAttributes<HTMLImageElement>,
    'loading' | 'decoding' | 'src'
>;

export interface OptimizedImageProps extends NativeImageProps {
    src?: string | null;
    fallbackSrc?: string;
    sizes?: string;
    loading?: 'eager' | 'lazy';
}

export const OptimizedImage: React.FC<OptimizedImageProps> = ({
    src,
    alt,
    className,
    sizes = '(max-width: 768px) 100vw, 400px',
    loading = 'lazy',
    fallbackSrc = DEFAULT_PLACEHOLDER_SRC,
    onError,
    ...rest
}) => {
    const [currentSrc, setCurrentSrc] = React.useState(src || fallbackSrc);
    const [usedFallback, setUsedFallback] = React.useState(!src);
    const fetchPriority = loading === 'eager' ? 'high' : 'auto';

    React.useEffect(() => {
        setCurrentSrc(src || fallbackSrc);
        setUsedFallback(!src);
    }, [fallbackSrc, src]);

    const handleError = React.useCallback(
        (event: React.SyntheticEvent<HTMLImageElement, Event>) => {
            if (!usedFallback && currentSrc !== fallbackSrc) {
                setCurrentSrc(fallbackSrc);
                setUsedFallback(true);
            } else if (currentSrc !== INLINE_PLACEHOLDER) {
                setCurrentSrc(INLINE_PLACEHOLDER);
                setUsedFallback(true);
            }

            onError?.(event);
        },
        [currentSrc, fallbackSrc, onError, usedFallback]
    );

    return (
        <img
            {...rest}
            {...{ fetchpriority: fetchPriority }}
            src={currentSrc}
            alt={alt}
            loading={loading}
            decoding="async"
            sizes={sizes}
            className={className}
            onError={handleError}
        />
    );
};

export default OptimizedImage;
