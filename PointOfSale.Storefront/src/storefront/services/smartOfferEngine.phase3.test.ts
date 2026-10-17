import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { SmartOffer } from '@/types';

const mocks = vi.hoisted(() => ({ fetch: vi.fn(), tenant: 'tenant-a' }));
vi.mock('@/services/api', () => ({ fetchSmartOffers: mocks.fetch }));
vi.mock('@/bootstrap/tenantScopedStorage', () => ({ tryGetTenantScopedStorageKey: (key: string) => `${key}:${mocks.tenant}` }));
import { getEligibleOffers, getLoadedSmartOffers, loadSmartOffers, resetSmartOfferSessionCache } from './smartOfferEngine';

const offer = (id: string): SmartOffer => ({ id, name: id, priority: 1, triggerType: 'ExitIntent', pagesScope: 'All', showOncePerSession: false, popupTitle: id, buttonText: 'Continue' });
const deferred = <T,>() => { let resolve!: (value: T) => void; const promise = new Promise<T>(r => { resolve = r; }); return { promise, resolve }; };
beforeEach(() => { mocks.tenant = 'tenant-a'; vi.clearAllMocks(); resetSmartOfferSessionCache(); });

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
});
