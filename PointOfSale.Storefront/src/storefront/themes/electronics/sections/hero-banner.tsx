import React from 'react';
import { ThemeHeroCTA } from '@/components/theme-components';
import { useTranslation } from '@/i18n';

const heightClasses: Record<string, string> = {
    small: 'min-h-[560px]',
    medium: 'min-h-[700px]',
    large: 'min-h-[800px]',
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
    const resolvedOverlayOpacity = Math.min(Math.max(Number(overlayOpacity ?? 18), 0), 100) / 100;
    const resolvedHeightClass = heightClasses[height] || heightClasses.medium;
    const resolvedAlignmentClass = alignmentClasses[alignment] || alignmentClasses.left;
    return (
        <section
            className={`relative isolate overflow-hidden bg-slate-950 ${resolvedHeightClass}`}
            style={{
                backgroundColor: backgroundColor || '#020617',
                color: textColor || '#ffffff',
            }}
        >
            <div className="absolute inset-0 bg-[linear-gradient(90deg,rgba(148,163,184,0.11)_1px,transparent_1px),linear-gradient(180deg,rgba(148,163,184,0.11)_1px,transparent_1px)] bg-[size:32px_32px]" />
            <div className="absolute inset-0 bg-[linear-gradient(135deg,rgba(56,189,248,0.18)_0%,transparent_42%,rgba(34,197,94,0.12)_100%)]" />

            <div className={`relative mx-auto grid max-w-7xl grid-cols-1 gap-8 px-6 py-12 sm:px-8 lg:grid-cols-[0.88fr_1.12fr] lg:items-center lg:px-12 ${resolvedAlignmentClass}`}>
                <div>
                    <div className="mb-5 inline-flex border border-cyan-300/30 bg-cyan-300/10 px-3 py-2 font-mono text-[11px] font-bold uppercase tracking-[0.16em] text-cyan-200">
                        {t('home.welcome', { storeName: '' })}
                    </div>

                    <h1 className="max-w-3xl text-balance text-4xl font-black leading-[0.98] text-white sm:text-5xl lg:text-7xl">
                        {title || t('home.welcome', { storeName: '' })}
                    </h1>

                    {subtitle && (
                        <p className="mt-6 max-w-2xl text-base leading-8 text-slate-300 sm:text-lg">
                            {subtitle}
                        </p>
                    )}

                    {ctaText && (
                        <ThemeHeroCTA
                            themeId="electronics"
                            href={ctaLink || '/products'}
                            align={alignment || 'left'}
                            className="mt-9"
                        >
                            {ctaText}
                        </ThemeHeroCTA>
                    )}

                    <div className="mt-10 grid max-w-2xl grid-cols-1 gap-3 sm:grid-cols-3">
                        {['IN STOCK', 'WARRANTY READY', 'COMPARE SPECS'].map((label) => (
                            <div key={label} className="border border-slate-700 bg-slate-900/80 px-4 py-3 font-mono text-[11px] font-bold uppercase tracking-[0.1em] text-slate-200">
                                {label}
                            </div>
                        ))}
                    </div>
                </div>

                <div className="relative">
                    <div className="absolute -right-3 -top-3 hidden h-full w-full border border-cyan-300/20 lg:block" />
                    <div className="relative overflow-hidden rounded-md border border-slate-700 bg-slate-900 shadow-2xl shadow-cyan-950/50">
                        <div className="relative aspect-[4/3]">
                            {backgroundImage ? (
                                <img
                                    src={backgroundImage}
                                    alt={title || 'Technical product hero'}
                                    className="h-full w-full object-contain p-8"
                                />
                            ) : (
                                <div className="grid h-full w-full place-items-center bg-[linear-gradient(135deg,#111827_0%,#1e293b_50%,#38bdf8_100%)]">
                                    <div className="h-28 w-48 rounded-md border border-cyan-200/50 bg-slate-950/70 shadow-[0_24px_80px_rgba(56,189,248,0.28)]" />
                                </div>
                            )}
                            <div className="absolute inset-0 bg-slate-950" style={{ opacity: resolvedOverlayOpacity * 0.12 }} />
                        </div>

                        <div className="grid border-t border-slate-700 bg-slate-950/90 font-mono text-[11px] uppercase tracking-[0.08em] text-slate-300 sm:grid-cols-3">
                            <div className="border-b border-slate-800 px-4 py-3 sm:border-b-0 sm:border-r">stock: live</div>
                            <div className="border-b border-slate-800 px-4 py-3 sm:border-b-0 sm:border-r">ship: fast</div>
                            <div className="px-4 py-3">support: 24/7</div>
                        </div>
                    </div>
                </div>
            </div>
        </section>
    );
};

export default HeroBanner;
