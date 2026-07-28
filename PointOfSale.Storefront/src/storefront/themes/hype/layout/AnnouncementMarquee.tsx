/**
 * Hype Theme — Announcement Marquee (Phase H1.1)
 *
 * A reusable, orange announcement strip for the Hype theme.
 *
 * Design (from report): orange (#F06736) scrolling strip, white uppercase text,
 * repeated with bullet (•) separators, sits above the header.
 *
 * Reuse note: there is NO existing marquee/announcement primitive in the
 * platform, so this is a new, self-contained, theme-scoped component. It does
 * not duplicate any business logic — it only renders a presentational strip.
 */

import React from 'react';
import { useTranslation } from '@/i18n';
import { HYPE_BRAND_BG, HYPE_EYEBROW, HYPE_FONT_BODY } from './hypeTokens';

export interface AnnouncementMarqueeProps {
    /** Enable/disable the strip. Defaults to true. */
    enabled?: boolean;
    /** Explicit text (LTR). When omitted, falls back to the i18n default. */
    text?: string;
    /** Explicit Arabic text. When omitted, `text` or i18n default is used. */
    textAr?: string;
    /** Optional accent background override class (defaults to Hype orange). */
    accentClass?: string;
}

export interface ResolvedAnnouncement {
    visible: boolean;
    text: string;
    isRTL: boolean;
}

/**
 * Pure resolution of marquee visibility + copy.
 * Extracted so behavior is unit-testable without rendering.
 */
export function resolveAnnouncement(
    args: {
        enabled?: boolean;
        text?: string;
        textAr?: string;
        language?: string;
        defaultText?: string;
        direction?: 'ltr' | 'rtl';
    }
): ResolvedAnnouncement {
    const enabled = args.enabled ?? true;
    if (!enabled) {
        return { visible: false, text: '', isRTL: false };
    }

    const isRTL = args.direction === 'rtl' || args.language === 'ar';
    const localized = isRTL && args.textAr ? args.textAr : (args.text ?? args.defaultText ?? '');
    return { visible: localized.trim().length > 0, text: localized, isRTL };
}

const AnnouncementMarquee: React.FC<AnnouncementMarqueeProps> = ({
    enabled = true,
    text,
    textAr,
    accentClass = HYPE_BRAND_BG,
}) => {
    const { t, language, direction } = useTranslation();
    const resolved = resolveAnnouncement({
        enabled,
        text,
        textAr,
        language,
        defaultText: t('announcement.defaultText'),
        direction,
    });

    if (!resolved.visible) {
        return null;
    }

    // Repeat the message to create a continuous scrolling band.
    const items = Array.from({ length: 6 });

    return (
        <div
            role="region"
            aria-label={t('announcement.label')}
            dir={resolved.isRTL ? 'rtl' : 'ltr'}
            className={`${accentClass} overflow-hidden`}
        >
            <div
                className={`flex w-max ${HYPE_FONT_BODY} animate-[marquee_22s_linear_infinite] items-center`}
                aria-hidden={false}
            >
                {items.map((_, index) => (
                    <span
                        key={index}
                        className={`${HYPE_EYEBROW} whitespace-nowrap px-6 py-2 text-white`}
                    >
                        {resolved.text}
                        <span className="px-6 opacity-70">&#10022;</span>
                    </span>
                ))}
            </div>
        </div>
    );
};

export default AnnouncementMarquee;
