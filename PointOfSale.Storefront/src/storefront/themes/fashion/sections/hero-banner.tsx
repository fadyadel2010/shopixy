import React from 'react';
import { Button } from '@/components/common';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { useTranslation } from '@/i18n';

const heightClasses: Record<string, string> = {
    small: 'min-h-[70vh]',
    medium: 'min-h-screen',
    large: 'min-h-screen',
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
    const resolvedOverlayOpacity = Math.min(Math.max(Number(overlayOpacity ?? 35), 0), 100) / 100;
    const resolvedHeightClass = heightClasses[height] || heightClasses.medium;
    const resolvedAlignmentClass = alignmentClasses[alignment] || alignmentClasses.left;
    const resolvedBackgroundImage = backgroundImage || imageUrl;

    return (
        <section
            className={`relative isolate overflow-hidden ${resolvedHeightClass}`}
            style={{
                backgroundColor: backgroundColor || '#111827',
                color: textColor || '#ffffff',
            }}
        >
            <div className="absolute inset-0">
                {resolvedBackgroundImage ? (
                    <img
                        src={resolvedBackgroundImage}
                        alt={title || 'Fashion hero background'}
                        className="h-full w-full object-cover"
                    />
                ) : (
                    <div className="h-full w-full bg-[radial-gradient(circle_at_top,#f9a8d4,transparent_30%),linear-gradient(135deg,#111827_0%,#4c1d95_55%,#be185d_100%)]" />
                )}
                <div className="absolute inset-0 bg-black" style={{ opacity: resolvedOverlayOpacity }} />
                <div className="absolute inset-0 bg-[linear-gradient(180deg,rgba(0,0,0,0.1)_0%,rgba(0,0,0,0.72)_100%)]" />
            </div>

            <div className={`relative mx-auto flex h-full max-w-7xl px-6 py-16 sm:px-8 lg:px-12 ${resolvedAlignmentClass}`}>
                <div className="mt-auto max-w-3xl">
                    <div className="mb-5 text-sm font-medium uppercase tracking-[0.38em] text-white/75">
                        {t('home.welcome', { storeName: '' })}
                    </div>

                    <h1 className="text-5xl font-semibold leading-[0.95] tracking-tight text-white sm:text-6xl lg:text-8xl">
                        {title || t('home.welcome', { storeName: '' })}
                    </h1>

                    {subtitle && (
                        <p className="mt-6 max-w-2xl text-lg leading-8 text-white/80 sm:text-xl">
                            {subtitle}
                        </p>
                    )}

                    {ctaText && (
                        <div className="mt-10">
                            <AffiliateLink href={ctaLink || '/products'}>
                                <Button
                                    size="lg"
                                    className="rounded-full border border-white/30 bg-white/15 px-8 py-4 backdrop-blur-sm"
                                >
                                    {ctaText}
                                </Button>
                            </AffiliateLink>
                        </div>
                    )}
                </div>
            </div>
        </section>
    );
};

export default HeroBanner;
