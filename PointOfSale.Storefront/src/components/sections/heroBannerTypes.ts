/** Shared authored media contract; independent of a theme's visual layout. */
export interface HeroBannerProps {
    title?: string;
    subtitle?: string;
    ctaText?: string;
    ctaLink?: string;
    backgroundImage?: string;
    backgroundColor?: string;
    overlayOpacity?: number | string;
    textColor?: string;
    alignment?: 'left' | 'center' | 'right';
    height?: 'small' | 'medium' | 'large';
    highlights?: Array<{ value: string; label: string }>;
}
