import React from 'react';
import { Button } from '@/components/common';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { useTranslation } from '@/i18n';

const heightClasses: Record<string, string> = {
    small: 'min-h-[420px]',
    medium: 'min-h-[520px]',
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
    const resolvedOverlayOpacity = Math.min(Math.max(Number(overlayOpacity ?? 20), 0), 100) / 100;
    const resolvedHeightClass = heightClasses[height] || heightClasses.medium;
    const resolvedAlignmentClass = alignmentClasses[alignment] || alignmentClasses.left;
    const resolvedBackgroundImage = backgroundImage || imageUrl;

    return (
        <section
            className="border-y border-slate-200"
            style={{
                backgroundColor: backgroundColor || '#ffffff',
                color: textColor || '#0f172a',
            }}
        >
            <div className={`mx-auto grid max-w-7xl grid-cols-1 gap-10 px-6 py-14 lg:grid-cols-[1.05fr_0.95fr] lg:items-center lg:px-10 ${resolvedHeightClass}`}>
                <div className={`flex flex-col justify-center ${resolvedAlignmentClass}`}>
                    <div className="mb-5 text-xs font-semibold uppercase tracking-[0.28em] text-slate-500">
                        {t('home.welcome', { storeName: '' })}
                    </div>

                    <h1 className="max-w-2xl text-4xl font-medium leading-tight tracking-tight text-slate-950 sm:text-5xl">
                        {title || t('home.welcome', { storeName: '' })}
                    </h1>

                    {subtitle && (
                        <p className="mt-5 max-w-xl text-base leading-7 text-slate-600 sm:text-lg">
                            {subtitle}
                        </p>
                    )}

                    {ctaText && (
                        <div className="mt-8">
                            <AffiliateLink href={ctaLink || '/products'}>
                                <Button
                                    size="md"
                                    styleOverride="outline"
                                    className="rounded-none border-slate-950 px-6"
                                >
                                    {ctaText}
                                </Button>
                            </AffiliateLink>
                        </div>
                    )}
                </div>

                <div className="relative">
                    <div className="aspect-[5/4] overflow-hidden border border-slate-200 bg-slate-50">
                        {resolvedBackgroundImage ? (
                            <div className="relative h-full w-full">
                                <img
                                    src={resolvedBackgroundImage}
                                    alt={title || 'Hero background'}
                                    className="h-full w-full object-cover"
                                />
                                <div className="absolute inset-0 bg-black" style={{ opacity: resolvedOverlayOpacity }} />
                            </div>
                        ) : (
                            <div className="flex h-full w-full items-center justify-center bg-[linear-gradient(135deg,#f8fafc_0%,#e2e8f0_100%)] px-8 text-center text-sm font-medium uppercase tracking-[0.25em] text-slate-500">
                                {t('common.products')}
                            </div>
                        )}
                    </div>
                </div>
            </div>
        </section>
    );
};

export default HeroBanner;
