import React from 'react';
import { ThemeHeroCTA } from '@/components/theme-components';
import { useTranslation } from '@/i18n';

const heightClasses: Record<string, string> = {
    small: 'min-h-[560px]',
    medium: 'min-h-[720px]',
    large: 'min-h-[820px]',
};

const HeroBanner: React.FC<any> = ({
    title,
    subtitle,
    ctaText,
    ctaLink,
    backgroundImage,
    overlayOpacity,
    alignment,
    height,
}) => {
    const { t } = useTranslation();
    const resolvedOverlayOpacity = Math.min(Math.max(Number(overlayOpacity ?? 12), 0), 100) / 100;
    const resolvedHeightClass = heightClasses[height] || heightClasses.medium;
    const ctaAlignment = alignment || 'left';
    return (
        <section
            className={`relative isolate overflow-hidden border-b border-[#C9A863]/30 bg-[#FBFAF7] text-[#131316] ${resolvedHeightClass}`}
        >
            <div className="absolute inset-x-0 top-0 h-px bg-[#C9A863]/70" />
            <div className="absolute inset-0 bg-[linear-gradient(110deg,#fffefa_0%,#fbfaf7_46%,#f2ebdc_100%)]" />

            <div className="relative mx-auto grid max-w-7xl grid-cols-1 items-center gap-12 px-6 py-16 sm:px-8 lg:grid-cols-[minmax(0,0.92fr)_minmax(0,1.08fr)] lg:px-12">
                <div className="order-2 lg:order-1">
                    <div className="mx-auto max-w-lg border border-[#C9A863]/35 bg-white p-4 shadow-[0_28px_80px_rgba(58,45,22,0.12)] lg:mx-0">
                        <div className="relative aspect-[4/5] overflow-hidden bg-[#f5f0e8]">
                            {backgroundImage ? (
                                <img
                                    src={backgroundImage}
                                    alt={title || 'Pearl boutique hero image'}
                                    className="h-full w-full object-contain p-4"
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

                <div className="order-1 mx-auto max-w-2xl text-center lg:order-2 lg:text-left">
                    <div className="mx-auto mb-7 h-px w-24 bg-[#C9A863] lg:mx-0" />
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
                            align={ctaAlignment}
                            className="mt-10"
                        >
                            {ctaText}
                        </ThemeHeroCTA>
                    )}

                    <div className="mx-auto mt-12 grid max-w-xl grid-cols-3 gap-4 border-y border-[#C9A863]/25 py-5 text-[10px] font-semibold uppercase tracking-[0.26em] text-[#313E61] lg:mx-0">
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
