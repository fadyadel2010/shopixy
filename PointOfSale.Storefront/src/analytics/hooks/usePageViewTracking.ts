import { useEffect, useRef } from 'react';
import { useLocation } from 'react-router-dom';
import { useAnalytics } from './useAnalytics';

// Exactly one owner for navigation events, independent of how many sections use analytics.
export function usePageViewTracking(): void {
    const location = useLocation();
    const { pageView } = useAnalytics();
    const lastNavigation = useRef('');
    useEffect(() => {
        const navigation = `${location.key}:${location.pathname}${location.search}`;
        if (lastNavigation.current === navigation) return;
        lastNavigation.current = navigation;
        pageView();
    }, [location.key, location.pathname, location.search, pageView]);
}
