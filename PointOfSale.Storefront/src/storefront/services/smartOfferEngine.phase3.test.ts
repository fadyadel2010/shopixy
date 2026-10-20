import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { SmartOffer } from '@/types';

const mocks = vi.hoisted(() => ({ fetch: vi.fn(), tenant: 'tenant-a' }));
vi.mock('@/services/api', () => ({ fetchSmartOffers: mocks.fetch }));
vi.mock('@/bootstrap/tenantScopedStorage', () => ({ tryGetTenantScopedStorageKey: (key: string) => `${key}:${mocks.tenant}` }));
import { getEligibleOffers, getLoadedSmartOffers, loadSmartOffers, markOfferDisplayed, resetSmartOfferSessionCache } from './smartOfferEngine';

const offer = (id: string): SmartOffer => ({ id, name: id, priority: 1, triggerType: 'ExitIntent', pagesScope: 'All', showOncePerSession: false, popupTitle: id, buttonText: 'Continue' });
const deferred = <T,>() => { let resolve!: (value: T) => void; const promise = new Promise<T>(r => { resolve = r; }); return { promise, resolve }; };
beforeEach(() => { mocks.tenant = 'tenant-a'; vi.clearAllMocks(); resetSmartOfferSessionCache(); });
afterEach(() => vi.unstubAllGlobals());

describe('Smart Offer display candidates remain scoped and current', () => {
    it('never reuses memory across tenant changes', async () => {
        mocks.fetch.mockResolvedValueOnce([offer('a')]).mockResolvedValueOnce([offer('b')]);
        await loadSmartOffers();
        mocks.tenant = 'tenant-b';
        expect(getLoadedSmartOffers()).toEqual([]);
        expect((await loadSmartOffers()).map(x => x.id)).toEqual(['b']);
    });
    it('old tenant completion cannot populate the new tenant cache', async () => {
        const old = deferred<SmartOffer[]>();
        mocks.fetch.mockReturnValueOnce(old.promise).mockResolvedValueOnce([offer('b')]);
        const pending = loadSmartOffers();
        mocks.tenant = 'tenant-b';
        await loadSmartOffers(true);
        old.resolve([offer('a')]);
        expect(await pending).toEqual([]);
        expect(getLoadedSmartOffers().map(x => x.id)).toEqual(['b']);
    });
    it('forced refresh cannot be overwritten by an earlier response', async () => {
        const old = deferred<SmartOffer[]>();
        mocks.fetch.mockReturnValueOnce(old.promise).mockResolvedValueOnce([offer('new')]);
        const pending = loadSmartOffers();
        await loadSmartOffers(true);
        old.resolve([offer('old')]);
        await pending;
        expect(getLoadedSmartOffers().map(x => x.id)).toEqual(['new']);
    });
    it('reset invalidates pending work and duplicates are not presented twice', async () => {
        const old = deferred<SmartOffer[]>();
        mocks.fetch.mockReturnValueOnce(old.promise);
        const pending = loadSmartOffers();
        resetSmartOfferSessionCache();
        old.resolve([offer('old')]);
        expect(await pending).toEqual([]);
        expect(getLoadedSmartOffers()).toEqual([]);
        expect(getEligibleOffers({ offers: [offer('b'), offer('a'), offer('a')], pageType: 'Home' }).map(x => x.id)).toEqual(['a', 'b']);
    });
    it('honors Admin timing, scope and scroll settings without treating unrelated trigger fields as applicable', () => {
        const timed = { ...offer('timed'), triggerType: 'TimeDelay' as const, pagesScope: 'Product' as const, delaySeconds: 12, scrollPercent: 80 };
        expect(getEligibleOffers({ offers: [timed], pathname: '/ar/products/example', elapsedSeconds: 11 })).toEqual([]);
        expect(getEligibleOffers({ offers: [timed], pathname: '/ar/products/example', elapsedSeconds: 12 })).toEqual([timed]);
        expect(getEligibleOffers({ offers: [timed], pageType: 'Cart', elapsedSeconds: 99 })).toEqual([]);
        const scrolled = { ...timed, triggerType: 'ScrollDepth' as const, scrollPercent: 45 };
        expect(getEligibleOffers({ offers: [scrolled], pageType: 'Product', scrollPercent: 44 })).toEqual([]);
        expect(getEligibleOffers({ offers: [scrolled], pageType: 'Product', scrollPercent: 45 })).toEqual([scrolled]);
    });
    it('applies persisted display limits and the session flag using tenant-scoped storage', () => {
        const storage = () => {
            const data = new Map<string, string>();
            return { getItem: (key: string) => data.get(key) ?? null, setItem: (key: string, value: string) => data.set(key, value) };
        };
        vi.stubGlobal('window', { localStorage: storage(), sessionStorage: storage() });
        const limited = { ...offer('frequency-fixture'), maxDisplaysPerUser: 2, showOncePerSession: true };
        expect(getEligibleOffers({ offers: [limited], pageType: 'Home' })).toEqual([limited]);
        markOfferDisplayed(limited);
        expect(getEligibleOffers({ offers: [limited], pageType: 'Home' })).toEqual([]);
        const repeatable = { ...limited, showOncePerSession: false };
        expect(getEligibleOffers({ offers: [repeatable], pageType: 'Home' })).toEqual([repeatable]);
        markOfferDisplayed(repeatable);
        expect(getEligibleOffers({ offers: [repeatable], pageType: 'Home' })).toEqual([]);
        mocks.tenant = 'tenant-b';
        expect(getEligibleOffers({ offers: [repeatable], pageType: 'Home' })).toEqual([repeatable]);
    });
});
