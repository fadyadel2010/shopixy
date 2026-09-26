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
            className={`relative isolate overflow-hidden border-b border-muted/20 bg-background text-text ${resolvedHeightClass}`}
        >
            <div className="absolute inset-x-0 top-0 h-px bg-muted/30" />
            <div className="absolute inset-0 bg-[linear-gradient(110deg,#ffffff_0%,#f8fafc_46%,#f1f5f9_100%)]" />

            <div className="relative mx-auto grid max-w-7xl grid-cols-1 items-center gap-12 px-6 py-16 sm:px-8 lg:grid-cols-[minmax(0,0.92fr)_minmax(0,1.08fr)] lg:px-12">
                <div className="order-2 lg:order-1">
                    <div className="mx-auto max-w-lg border border-muted/30 bg-white p-4 shadow-[0_28px_80px_rgba(1,52,35,0.08)] lg:mx-0">
                        <div className="relative aspect-[4/5] overflow-hidden bg-muted/5">
                            {backgroundImage ? (
                                <img
                                    src={backgroundImage}
                                    alt={title || ''}
                                    className="h-full w-full object-contain p-4"
                                />
                            ) : (
                                <div className="grid h-full w-full place-items-center bg-[linear-gradient(135deg,#f0f9ff_0%,#ffffff_45%,#013423_100%)]">
                                    <div className="h-28 w-28 border border-muted/30 bg-white/40 shadow-[0_20px_60px_rgba(1,52,35,0.15)]" />
                                </div>
                            )}
                            <div className="absolute inset-0 bg-white" style={{ opacity: resolvedOverlayOpacity * 0.18 }} />
                        </div>
                    </div>
                </div>

                <div className="order-1 mx-auto max-w-2xl text-center lg:order-2 lg:text-left">
                    <div className="mx-auto mb-7 h-px w-24 bg-secondary lg:mx-0" />
                    <div className="mb-5 text-[11px] font-semibold uppercase tracking-[0.36em] text-secondary">
                        {t('home.welcome', { storeName: '' })}
                    </div>

                    <h1 className="text-4xl font-semibold leading-tight text-text sm:text-5xl lg:text-6xl">
                        {title || t('home.welcome', { storeName: '' })}
                    </h1>

                    {subtitle && (
                        <p className="mx-auto mt-6 max-w-xl text-base leading-8 text-muted">
                            {subtitle}
                        </p>
                    )}

                    {ctaText && (
                        <ThemeHeroCTA
                            themeId="sharp"
                            href={ctaLink || '/products'}
                            align={ctaAlignment}
                            className="mt-10"
                        >
                            {ctaText}
                        </ThemeHeroCTA>
                    )}

                </div>
            </div>
        </section>
    );
};

export default HeroBanner;
