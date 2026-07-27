/**
 * Pearl Theme — Luxury Promotional Banner (Phase T1.3)
 *
 * REUSE DECISION
 * --------------
 * ADAPTATION (~85% reuse) of `components/sections/defaults/banner.tsx`, the
 * platform's registered CMS 'banner' section. Contract preserved verbatim:
 * `title, subtitle, ctaText, ctaLink, imageUrl, backgroundImage` (forwarded
 * flat by SectionRenderer). Structure (copy block + optional artwork panel,
 * AffiliateLink CTA, imageUrl||backgroundImage fallback) is retained; only
 * the visual shell changes.
 *
 * Pearl identity (design report §1/§2/§6): dark editorial block `#1E1C24`,
 * SQUARE corners (no 32px-radius pill chrome), gold eyebrow, Newsreader
 * serif light heading, square red #DC2626 uppercase CTA, CSS-only hover.
 */

import React from 'react';
import { AffiliateLink } from '@/components/common/AffiliateLink';
import { useTranslation } from '@/i18n';
import { PEARL_FONT_BODY, PEARL_FONT_DISPLAY } from '../layout/pearlTokens';

const BannerSection: React.FC<any> = ({
    title,
    subtitle,
    ctaText,
    ctaLink,
    imageUrl,
    backgroundImage,
}) => {
    const { t } = useTranslation();
    const artwork = imageUrl || backgroundImage;

    return (
        <section className="bg-white px-4 py-16 sm:px-6 sm:py-20" aria-label={title || t('home.featuredPromotion', { defaultValue: 'Featured promotion' })}>
            <div className="mx-auto max-w-7xl">
                <div className="relative overflow-hidden bg-[#1E1C24] text-white">
                    <div className="grid gap-10 px-6 py-12 sm:px-10 sm:py-16 lg:grid-cols-[1fr_auto] lg:items-center lg:gap-14">
                        <div className="max-w-3xl">
                            <p className={`text-[11px] font-medium uppercase tracking-[0.4em] text-[#C9A863] ${PEARL_FONT_BODY}`}>
                                {t('home.featuredPromotion', { defaultValue: 'Featured promotion' })}
                            </p>
                            <h2 className={`mt-5 text-[clamp(2rem,4vw,3rem)] font-extralight leading-tight tracking-[0.02em] text-[#FAF7F2] ${PEARL_FONT_DISPLAY}`}>
                                {title || t('home.featuredPromotion', { defaultValue: 'Featured promotion' })}
                            </h2>
                            {subtitle ? (
                                <p className={`mt-5 max-w-2xl text-base leading-8 text-[#FAF7F2]/75 ${PEARL_FONT_BODY}`}>
                                    {subtitle}
                                </p>
                            ) : null}

                            {ctaText ? (
                                <AffiliateLink
                                    href={ctaLink || '/products'}
                                    className={`mt-8 inline-flex bg-[#DC2626] px-8 py-3.5 text-[12px] uppercase tracking-[0.2em] text-white transition-colors duration-300 hover:bg-[#b91c1c] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#DC2626] ${PEARL_FONT_BODY}`}
                                >
                                    {ctaText}
                                </AffiliateLink>
                            ) : null}
                        </div>

                        <div className="group overflow-hidden bg-white/5 lg:w-[22rem]">
                            {artwork ? (
                                <img
                                    src={artwork}
                                    alt={title || ''}
                                    loading="lazy"
                                    className="h-56 w-full object-cover transition-transform duration-700 ease-out group-hover:scale-105"
                                />
                            ) : (
                                <div className="grid h-56 place-items-center bg-[radial-gradient(circle_at_center,#2b2833_0%,#1E1C24_70%)]">
                                    <span className={`text-[11px] uppercase tracking-[0.32em] text-[#C9A863]/70 ${PEARL_FONT_BODY}`}>
                                        {t('home.featuredPromotion', { defaultValue: 'Featured promotion' })}
                                    </span>
                                </div>
                            )}
                        </div>
                    </div>
                </div>
            </div>
        </section>
    );
};

export default BannerSection;
