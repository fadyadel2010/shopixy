import type React from 'react';
import type { ProductReadDto } from '@/types';

export interface HeaderNavigationLinkPresentationModel {
    key: string;
    index: number;
    label: string;
    href: string;
    openInNewTab: boolean;
    isActive: boolean;
}

export interface HeaderPresentationModel {
    announcement?: import('@/types/cms').HeaderAnnouncementConfig | null;
    storeName: string;
    headerVariant: 'standard' | 'centered' | 'minimal';
    isHome: boolean;
    navigation: {
        links: HeaderNavigationLinkPresentationModel[];
        isLoading: boolean;
        error: string | null;
    };
    labels: {
        account: string;
        announcement: string;
        closeMenu: string;
        mainNavigation: string;
        menu: string;
        next: string;
        previous: string;
        search: string;
        searchProducts: string;
        viewCart: string;
    };
}

export interface NavigationSlotProps {
    className?: string;
    ariaLabel?: string;
    beforeLinks?: React.ReactNode;
    afterLinks?: React.ReactNode;
    linkClassName?: string | ((link: HeaderNavigationLinkPresentationModel) => string);
    onLinkClick?: (link: HeaderNavigationLinkPresentationModel) => void;
    renderLinkContent?: (link: HeaderNavigationLinkPresentationModel) => React.ReactNode;
}

export interface SearchSlotProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
    children?: React.ReactNode;
}

export interface AuthenticationSlotProps {
    variant?: 'desktop' | 'mobile';
    className?: string;
    onLogout?: () => void;
    onLinkClick?: () => void;
}

export interface WishlistSlotProps {
    className?: string;
    showTooltip?: boolean;
}

export interface CartSlotProps {
    className?: string;
    showTooltip?: boolean;
}

export interface LanguageSlotProps {
    variant?: 'icon' | 'text' | 'full';
    className?: string;
    size?: 'sm' | 'md' | 'lg';
}

export interface HeaderRuntimeSlots {
    NavigationSlot: React.ComponentType<NavigationSlotProps>;
    SearchSlot: React.ComponentType<SearchSlotProps>;
    AuthenticationSlot: React.ComponentType<AuthenticationSlotProps>;
    WishlistSlot: React.ComponentType<WishlistSlotProps>;
    CartSlot: React.ComponentType<CartSlotProps>;
    LanguageSlot: React.ComponentType<LanguageSlotProps>;
}

export interface HeaderPresentationProps {
    model: HeaderPresentationModel;
    slots: HeaderRuntimeSlots;
}

export interface FooterLinkPresentationModel {
    key: string;
    label: string;
    href: string;
    openInNewTab: boolean;
}

export interface FooterNavigationPresentationModel {
    links: FooterLinkPresentationModel[];
    isLoading: boolean;
    error: string | null;
}

export interface FooterSectionPresentationModel {
    key: string;
    title: string;
    sortOrder: number;
    links: FooterLinkPresentationModel[];
}

export interface FooterSocialLinkPresentationModel {
    key: string;
    platform: string;
    href: string;
    icon?: string | null;
}

export interface FooterPresentationModel {
    storeName: string;
    currentYear: number;
    copyright: string;
    navigation: FooterNavigationPresentationModel;
    sections: FooterSectionPresentationModel[];
    socialLinks: FooterSocialLinkPresentationModel[];
    isLoading: boolean;
    error: string | null;
    labels: {
        contact: string;
        customerService: string;
        followUs: string;
        loading: string;
        paymentMethods: string;
        quickLinks: string;
    };
}

export interface FooterPresentationProps {
    model: FooterPresentationModel;
}

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
