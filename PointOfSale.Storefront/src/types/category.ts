/**
 * Category Types
 * Matches backend StorefrontDtos.cs
 */

export interface CategoryReadDto {
    id: string;
    name: string;
    nameAr?: string;
    slug: string;
    parentId?: string;

    // PHASE: Promotions Engine - Category Banner Fields
    /** Whether this category has an active promotion */
    hasActivePromotion?: boolean;
    /** Banner text to display at top of category page */
    promotionBannerText?: string;
    /** End date of promotion for countdown timer (ISO string) */
    promotionEndDate?: string;
}

export interface CategoryTreeDto {
    id: string;
    name: string;
    nameAr?: string;
    slug: string;
    children: CategoryTreeDto[];

    // PHASE: Promotions Engine - Category Banner Fields
    /** Whether this category has an active promotion */
    hasActivePromotion?: boolean;
    /** Banner text to display at top of category page */
    promotionBannerText?: string;
    /** End date of promotion for countdown timer (ISO string) */
    promotionEndDate?: string;
    /** Banner display style: gradient, solid, minimal, festive */
    promotionBannerStyle?: 'gradient' | 'solid' | 'minimal' | 'festive';
}
