import React from 'react';
import { ThemeHeroCTA } from '@/components/theme-components';
import { useTranslation } from '@/i18n';

const heightClasses: Record<string, string> = {
    small: 'min-h-[520px]',
    medium: 'min-h-[680px]',
    large: 'min-h-[780px]',
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
    const resolvedOverlayOpacity = Math.min(Math.max(Number(overlayOpacity ?? 50), 0), 100) / 100;
    const resolvedHeightClass = heightClasses[height] || heightClasses.medium;
    const resolvedAlignmentClass = alignmentClasses[alignment] || alignmentClasses.left;
    return (
        <section
            className="relative isolate overflow-hidden border-b border-slate-200 bg-slate-50"
            style={{
                backgroundColor: backgroundColor || '#f8fafc',
                color: textColor || '#0f172a',
            }}
        >
            <div className={`mx-auto grid w-full max-w-7xl grid-cols-1 gap-10 px-6 py-14 sm:px-8 lg:grid-cols-[0.9fr_1.1fr] lg:items-center lg:px-12 ${resolvedHeightClass}`}>
                <div className={`flex flex-col justify-center ${resolvedAlignmentClass}`}>
                    <div className="mb-6 inline-flex w-fit items-center rounded-md border border-primary/20 bg-white px-4 py-2 text-xs font-semibold uppercase tracking-[0.22em] text-primary shadow-sm">
                        {t('home.welcome', { storeName: '' })}
                    </div>

                    <h1 className="max-w-2xl text-balance text-5xl font-semibold leading-[0.98] tracking-tight text-slate-950 sm:text-6xl lg:text-7xl">
                        {title || t('home.welcome', { storeName: '' })}
                    </h1>

                    {subtitle && (
                        <p className="mt-6 max-w-xl text-lg leading-8 text-slate-600">
                            {subtitle}
                        </p>
                    )}

                    {ctaText && (
                        <ThemeHeroCTA
                            themeId="modern"
                            href={ctaLink || '/products'}
                            align={alignment || 'left'}
                            className="mt-10"
                        >
                            {ctaText}
                        </ThemeHeroCTA>
                    )}

                    <div className="mt-10 grid max-w-xl grid-cols-3 gap-3 border-t border-slate-200 pt-6 text-left">
                        <div>
                            <p className="text-2xl font-semibold text-slate-950">24h</p>
                            <p className="mt-1 text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">fresh edits</p>
                        </div>
                        <div>
                            <p className="text-2xl font-semibold text-slate-950">3x</p>
                            <p className="mt-1 text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">easy scan</p>
                        </div>
                        <div>
                            <p className="text-2xl font-semibold text-slate-950">1</p>
                            <p className="mt-1 text-xs font-semibold uppercase tracking-[0.16em] text-slate-500">clear cart</p>
                        </div>
                    </div>
                </div>

                <div className="relative">
                    <div className="absolute -left-6 top-8 hidden h-24 w-24 border border-primary/25 bg-primary/10 lg:block" />
                    <div className="relative overflow-hidden rounded-md bg-slate-950 shadow-2xl shadow-slate-300/60">
                        <div className="relative aspect-[5/4]">
                            {backgroundImage ? (
                                <img
                                    src={backgroundImage}
                                    alt={title || 'Featured storefront collection'}
                                    className="h-full w-full object-cover"
                                />
                            ) : (
                                <div className="h-full w-full bg-[linear-gradient(135deg,#0f172a_0%,#2563eb_48%,#f97316_100%)]" />
                            )}
                            <div className="absolute inset-0 bg-slate-950" style={{ opacity: resolvedOverlayOpacity * 0.28 }} />
                        </div>
                        <div className="absolute inset-x-5 bottom-5 rounded-md border border-white/15 bg-white/12 p-4 text-white backdrop-blur-md">
                            <p className="text-xs font-semibold uppercase tracking-[0.22em] text-white/70">Featured collection</p>
                            <p className="mt-2 text-2xl font-semibold">Ready to shop</p>
                        </div>
                    </div>
                </div>
            </div>
        </section>
    );
};

export default HeroBanner;
