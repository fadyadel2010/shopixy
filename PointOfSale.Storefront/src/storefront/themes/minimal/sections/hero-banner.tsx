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
    const resolvedOverlayOpacity = Math.min(Math.max(Number(overlayOpacity ?? 20), 0), 100) / 100;
    const resolvedHeightClass = heightClasses[height] || heightClasses.medium;
    const resolvedAlignmentClass = alignmentClasses[alignment] || alignmentClasses.center;
    return (
        <section
            className="border-b border-slate-200 bg-white"
            style={{
                backgroundColor: backgroundColor || '#ffffff',
                color: textColor || '#0f172a',
            }}
        >
            <div className={`mx-auto flex max-w-5xl flex-col justify-center px-6 py-16 lg:px-10 ${resolvedHeightClass}`}>
                <div className={`mx-auto flex max-w-3xl flex-col ${resolvedAlignmentClass}`}>
                    <div className="mb-8 text-xs font-medium uppercase tracking-[0.3em] text-slate-500">
                        {t('home.welcome', { storeName: '' })}
                    </div>

                    <h1 className="max-w-3xl text-balance text-4xl font-normal leading-tight text-slate-950 sm:text-5xl lg:text-6xl">
                        {title || t('home.welcome', { storeName: '' })}
                    </h1>

                    {subtitle && (
                        <p className="mx-auto mt-6 max-w-xl text-base leading-8 text-slate-600">
                            {subtitle}
                        </p>
                    )}

                    {ctaText && (
                        <ThemeHeroCTA
                            themeId="minimal"
                            href={ctaLink || '/products'}
                            align={alignment || 'center'}
                            className="mt-9"
                        >
                            {ctaText}
                        </ThemeHeroCTA>
                    )}
                </div>

                <div className="mx-auto mt-14 w-full max-w-xl">
                    <div className="relative aspect-[4/5] overflow-hidden border border-slate-200 bg-slate-50">
                        {backgroundImage ? (
                            <img
                                src={backgroundImage}
                                alt={title || 'Focused product image'}
                                className="h-full w-full object-cover"
                            />
                        ) : (
                            <div className="grid h-full w-full place-items-center bg-[linear-gradient(180deg,#ffffff_0%,#f8fafc_100%)] px-10">
                                <div className="h-2/3 w-2/3 border border-slate-300 bg-white" />
                            </div>
                        )}
                        <div className="absolute inset-0 bg-white" style={{ opacity: resolvedOverlayOpacity * 0.12 }} />
                    </div>
                    <div className="mt-5 flex items-center justify-between border-t border-slate-200 pt-4 text-xs uppercase tracking-[0.22em] text-slate-500">
                        <span>selected</span>
                        <span>quiet edit</span>
                    </div>
                </div>
            </div>
        </section>
    );
};

export default HeroBanner;
