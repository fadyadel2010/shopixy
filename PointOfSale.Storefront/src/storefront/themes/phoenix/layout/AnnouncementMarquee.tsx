/**
 * Phoenix Sports Theme — Announcement Marquee (Phase T1.1)
 *
 * A reusable, dark announcement strip for the Phoenix theme.
 *
 * Design (from report): dark (#212121) strip, white uppercase text, sits above
 * the floating header. This component is:
 *   - configurable via `text` / `textAr` props (no hard-coded copy),
 *   - enable/disable aware via the `enabled` prop,
 *   - RTL aware (reads direction from useTranslation),
 *   - localized (falls back to the `announcement.defaultText` i18n key).
 *
 * Reuse note: there is NO existing marquee/announcement primitive in the
 * platform, so this is a new, self-contained, theme-scoped component. It does
 * not duplicate any business logic — it only renders a presentational strip.
 */

import React from 'react';
import { useTranslation } from '@/i18n';
import { PHOENIX_ACCENT_BG, PHOENIX_ACCENT_TEXT, PHOENIX_EYEBROW, PHOENIX_FONT_BODY } from './phoenixTokens';

export interface AnnouncementMarqueeProps {
    /** Enable/disable the strip. Defaults to true. */
    enabled?: boolean;
    /** Explicit text (LTR). When omitted, falls back to the i18n default. */
    text?: string;
    /** Explicit Arabic text. When omitted, `text` or i18n default is used. */
    textAr?: string;
    /** Optional accent background override class (defaults to Phoenix dark). */
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
    accentClass = PHOENIX_ACCENT_BG,
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
            className={`${accentClass} ${PHOENIX_ACCENT_TEXT} overflow-hidden`}
        >
            <div
                className={`flex w-max ${PHOENIX_FONT_BODY} animate-[marquee_22s_linear_infinite] items-center`}
                aria-hidden={false}
            >
                {items.map((_, index) => (
                    <span
                        key={index}
                        className={`${PHOENIX_EYEBROW} ${PHOENIX_ACCENT_TEXT} whitespace-nowrap px-6 py-2`}
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
