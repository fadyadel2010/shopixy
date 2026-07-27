/**
 * Pearl Theme — Newsletter Signup (Phase T1.3)
 *
 * REUSE DECISION
 * --------------
 * ADAPTATION (~85% reuse) of `components/sections/defaults/newsletter-signup.tsx`.
 * Contract preserved verbatim: `heading, subheading, buttonText,
 * backgroundColor, textColor`. Same i18n default-swap logic
 * (footer.stayUpdated / footer.subscribeText / footer.subscribe /
 * footer.enterEmail), same preventDefault form (no new business logic —
 * subscription wiring is platform-owned, out of scope).
 *
 * Pearl identity (design report §4 item 13 / §2): dark `#1D1C1C` block,
 * gold eyebrow, Newsreader light heading, SQUARE input + solid red
 * `#DC2626` uppercase SUBSCRIBE button (no pill radius).
 */

import React from 'react';
import { useTranslation } from '@/i18n';
import { PEARL_FONT_BODY, PEARL_FONT_DISPLAY } from '../layout/pearlTokens';

const NewsletterSignup: React.FC<any> = ({
    heading,
    subheading,
    buttonText,
    backgroundColor,
    textColor,
}) => {
    const { t } = useTranslation();

    const displayHeading = (!heading || heading === 'Stay Updated')
        ? t('footer.stayUpdated')
        : heading;
    const displaySubheading = (!subheading || subheading === 'Subscribe to our newsletter for exclusive offers and updates')
        ? t('footer.subscribeText')
        : subheading;
    const displayButtonText = (!buttonText || buttonText === 'Subscribe')
        ? t('footer.subscribe')
        : buttonText;

    return (
        <section
            className="px-4 py-20 sm:py-24"
            style={{
                backgroundColor: backgroundColor || '#1D1C1C',
                color: textColor || '#FAF7F2',
            }}
            aria-label={displayHeading}
        >
            <div className="mx-auto max-w-2xl text-center">
                <p className={`text-[11px] font-medium uppercase tracking-[0.4em] text-[#C9A863] ${PEARL_FONT_BODY}`}>
                    {t('footer.stayUpdated')}
                </p>
                <h2 className={`mt-4 text-[clamp(1.75rem,3.5vw,2.5rem)] font-extralight leading-tight tracking-[0.02em] ${PEARL_FONT_DISPLAY}`}>
                    {displayHeading}
                </h2>
                <p className={`mt-5 text-base leading-7 opacity-70 ${PEARL_FONT_BODY}`}>
                    {displaySubheading}
                </p>
                <form
                    className="mt-10 flex flex-col justify-center gap-3 sm:flex-row sm:gap-0"
                    onSubmit={(event) => event.preventDefault()}
                >
                    <input
                        type="email"
                        autoComplete="email"
                        aria-label={t('footer.enterEmail')}
                        placeholder={t('footer.enterEmail')}
                        className={`w-full max-w-md flex-1 bg-white px-6 py-3.5 text-sm text-[#131316] placeholder:text-[#131316]/50 focus:outline-none focus-visible:outline focus-visible:outline-2 focus-visible:outline-[#DC2626] ${PEARL_FONT_BODY}`}
                    />
                    <button
                        type="submit"
                        className={`bg-[#DC2626] px-8 py-3.5 text-[12px] uppercase tracking-[0.2em] text-white transition-colors duration-300 hover:bg-[#b91c1c] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-white ${PEARL_FONT_BODY}`}
                    >
                        {displayButtonText}
                    </button>
                </form>
            </div>
        </section>
    );
};

export default NewsletterSignup;
