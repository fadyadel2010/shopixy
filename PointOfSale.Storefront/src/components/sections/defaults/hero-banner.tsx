import React from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { useTranslation } from '@/i18n';

const heightClasses: Record<string, string> = {
    small: 'min-h-[360px]',
    medium: 'min-h-[500px]',
    large: 'min-h-[640px]',
};

const alignmentClasses: Record<string, string> = {
    left: 'items-start text-left',
    center: 'items-center text-center',
    right: 'items-end text-right',
};

const HeroBanner: React.FC<any> = ({
    title,
    subtitle,
    ctaText,
    ctaLink,
    backgroundImage,
    imageUrl,
    backgroundColor,
    overlayOpacity,
    textColor,
    alignment,
    height,
}) => {
    const { t } = useTranslation();
    const resolvedOverlayOpacity = Math.min(Math.max(Number(overlayOpacity ?? 45), 0), 100) / 100;
    const resolvedHeightClass = heightClasses[height] || heightClasses.medium;
    const resolvedAlignmentClass = alignmentClasses[alignment] || alignmentClasses.center;
    const resolvedBackgroundImage = backgroundImage || imageUrl;

    return (
        <section
            className="relative isolate overflow-hidden px-6"
            style={{
                backgroundColor: backgroundColor || '#1e40af',
                color: textColor || '#ffffff',
            }}
        >
            {resolvedBackgroundImage && (
                <>
                    <img
                        src={resolvedBackgroundImage}
                        alt={title || 'Hero background'}
                        className="absolute inset-0 h-full w-full object-cover"
                    />
                    <div
                        className="absolute inset-0 bg-black"
                        style={{ opacity: resolvedOverlayOpacity }}
                    />
                </>
            )}

            <div className={`relative z-10 mx-auto flex w-full max-w-5xl flex-col justify-center ${resolvedHeightClass} ${resolvedAlignmentClass}`}>
                <h1 className="text-4xl font-bold mb-4 md:text-6xl">
                    {title || t('home.welcome', { storeName: '' })}
                </h1>
                {subtitle ? (
                    <p className="mb-8 max-w-2xl text-xl opacity-85">{subtitle}</p>
                ) : null}
                {ctaText && (
                    <AffiliateLink
                        href={ctaLink || '/products'}
                        className="inline-block rounded-full bg-primary px-8 py-3 font-semibold text-white transition-opacity hover:opacity-90"
                    >
                        {ctaText}
                    </AffiliateLink>
                )}
            </div>
        </section>
    );
};

export default HeroBanner;
