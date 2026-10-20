import React from 'react';
import type { HeroBannerProps } from '@/components/sections/heroBannerTypes';
import { OptimizedImage } from '@/components/media';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { useTranslation } from '@/i18n';
import { COSMETICS_FONT } from '../layout/cosmeticsTokens';

const heightClasses: Record<string, string> = {
    small: 'min-h-[min(480px,65svh)]',
    medium: 'min-h-[min(640px,75svh)]',
    large: 'min-h-[min(760px,85svh)]',
};

const HeroBanner: React.FC<HeroBannerProps> = ({
    title,
    subtitle,
    ctaText,
    ctaLink,
    backgroundImage,
    backgroundColor,
    overlayOpacity,
    alignment,
    height = 'medium',
}) => {
    const { t } = useTranslation();
    const resolvedOverlayOpacity = Math.min(Math.max(Number(overlayOpacity ?? 12), 0), 100) / 100;
    const resolvedHeightClass = heightClasses[height] || heightClasses.medium;
    const isRight = alignment === 'right';
    const isCenter = alignment === 'center';

    return (
        <section
            className={`${COSMETICS_FONT} relative isolate -mt-[100px] overflow-hidden text-white md:-mt-[106px] ${resolvedHeightClass}`}
            style={{ backgroundColor: backgroundColor || '#7a635c' }}
        >
            {backgroundImage ? (
                <OptimizedImage loading="eager"
                    src={backgroundImage}
                    alt={title || ''}
                    className="absolute inset-0 h-full w-full object-cover"
                />
            ) : (
                <div className="absolute inset-0 bg-[linear-gradient(115deg,#b98b72_0%,#e7dfdb_42%,#52636a_100%)]">
                    <div className="absolute bottom-[18%] right-[12%] h-[44%] w-[18%] bg-[#7d4b3a]/70 shadow-[28px_0_0_rgba(82,52,42,0.55),56px_0_0_rgba(52,32,28,0.55)]" />
                    <div className="absolute inset-y-0 left-[12%] w-[34%] bg-white/10" />
                </div>
            )}
            <div className="absolute inset-0 bg-black" style={{ opacity: resolvedOverlayOpacity }} />

            <div
                className={`relative z-10 flex min-h-[inherit] px-5 pb-8 pt-[132px] md:px-10 md:pb-[18vh] md:pt-[150px] ${
                    isCenter
                        ? 'items-end justify-center text-center'
                        : isRight
                            ? 'items-end justify-end text-right'
                            : 'items-end justify-start text-left'
                }`}
            >
                <div className="w-full max-w-xl">
                    <h1 className="text-[32px] font-black uppercase leading-none tracking-normal md:text-6xl">
                        {title || t('home.welcome', { storeName: '' })}
                    </h1>
                    {subtitle ? (
                        <p className="mt-3 text-xs font-bold uppercase tracking-[0.08em] md:text-base">
                            {subtitle}
                        </p>
                    ) : null}
                    {ctaText ? (
                        <AffiliateLink
                            href={ctaLink || '/products'}
                            className="mt-5 inline-flex min-h-11 w-full items-center justify-center border border-white px-7 py-3 text-xs font-bold uppercase tracking-[0.08em] text-white transition-colors hover:bg-white hover:text-[#131316] md:w-auto md:min-w-[145px] md:text-sm"
                        >
                            {ctaText}
                        </AffiliateLink>
                    ) : null}
                </div>
            </div>
        </section>
    );
};

export default HeroBanner;
