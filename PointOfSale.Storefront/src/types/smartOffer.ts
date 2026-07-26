export type SmartOfferTriggerType =
    | 'ExitIntent'
    | 'TimeDelay'
    | 'ScrollDepth'
    | 'AddToCart'
    | 'BeforeCheckout';

export type SmartOfferPagesScope =
    | 'All'
    | 'Home'
    | 'Product'
    | 'Category'
    | 'Cart'
    | 'Checkout';

export type StorefrontPageType =
    | 'Home'
    | 'Product'
    | 'Category'
    | 'Cart'
    | 'Checkout'
    | null;

export interface SmartOffer {
    id: string;
    name: string;
    priority: number;
    triggerType: SmartOfferTriggerType;
    pagesScope: SmartOfferPagesScope;
    delaySeconds?: number | null;
    scrollPercent?: number | null;
    showOncePerSession: boolean;
    maxDisplaysPerUser?: number | null;
    popupTitle: string;
    popupDescription?: string | null;
    buttonText: string;
    imageUrl?: string | null;
    backgroundColor?: string | null;
}
