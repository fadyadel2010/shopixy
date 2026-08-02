import type React from 'react';
import type { ProductReadDto } from '@/types';

export interface ProductPresentationModel {
    id: string;
}

export interface CategoryPresentationModel {
    id: string;
    title: string;
    href: string;
}

export interface ProductGridPresentationModel {
    title: string;
    subtitle?: string;
    layout: string;
    viewAllHref?: string | null;
    pageSize: number;
    products: ProductPresentationModel[];
    isLoading: boolean;
    error: string | null;
    isCuratedConfig: boolean;
    isCuratedSelectionInvalid: boolean;
    emptyStateTitle: string;
    emptyStateMessage?: string;
    labels: {
        eyebrowProducts: string;
        viewAll: string;
        failedToLoad: string;
    };
}

export interface CategoryGridPresentationModel {
    title: string;
    subtitle?: string;
    visibleCount: number;
    categories: CategoryPresentationModel[];
    isLoading: boolean;
    error: string | null;
    hasCuratedSelection: boolean;
    emptyStateTitle: string;
    emptyStateMessage?: string;
    labels: {
        eyebrowCategories: string;
        failedToLoadCategories: string;
    };
}

export type RenderProductCard = (product: ProductPresentationModel, index: number) => React.ReactNode;

export interface ProductHeroPresentationModel {
    product: ProductReadDto;
    profile: Record<string, any>;
    showDescription: boolean;
    showMediaGallery: boolean;
    showVariantSelector: boolean;
    showAddToCart: boolean;
    showStockStatus: boolean;
    localizedName: string;
    summaryParagraphs: string[];
    featureHighlights: string[];
    attributeGroups: any[];
    selectedAttributes: Record<string, string>;
    hasVariants: boolean;
    hasVariantResolutionError: boolean;
    preferredMediaId?: string | null;
    activeImage?: string | null;
    pricing: {
        currentPrice: number;
        current: string;
        compareAt?: string;
    };
    hasActivePromotion: boolean;
    promotionBadge?: string | null;
    trustItems: Array<{ label: string; value: string }>;
    stockLabel: string;
    stockToneClass: string;
    canAddToCart: boolean;
    isAddingToCart: boolean;
    addToCartLabel: string;
    labels: {
        invalidVariantCombination: string;
        products: string;
    };
    callbacks: {
        onAddToCart: () => Promise<void>;
        onSelectAttribute: (attributeName: string, value: string) => void;
        isValueAvailable: (attributeName: string, value: string) => boolean;
    };
}
