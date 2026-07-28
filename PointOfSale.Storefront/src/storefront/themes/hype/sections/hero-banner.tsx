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
    const resolvedHeightClass = heightClasses[height] || heightClasses.large;
    const resolvedAlignmentClass = alignmentClasses[alignment] || alignmentClasses.center;
    // Hype hero: dark band (#141218) bracketing an otherwise white page.
    const resolvedBackgroundColor = backgroundColor || '#141218';
    const resolvedTextColor = textColor || '#FAF7F2';

    return (
        <section
            className={`relative isolate overflow-hidden ${resolvedHeightClass}`}
            style={{
                backgroundColor: resolvedBackgroundColor,
                color: resolvedTextColor,
            }}
        >
            <div className="absolute inset-x-0 top-0 h-px bg-[#F06736]" />

            <div
                className={`relative mx-auto grid max-w-7xl grid-cols-1 gap-12 px-6 py-20 sm:px-8 lg:grid-cols-[1.1fr_0.9fr] lg:items-center lg:px-12 ${resolvedAlignmentClass}`}
            >
                <div className="order-1 mx-auto max-w-2xl text-center">
                    <div className="mx-auto mb-6 h-px w-20 bg-[#F06736]" />
                    <div className="mb-5 text-[12px] font-semibold uppercase tracking-[4.2px] text-white/80">
                        {t('home.welcome', { storeName: '' })}
                    </div>

                    <h1 className="font-['Newsreader',serif] text-4xl font-light leading-tight text-[#FAF7F2] sm:text-5xl lg:text-6xl">
                        {title || t('home.welcome', { storeName: '' })}
                    </h1>

                    {subtitle && (
                        <p className="mx-auto mt-6 max-w-xl text-base leading-8 text-white/75">
                            {subtitle}
                        </p>
                    )}

                    {ctaText && (
                        <ThemeHeroCTA
                            themeId="hype"
                            href={ctaLink || '/products'}
                            align={alignment || 'center'}
                            className="mt-10"
                        >
                            {ctaText}
                        </ThemeHeroCTA>
                    )}
                </div>

                <div className="order-2">
                    <div className="mx-auto max-w-md overflow-hidden border border-white/15 bg-white/5 shadow-[0_24px_70px_rgba(0,0,0,0.4)]">
                        <div className="relative aspect-[4/5] overflow-hidden bg-[#1c1822]">
                            {backgroundImage ? (
                                <img
                                    src={backgroundImage}
                                    alt={title || 'Hype hero image'}
                                    className="h-full w-full object-cover"
                                />
                            ) : (
                                <div className="grid h-full w-full place-items-center bg-[linear-gradient(135deg,#141218_0%,#21262b_55%,#F06736_100%)]">
                                    <div className="h-28 w-28 border border-[#F06736]/60 bg-white/10 shadow-[0_20px_60px_rgba(240,103,54,0.3)]" />
                                </div>
                            )}
                            <div className="absolute inset-0 bg-black" style={{ opacity: resolvedOverlayOpacity * 0.2 }} />
                        </div>
                    </div>
                </div>
            </div>
        </section>
    );
};

export default HeroBanner;
