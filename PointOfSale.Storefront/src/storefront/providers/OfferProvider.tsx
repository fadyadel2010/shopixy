import React, { Suspense, lazy, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { selectIsRTL, useLanguageStore } from '@/i18n';
import { selectCartItemCount, selectIsCartInitialized, useCartStore } from '@/store';
import type { SmartOffer, SmartOfferTriggerType } from '@/types';
import { useExitIntent } from '../hooks/useExitIntent';
import { useOfferTimer } from '../hooks/useOfferTimer';
import { useScrollTrigger } from '../hooks/useScrollTrigger';
import {
    detectStorefrontPageType,
    getEligibleOffers,
    getNextTimeDelaySeconds,
    loadSmartOffers,
    markOfferDisplayed,
} from '../services/smartOfferEngine';

const OfferPopup = lazy(() => import('../components/offers/OfferPopup'));
const NAVIGATION_EVENT = 'smart-offers:navigation';

interface OfferProviderProps {
    children: React.ReactNode;
}

function installNavigationListeners(onNavigate: () => void) {
    const pushState = window.history.pushState;
    const replaceState = window.history.replaceState;

    const notify = () => window.dispatchEvent(new Event(NAVIGATION_EVENT));

    window.history.pushState = function patchedPushState(...args) {
        pushState.apply(window.history, args);
        notify();
    };

    window.history.replaceState = function patchedReplaceState(...args) {
        replaceState.apply(window.history, args);
        notify();
    };

    window.addEventListener('popstate', onNavigate);
    window.addEventListener(NAVIGATION_EVENT, onNavigate);

    return () => {
        window.history.pushState = pushState;
        window.history.replaceState = replaceState;
        window.removeEventListener('popstate', onNavigate);
        window.removeEventListener(NAVIGATION_EVENT, onNavigate);
    };
}

export const OfferProvider: React.FC<OfferProviderProps> = ({ children }) => {
    const isRTL = useLanguageStore(selectIsRTL);
    const cartItemCount = useCartStore(selectCartItemCount);
    const isCartInitialized = useCartStore(selectIsCartInitialized);

    const [offers, setOffers] = useState<SmartOffer[]>([]);
    const [isLoaded, setIsLoaded] = useState(false);
    const [activeOffer, setActiveOffer] = useState<SmartOffer | null>(null);
    const [pathname, setPathname] = useState(() => window.location.pathname);
    const [pageEnteredAt, setPageEnteredAt] = useState(() => Date.now());

    const previousCartCountRef = useRef(0);
    const hasObservedCartRef = useRef(false);

    useEffect(() => {
        let isMounted = true;

        const refresh = () => loadSmartOffers()
            .then((loadedOffers) => {
                if (!isMounted) {
                    return;
                }

                setOffers(loadedOffers);
                setIsLoaded(true);
            })
            .catch((error) => {
                console.error('[SmartOffers] Failed to load offers:', error);
                if (isMounted) {
                    setOffers([]);
                    setIsLoaded(true);
                }
            });
        void refresh();
        const interval = window.setInterval(refresh, 60_000);
        window.addEventListener('focus', refresh);

        return () => {
            isMounted = false;
            window.clearInterval(interval);
            window.removeEventListener('focus', refresh);
        };
    }, [pathname]);

    useEffect(() => {
        setActiveOffer(current => current ? offers.find(offer => offer.id === current.id) ?? null : null);
    }, [offers]);

    useEffect(() => {
        const handleNavigate = () => {
            setPathname(window.location.pathname);
        };

        return installNavigationListeners(handleNavigate);
    }, []);

    useEffect(() => {
        setActiveOffer(null);
        setPageEnteredAt(Date.now());
    }, [pathname]);

    const pageType = useMemo(() => detectStorefrontPageType(pathname), [pathname]);

    const processTrigger = useCallback(
        (triggerType: SmartOfferTriggerType, extra: { scrollPercent?: number; elapsedSeconds?: number } = {}) => {
            if (!isLoaded || activeOffer) {
                return false;
            }

            const offer = getEligibleOffers({
                offers,
                triggerType,
                pathname,
                pageType,
                scrollPercent: extra.scrollPercent,
                elapsedSeconds: extra.elapsedSeconds,
            })[0];

            if (!offer) {
                return false;
            }

            markOfferDisplayed(offer);
            setActiveOffer(offer);
            return true;
        },
        [activeOffer, isLoaded, offers, pageType, pathname],
    );

    const nextTimeDelaySeconds = useMemo(() => {
        if (!isLoaded || activeOffer) {
            return null;
        }

        const elapsedSeconds = Math.floor((Date.now() - pageEnteredAt) / 1000);
        const immediatelyEligible = getEligibleOffers({
            offers,
            triggerType: 'TimeDelay',
            pathname,
            pageType,
            elapsedSeconds,
        });

        if (immediatelyEligible.length > 0) {
            return 0;
        }

        return getNextTimeDelaySeconds(offers, pageType, elapsedSeconds);
    }, [activeOffer, isLoaded, offers, pageEnteredAt, pageType, pathname]);

    useExitIntent({
        enabled: isLoaded && !activeOffer,
        onTrigger: () => {
            processTrigger('ExitIntent');
        },
    });

    useScrollTrigger({
        enabled: isLoaded && !activeOffer,
        onTrigger: (scrollPercent) => {
            processTrigger('ScrollDepth', { scrollPercent });
        },
    });

    useOfferTimer({
        enabled: isLoaded && !activeOffer,
        delaySeconds: nextTimeDelaySeconds,
        onTrigger: () => {
            processTrigger('TimeDelay', {
                elapsedSeconds: Math.floor((Date.now() - pageEnteredAt) / 1000),
            });
        },
    });

    useEffect(() => {
        if (pageType !== 'Checkout' || activeOffer || !isLoaded) {
            return;
        }

        processTrigger('BeforeCheckout');
    }, [activeOffer, isLoaded, pageType, processTrigger]);

    useEffect(() => {
        if (!isCartInitialized) {
            return;
        }

        if (!hasObservedCartRef.current) {
            hasObservedCartRef.current = true;
            previousCartCountRef.current = cartItemCount;
            return;
        }

        if (cartItemCount > previousCartCountRef.current) {
            processTrigger('AddToCart');
        }

        previousCartCountRef.current = cartItemCount;
    }, [cartItemCount, isCartInitialized, processTrigger]);

    return (
        <>
            {children}
            <Suspense fallback={null}>
                {activeOffer ? (
                    <OfferPopup
                        offer={activeOffer}
                        isRTL={isRTL}
                        onClose={() => setActiveOffer(null)}
                    />
                ) : null}
            </Suspense>
        </>
    );
};

export default OfferProvider;
