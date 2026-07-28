import React from 'react';
import { ThemeHeroCTA } from '@/components/theme-components';
import { useTranslation } from '@/i18n';

const heightClasses: Record<string, string> = {
    small: 'min-h-[560px]',
    medium: 'min-h-[720px]',
    large: 'min-h-[820px]',
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
    backgroundColor,
    overlayOpacity,
    textColor,
    alignment,
    height,
}) => {
    const { t } = useTranslation();
    const resolvedOverlayOpacity = Math.min(Math.max(Number(overlayOpacity ?? 12), 0), 100) / 100;
    const resolvedHeightClass = heightClasses[height] || heightClasses.medium;
    const resolvedAlignmentClass = alignmentClasses[alignment] || alignmentClasses.left;
    return (
        <section
            className={`relative isolate overflow-hidden border-b border-[#C9A863]/30 bg-[#FBFAF7] ${resolvedHeightClass}`}
            style={{
                backgroundColor: backgroundColor || '#FBFAF7',
                color: textColor || '#131316',
            }}
        >
            <div className="absolute inset-x-0 top-0 h-px bg-[#C9A863]" />

            <div className={`relative mx-auto grid max-w-7xl grid-cols-1 gap-12 px-6 py-16 sm:px-8 lg:grid-cols-[0.95fr_1.05fr] lg:items-center lg:px-12 ${resolvedAlignmentClass}`}>
                <div className="order-2 lg:order-1">
                    <div className="mx-auto max-w-md border border-[#C9A863]/35 bg-white p-3 shadow-[0_24px_70px_rgba(19,19,22,0.08)] lg:mx-0">
                        <div className="relative aspect-[4/5] overflow-hidden bg-[#f5f0e8]">
                            {backgroundImage ? (
                                <img
                                    src={backgroundImage}
                                    alt={title || 'Pearl boutique hero image'}
                                    className="h-full w-full object-cover"
                                />
                            ) : (
                                <div className="grid h-full w-full place-items-center bg-[linear-gradient(135deg,#fffaf0_0%,#f8fafc_45%,#313E61_100%)]">
                                    <div className="h-28 w-28 border border-[#C9A863]/70 bg-white/40 shadow-[0_20px_60px_rgba(201,168,99,0.22)]" />
                                </div>
                            )}
                            <div className="absolute inset-0 bg-white" style={{ opacity: resolvedOverlayOpacity * 0.18 }} />
                        </div>
                    </div>
                </div>

                <div className="order-1 mx-auto max-w-2xl text-center lg:order-2">
                    <div className="mx-auto mb-7 h-px w-24 bg-[#C9A863]" />
                    <div className="mb-5 text-[11px] font-semibold uppercase tracking-[0.36em] text-[#C9A863]">
                        {t('home.welcome', { storeName: '' })}
                    </div>

                    <h1 className="font-serif text-4xl font-normal leading-tight text-[#131316] sm:text-5xl lg:text-6xl">
                        {title || t('home.welcome', { storeName: '' })}
                    </h1>

                    {subtitle && (
                        <p className="mx-auto mt-6 max-w-xl text-base leading-8 text-[#313E61]">
                            {subtitle}
                        </p>
                    )}

                    {ctaText && (
                        <ThemeHeroCTA
                            themeId="pearl"
                            href={ctaLink || '/products'}
                            align={alignment || 'center'}
                            className="mt-10"
                        >
                            {ctaText}
                        </ThemeHeroCTA>
                    )}

                    <div className="mx-auto mt-12 grid max-w-xl grid-cols-3 gap-4 border-y border-[#C9A863]/25 py-5 text-[10px] font-semibold uppercase tracking-[0.26em] text-[#313E61]">
                        <span>crafted</span>
                        <span>gift-ready</span>
                        <span>private edit</span>
                    </div>
                </div>
            </div>
        </section>
    );
};

export default HeroBanner;
