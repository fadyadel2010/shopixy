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
    const resolvedOverlayOpacity = Math.min(Math.max(Number(overlayOpacity ?? 18), 0), 100) / 100;
    const resolvedHeightClass = heightClasses[height] || heightClasses.medium;
    const ctaAlignment = alignment || 'left';

    return (
        <section
            className={`relative isolate overflow-hidden bg-[#ece9e2] text-[#131316] ${resolvedHeightClass}`}
        >
            <div className="absolute inset-x-0 top-0 h-1 bg-[#F25A2A]" />
            <div className="absolute inset-0 bg-[linear-gradient(135deg,#f8f5ef_0%,#ece9e2_48%,#d9d2c5_100%)]" />
            <div className="absolute inset-y-0 right-0 hidden w-1/2 bg-[#151515] lg:block" />

            <div className="relative mx-auto grid max-w-7xl grid-cols-1 items-center gap-12 px-6 py-16 sm:px-8 lg:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)] lg:px-12">
                <div className="order-1 max-w-2xl text-center lg:text-left">
                    <div className="mx-auto mb-6 h-px w-20 bg-[#F25A2A] lg:mx-0" />
                    <div className="mb-5 text-[12px] font-black uppercase tracking-[0.32em] text-[#F25A2A]">
                        {t('home.welcome', { storeName: '' })}
                    </div>

                    <h1 className="text-5xl font-black leading-[0.98] text-[#131316] sm:text-6xl lg:text-8xl">
                        {title || t('home.welcome', { storeName: '' })}
                    </h1>

                    {subtitle && (
                        <p className="mx-auto mt-6 max-w-xl text-lg leading-8 text-[#3d3934] sm:text-xl lg:mx-0">
                            {subtitle}
                        </p>
                    )}

                    {ctaText && (
                        <ThemeHeroCTA
                            themeId="phoenix"
                            href={ctaLink || '/products'}
                            align={ctaAlignment}
                            className="mt-10"
                        >
                            {ctaText}
                        </ThemeHeroCTA>
                    )}
                </div>

                <div className="order-2">
                    <div className="mx-auto max-w-xl border border-black/10 bg-white p-4 shadow-[0_28px_90px_rgba(0,0,0,0.28)] lg:mx-0">
                        <div className="relative aspect-[4/5] overflow-hidden bg-[#f7f5f0]">
                            {backgroundImage ? (
                                <img
                                    src={backgroundImage}
                                    alt={title || 'Phoenix hero image'}
                                    className="h-full w-full object-contain p-4"
                                />
                            ) : (
                                <div className="grid h-full w-full place-items-center bg-[linear-gradient(135deg,#f8f5ef_0%,#ffffff_52%,#F25A2A_100%)]">
                                    <div className="h-28 w-28 border border-[#F25A2A]/60 bg-white/60 shadow-[0_20px_60px_rgba(242,90,42,0.24)]" />
                                </div>
                            )}
                            <div className="absolute inset-0 bg-white" style={{ opacity: resolvedOverlayOpacity * 0.16 }} />
                        </div>
                    </div>
                </div>
            </div>
        </section>
    );
};

export default HeroBanner;
