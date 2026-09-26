import React from 'react';
import { ThemeHeroCTA } from '@/components/theme-components';
import { useTranslation } from '@/i18n';

const heightClasses: Record<string, string> = {
    small: 'min-h-[70vh]',
    medium: 'min-h-[88vh]',
    large: 'min-h-screen',
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
    const resolvedOverlayOpacity = Math.min(Math.max(Number(overlayOpacity ?? 45), 0), 100) / 100;
    const resolvedHeightClass = heightClasses[height] || heightClasses.medium;
    const resolvedAlignmentClass = alignmentClasses[alignment] || alignmentClasses.left;
    return (
        <section
            className={`relative isolate overflow-hidden ${resolvedHeightClass}`}
            style={{
                backgroundColor: backgroundColor || '#141218',
                color: textColor || '#ffffff',
            }}
        >
            <div className="absolute inset-0">
                {backgroundImage ? (
                    <img
                        src={backgroundImage}
                        alt={title || ''}
                        className="h-full w-full object-cover"
                    />
                ) : (
                    <div className="h-full w-full bg-[linear-gradient(120deg,rgba(12,10,14,0.58)_0%,rgba(12,10,14,0.4)_45%,rgba(12,10,14,0.22)_100%)]" />
                )}
                <div className="absolute inset-0 bg-black" style={{ opacity: resolvedOverlayOpacity }} />
            </div>

            <div className={`relative mx-auto grid h-full max-w-7xl grid-cols-1 px-6 py-14 sm:px-8 lg:grid-cols-[0.22fr_0.78fr] lg:px-12 ${resolvedAlignmentClass}`}>
                <div className="hidden border-r border-white/20 pr-6 pt-4 lg:block">
                </div>
                <div className="mt-auto max-w-4xl lg:pl-12">
                    <div className="mb-5 inline-flex border-b-2 border-white pb-2 text-sm font-black uppercase tracking-[0.38em] text-white/80">
                        {t('home.welcome', { storeName: '' })}
                    </div>

                    <h1 className="text-balance text-5xl font-black uppercase leading-[0.88] text-white sm:text-7xl lg:text-9xl">
                        {title || t('home.welcome', { storeName: '' })}
                    </h1>

                    {subtitle && (
                        <p className="mt-6 max-w-2xl text-lg leading-8 text-white/80 sm:text-xl">
                            {subtitle}
                        </p>
                    )}

                    {ctaText && (
                        <ThemeHeroCTA
                            themeId="seven"
                            href={ctaLink || '/products'}
                            align={alignment || 'left'}
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
