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
            className={`relative isolate overflow-hidden bg-[#FFFFFF] text-[#131316] ${resolvedHeightClass}`}
        >
            <div className="absolute inset-x-0 top-0 h-px bg-[#FF0000]" />
            <div className="absolute inset-0 bg-[radial-gradient(circle_at_20%_20%,rgba(255,0,0,0.08),transparent_34%),linear-gradient(135deg,#FFFFFF_0%,#F8FAFC_58%,#F1F5F9_100%)]" />

            <div
                className="relative mx-auto grid max-w-7xl grid-cols-1 items-center gap-12 px-6 py-16 sm:px-8 lg:grid-cols-[minmax(0,0.96fr)_minmax(0,1.04fr)] lg:px-12"
            >
                <div className="order-1 mx-auto max-w-2xl text-center lg:mx-0 lg:text-left">
                    <div className="mx-auto mb-6 h-px w-20 bg-[#FF0000] lg:mx-0" />
                    <div className="mb-5 text-[12px] font-semibold uppercase tracking-[4.2px] text-[#131316]/80">
                        {t('home.welcome', { storeName: '' })}
                    </div>

                    <h1 className="font-['Newsreader',serif] text-4xl font-light leading-tight text-[#131316] sm:text-5xl lg:text-7xl">
                        {title || t('home.welcome', { storeName: '' })}
                    </h1>

                    {subtitle && (
                        <p className="mx-auto mt-6 max-w-xl text-base leading-8 text-[#131316]/75">
                            {subtitle}
                        </p>
                    )}

                    {ctaText && (
                        <ThemeHeroCTA
                            themeId="alizon"
                            href={ctaLink || '/products'}
                            align={ctaAlignment}
                            className="mt-10"
                        >
                            {ctaText}
                        </ThemeHeroCTA>
                    )}
                </div>

                <div className="order-2">
                    <div className="mx-auto max-w-xl overflow-hidden border border-[#E5E7EB] bg-white shadow-[0_28px_90px_rgba(0,0,0,0.10)] lg:mx-0">
                        <div className="relative aspect-[4/3] overflow-hidden bg-white">
                            {backgroundImage ? (
                                <img
                                    src={backgroundImage}
                                    alt={title || ''}
                                    className="h-full w-full object-contain p-4"
                                />
                            ) : (
                                <div className="grid h-full w-full place-items-center bg-[linear-gradient(135deg,#FFFFFF_0%,#F8FAFC_55%,#FF0000_100%)]">
                                    <div className="h-28 w-28 border border-[#FF0000]/60 bg-[#FF0000]/10 shadow-[0_20px_60px_rgba(255,0,0,0.3)]" />
                                </div>
                            )}
                            <div className="absolute inset-0 bg-black" style={{ opacity: resolvedOverlayOpacity * 0.15 }} />
                        </div>
                    </div>
                </div>
            </div>
        </section>
    );
};

export default HeroBanner;
