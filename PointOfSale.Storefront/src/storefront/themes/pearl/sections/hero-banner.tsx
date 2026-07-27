/**
 * Pearl Theme — Hero Banner (Phase T1.2A)
 *
 * REUSE DECISION
 * --------------
 * This file is an ADAPTATION of the inherited `fashion` hero template (copied
 * verbatim into `pearl/sections` during T1.0 scaffolding). The fashion hero
 * already implements the full platform section data contract
 * (`<Component {...section.data} />` from SectionRenderer) plus the responsive
 * layout, background-image/overlay handling, height/alignment maps, and the
 * `AffiliateLink` + platform `Button` CTA plumbing. That is ~85% of the
 * requirement, so per the 80% reuse rule we ADAPT the existing component
 * rather than introduce a new one. No business logic is duplicated.
 *
 * What changed for Pearl identity (design report §2/§3/§4):
 *   - Dark block background `#141218` (was fashion's #111827 + pink gradient)
 *   - Display H1 uses Newsreader serif 60px / weight 300, off-white `#FAF7F2`
 *   - Thin gold micro-label eyebrow (`#C9A863`) instead of white/75
 *   - CTA is a SOLID RED `#DC2626`, SQUARE (radius 0), uppercase — the Pearl
 *     accent (platform `Button` still owns a11y/loading/focus; we only override
 *     its color + radius via className to honor the "sharp editorial" look).
 *   - Default alignment centered (matches the reference hero).
 */

import React from 'react';
import { Button } from '@/components/common';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { useTranslation } from '@/i18n';
import { PEARL_FONT_BODY, PEARL_FONT_DISPLAY } from '../layout/pearlTokens';

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
    imageUrl,
    backgroundColor,
    overlayOpacity,
    textColor,
    alignment,
    height,
}) => {
    const { t } = useTranslation();
    const resolvedOverlayOpacity = Math.min(Math.max(Number(overlayOpacity ?? 40), 0), 100) / 100;
    const resolvedHeightClass = heightClasses[height] || heightClasses.medium;
    const resolvedAlignmentClass = alignmentClasses[alignment] || alignmentClasses.center;
    const resolvedBackgroundImage = backgroundImage || imageUrl;

    return (
        <section
            className={`relative isolate overflow-hidden ${resolvedHeightClass}`}
            style={{
                backgroundColor: backgroundColor || '#141218',
                color: textColor || '#FAF7F2',
            }}
            aria-label={t('home.heroLabel', { defaultValue: 'Featured' })}
        >
            <div className="absolute inset-0">
                {resolvedBackgroundImage ? (
                    <img
                        src={resolvedBackgroundImage}
                        alt=""
                        aria-hidden="true"
                        loading="eager"
                        decoding="async"
                        className="h-full w-full object-cover"
                    />
                ) : (
                    <div className="h-full w-full bg-[radial-gradient(circle_at_center,#1c1922_0%,#0c0a0e_70%)]" />
                )}
                <div className="absolute inset-0 bg-black" style={{ opacity: resolvedOverlayOpacity }} />
                <div className="absolute inset-0 bg-[linear-gradient(180deg,rgba(20,18,24,0.2)_0%,rgba(20,18,24,0.85)_100%)]" />
            </div>

            <div className={`relative mx-auto flex h-full max-w-7xl px-6 py-16 sm:px-8 lg:px-12 ${resolvedAlignmentClass}`}>
                <div className="mt-auto max-w-3xl">
                    <div className={`mb-5 text-[11px] font-medium uppercase tracking-[0.4em] text-[#C9A863] ${PEARL_FONT_BODY}`}>
                        {t('home.newSeason', { defaultValue: 'New Season' })}
                    </div>

                    <h1
                        className={`text-[clamp(2.75rem,6vw,3.75rem)] font-light leading-[1.02] tracking-tight text-[#FAF7F2] ${PEARL_FONT_DISPLAY}`}
                    >
                        {title || t('home.welcome', { storeName: '' })}
                    </h1>

                    {subtitle && (
                        <p className={`mt-6 max-w-2xl text-lg leading-8 text-[#FAF7F2]/80 ${PEARL_FONT_BODY}`}>
                            {subtitle}
                        </p>
                    )}

                    {ctaText && (
                        <div className="mt-10">
                            <AffiliateLink href={ctaLink || '/products'}>
                                <Button
                                    size="lg"
                                    className={`!rounded-none !bg-[#DC2626] uppercase tracking-[0.2em] hover:!bg-[#b91c1c] ${PEARL_FONT_BODY}`}
                                >
                                    {ctaText}
                                </Button>
                            </AffiliateLink>
                        </div>
                    )}
                </div>
            </div>
        </section>
    );
};

export default HeroBanner;
