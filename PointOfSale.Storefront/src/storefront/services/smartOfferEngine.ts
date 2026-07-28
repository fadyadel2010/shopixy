import { fetchSmartOffers } from '@/services/api';
import { tryGetTenantScopedStorageKey } from '@/bootstrap/tenantScopedStorage';
import type {
    SmartOffer,
    SmartOfferPagesScope,
    SmartOfferTriggerType,
    StorefrontPageType,
} from '@/types';

const SESSION_CACHE_KEY = 'smart_offers_cache_v1';
const LOCAL_SEEN_PREFIX = 'offer_seen_';
const SESSION_SEEN_PREFIX = 'offer_seen_session_';

let inMemoryOffers: SmartOffer[] | null = null;
let pendingLoad: Promise<SmartOffer[]> | null = null;

export interface SmartOfferEligibilityInput {
    triggerType?: SmartOfferTriggerType;
    pathname?: string;
    pageType?: StorefrontPageType;
    scrollPercent?: number;
    elapsedSeconds?: number;
    offers?: SmartOffer[];
}

function canUseStorage() {
    return typeof window !== 'undefined' && typeof window.localStorage !== 'undefined';
}

function getScopedStorageKey(key: string): string | null {
    return tryGetTenantScopedStorageKey(key);
}

function safelyReadSessionCache(): SmartOffer[] | null {
    if (!canUseStorage()) {
        return null;
    }

    try {
        const storageKey = getScopedStorageKey(SESSION_CACHE_KEY);
        const cached = storageKey ? window.sessionStorage.getItem(storageKey) : null;
        if (!cached) {
            return null;
        }

        const parsed = JSON.parse(cached);
        return Array.isArray(parsed) ? parsed : null;
    } catch {
        return null;
    }
}

function writeSessionCache(offers: SmartOffer[]) {
    if (!canUseStorage()) {
        return;
    }

    try {
        const storageKey = getScopedStorageKey(SESSION_CACHE_KEY);
        if (storageKey) {
            window.sessionStorage.setItem(storageKey, JSON.stringify(offers));
        }
    } catch {
        // Ignore storage quota errors.
    }
}

function getDisplayCount(offerId: string): number {
    if (!canUseStorage()) {
        return 0;
    }

    const storageKey = getScopedStorageKey(`${LOCAL_SEEN_PREFIX}${offerId}`);
    const rawValue = storageKey ? window.localStorage.getItem(storageKey) : null;
    return rawValue ? Number.parseInt(rawValue, 10) || 0 : 0;
}

function hasSeenOfferThisSession(offerId: string): boolean {
    if (!canUseStorage()) {
        return false;
    }

    const storageKey = getScopedStorageKey(`${SESSION_SEEN_PREFIX}${offerId}`);
    return storageKey ? window.sessionStorage.getItem(storageKey) === '1' : false;
}

function matchesPageScope(scope: SmartOfferPagesScope, pageType: StorefrontPageType): boolean {
    if (scope === 'All') {
        return true;
    }

    return pageType === scope;
}

function isWithinFrequencyLimit(offer: SmartOffer): boolean {
    if (offer.showOncePerSession && hasSeenOfferThisSession(offer.id)) {
        return false;
    }

    if (offer.maxDisplaysPerUser && getDisplayCount(offer.id) >= offer.maxDisplaysPerUser) {
        return false;
    }

    return true;
}

export function normalizeStorefrontPath(pathname: string): string {
    const parts = pathname.split('/').filter(Boolean);

    if (parts[0] === 'ar' || parts[0] === 'en') {
        return `/${parts.slice(1).join('/')}`;
    }

    return pathname || '/';
}

export function detectStorefrontPageType(pathname: string): StorefrontPageType {
    const normalizedPath = normalizeStorefrontPath(pathname);

    if (normalizedPath === '/' || normalizedPath === '') {
        return 'Home';
    }

    if (normalizedPath === '/cart') {
        return 'Cart';
    }

    if (normalizedPath === '/checkout') {
        return 'Checkout';
    }

    if (normalizedPath === '/category' || normalizedPath.startsWith('/category/')) {
        return 'Category';
    }

    if (normalizedPath.startsWith('/products/')) {
        return 'Product';
    }

    return null;
}

export async function loadSmartOffers(forceRefresh = false): Promise<SmartOffer[]> {
    if (!forceRefresh && inMemoryOffers) {
        return inMemoryOffers;
    }

    if (!forceRefresh) {
        const cached = safelyReadSessionCache();
        if (cached) {
            inMemoryOffers = cached;
            return cached;
        }
    }

    if (!forceRefresh && pendingLoad) {
        return pendingLoad;
    }

    pendingLoad = fetchSmartOffers()
        .then((offers) => {
            const sortedOffers = [...offers].sort((left, right) => right.priority - left.priority);
            inMemoryOffers = sortedOffers;
            writeSessionCache(sortedOffers);
            return sortedOffers;
        })
        .finally(() => {
            pendingLoad = null;
        });

    return pendingLoad;
}

export function getLoadedSmartOffers(): SmartOffer[] {
    return inMemoryOffers ?? [];
}

export function getEligibleOffers(input: SmartOfferEligibilityInput = {}): SmartOffer[] {
    const offers = input.offers ?? inMemoryOffers ?? [];
    const pageType = input.pageType ?? detectStorefrontPageType(input.pathname ?? window.location.pathname);

    return offers
        .filter((offer) => {
            if (input.triggerType && offer.triggerType !== input.triggerType) {
                return false;
            }

            if (!matchesPageScope(offer.pagesScope, pageType)) {
                return false;
            }

            if (!isWithinFrequencyLimit(offer)) {
                return false;
            }

            if (offer.triggerType === 'TimeDelay') {
                return (input.elapsedSeconds ?? 0) >= (offer.delaySeconds ?? 0);
            }

            if (offer.triggerType === 'ScrollDepth') {
                return (input.scrollPercent ?? 0) >= (offer.scrollPercent ?? 0);
            }

            return true;
        })
        .sort((left, right) => right.priority - left.priority);
}

export function getNextTimeDelaySeconds(
    offers: SmartOffer[],
    pageType: StorefrontPageType,
    elapsedSeconds: number,
): number | null {
    const remainingDelays = offers
        .filter((offer) => offer.triggerType === 'TimeDelay')
        .filter((offer) => matchesPageScope(offer.pagesScope, pageType))
        .filter(isWithinFrequencyLimit)
        .map((offer) => (offer.delaySeconds ?? 0) - elapsedSeconds)
        .filter((secondsRemaining) => secondsRemaining > 0)
        .sort((left, right) => left - right);

    return remainingDelays[0] ?? null;
}

export function markOfferDisplayed(offer: SmartOffer) {
    if (!canUseStorage()) {
        return;
    }

    const count = getDisplayCount(offer.id) + 1;
    const localSeenKey = getScopedStorageKey(`${LOCAL_SEEN_PREFIX}${offer.id}`);
    const sessionSeenKey = getScopedStorageKey(`${SESSION_SEEN_PREFIX}${offer.id}`);
    if (localSeenKey) {
        window.localStorage.setItem(localSeenKey, String(count));
    }
    if (sessionSeenKey) {
        window.sessionStorage.setItem(sessionSeenKey, '1');
    }
}

export function resetSmartOfferSessionCache() {
    inMemoryOffers = null;

    if (!canUseStorage()) {
        return;
    }

    const storageKey = getScopedStorageKey(SESSION_CACHE_KEY);
    if (storageKey) {
        window.sessionStorage.removeItem(storageKey);
    }
}
