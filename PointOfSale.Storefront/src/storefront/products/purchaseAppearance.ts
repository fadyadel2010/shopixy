import type { PurchaseExperienceProfile } from './purchaseExperience';

/** Color ownership belongs to Appearance, not the historical purchase skins.
 * Keep each skin's layout/typography/shadows while projecting named color slots.
 * Only these internal profile strings are processed; never merchant content/classes.
 */
const roleClasses = {
    sectionClassName: 'bg-background text-text border-border',
    buyBoxClassName: 'bg-background text-text border-border',
    categoryChipClassName: 'bg-background text-muted border-border',
    titleClassName: 'text-text',
    priceClassName: 'text-primary-text',
    salePriceClassName: 'text-primary-text',
    comparePriceClassName: 'text-muted',
    promoBadgeClassName: 'bg-primary text-on-primary border-primary',
    highlightsPanelClassName: 'bg-surface text-text border-border',
    highlightItemClassName: 'bg-background text-text border-border',
    infoCardClassName: 'bg-surface text-text border-border',
    stockBadgeClassName: 'border-border',
    ctaClassName: 'bg-primary text-on-primary focus:ring-primary-text hover:opacity-90',
    secondaryCtaClassName: 'bg-background text-text border-border hover:bg-surface focus:ring-primary-text',
    trustGridClassName: 'border-border divide-border',
    trustItemClassName: 'bg-surface text-text border-border',
    mobileBarClassName: 'bg-background/95 text-text border-border',
    specsShellClassName: 'bg-background text-text border-border',
    specsFeatureCardClassName: 'bg-surface text-text border-border',
    specsFaqCardClassName: 'bg-background text-text border-border',
    relatedShellClassName: 'bg-background text-text border-border',
    reviewShellClassName: 'bg-background text-text border-border',
    reviewCardClassName: 'bg-surface text-text border-border',
    galleryShellClassName: 'bg-background text-text border-border',
} satisfies Partial<Record<keyof PurchaseExperienceProfile, string>>;

const colorUtility = /^(?:(?:hover|focus|disabled|last):)*(?:text|bg|border|ring|divide)-(?:white|black|primary|secondary|background|text|muted|(?:slate|stone|sky|rose|red)-\d+|\[#[\da-fA-F]+\])(?:\/\d+)?$/;

export function applyPurchaseAppearance(profile: PurchaseExperienceProfile): PurchaseExperienceProfile {
    const result = { ...profile };
    // A thumbnail row has a large min-content width. Explicit zero-minimum
    // tracks prevent it from widening the entire mobile purchase grid.
    result.heroGridClassName += ' grid-cols-1';
    result.mediaWrapClassName += ' min-w-0';
    for (const [key, colors] of Object.entries(roleClasses)) {
        const slot = key as keyof typeof roleClasses;
        result[slot] = `${profile[slot].split(/\s+/).filter(token => !colorUtility.test(token)).join(' ')} ${colors} min-w-0`;
    }
    return result;
}
