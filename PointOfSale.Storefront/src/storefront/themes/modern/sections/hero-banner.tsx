import React from 'react';
import { Button } from '@/components/common';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { useTranslation } from '@/i18n';

const heightClasses: Record<string, string> = {
    small: 'min-h-[420px]',
    medium: 'min-h-[72vh]',
    large: 'min-h-[86vh]',
};

const alignmentClasses: Record<string, string> = {
    left: 'items-start text-left',
    center: 'items-center text-center',
    right: 'items-end text-right',
};

const ctaAlignmentClasses: Record<string, string> = {
    left: 'justify-start',
    center: 'justify-center',
    right: 'justify-end',
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
    const resolvedOverlayOpacity = Math.min(Math.max(Number(overlayOpacity ?? 50), 0), 100) / 100;
    const resolvedHeightClass = heightClasses[height] || heightClasses.medium;
    const resolvedAlignmentClass = alignmentClasses[alignment] || alignmentClasses.center;
    const resolvedCtaAlignmentClass = ctaAlignmentClasses[alignment] || ctaAlignmentClasses.center;
    const resolvedBackgroundImage = backgroundImage || imageUrl;

    return (
        <section
            className="relative isolate overflow-hidden"
            style={{
                backgroundColor: backgroundColor || '#0f172a',
                color: textColor || '#ffffff',
            }}
        >
            <div className="absolute inset-0">
                {resolvedBackgroundImage ? (
                    <img
                        src={resolvedBackgroundImage}
                        alt={title || 'Hero background'}
                        className="h-full w-full object-cover"
                    />
                ) : (
                    <div className="h-full w-full bg-[radial-gradient(circle_at_top,rgba(255,255,255,0.16),transparent_42%),linear-gradient(135deg,#0f172a_0%,#1d4ed8_55%,#38bdf8_100%)]" />
                )}
                <div className="absolute inset-0 bg-slate-950" style={{ opacity: resolvedOverlayOpacity }} />
                <div className="absolute inset-0 bg-[linear-gradient(180deg,rgba(15,23,42,0.18)_0%,rgba(15,23,42,0.78)_100%)]" />
            </div>

            <div className={`relative mx-auto flex w-full max-w-7xl px-6 py-24 sm:px-8 lg:px-12 ${resolvedHeightClass}`}>
                <div className={`flex w-full flex-col justify-center ${resolvedAlignmentClass}`}>
                    <div className="mb-6 inline-flex items-center rounded-full border border-white/20 bg-white/10 px-4 py-2 text-sm font-medium tracking-[0.2em] text-white/80 uppercase backdrop-blur-md">
                        {t('home.welcome', { storeName: '' })}
                    </div>

                    <h1 className="text-balance text-5xl font-semibold leading-none tracking-tight sm:text-6xl lg:text-8xl">
                        {title || t('home.welcome', { storeName: '' })}
                    </h1>

                    {subtitle && (
                        <p className="mt-6 max-w-2xl text-lg leading-8 text-white/78 sm:text-xl">
                            {subtitle}
                        </p>
                    )}

                    {ctaText && (
                        <div className={`mt-10 flex ${resolvedCtaAlignmentClass}`}>
                            <AffiliateLink href={ctaLink || '/products'}>
                                <Button
                                    size="lg"
                                    className="rounded-full px-8 py-4 shadow-2xl shadow-slate-950/35"
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
