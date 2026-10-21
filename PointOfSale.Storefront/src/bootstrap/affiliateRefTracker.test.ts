import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const scope = vi.hoisted(() => ({ tenant: 'shopixy' }));
vi.mock('./tenantScopedStorage', () => ({ tryGetTenantScopedStorageKey: (key: string) => `${key}:${scope.tenant}` }));
import { acceptAffiliateRefVerdict, clearAffiliateRef, getAffiliateRefCode, getConfirmedAffiliateRefCode,
    initAffiliateRefTracker, isAffiliateRefActive, subscribeAffiliateRef } from './affiliateRefTracker';

const storage = new Map<string, string>();
function landing(search = '', pathname = '/products/phone') {
    vi.stubGlobal('window', { location: { search, pathname } });
    initAffiliateRefTracker();
}

describe('product-ref candidate and server-admitted first touch', () => {
    beforeEach(() => {
        scope.tenant = 'shopixy'; storage.clear();
        vi.stubGlobal('sessionStorage', { getItem: (k: string) => storage.get(k) ?? null,
            setItem: (k: string, v: string) => storage.set(k, v), removeItem: (k: string) => storage.delete(k) });
        clearAffiliateRef();
    });
    afterEach(() => vi.unstubAllGlobals());

    it('normalizes a product-link ref without claiming that the candidate is active', () => {
        landing('?ref=%20abc123%20');
        expect(getAffiliateRefCode()).toBe('ABC123');
        expect(isAffiliateRefActive()).toBe(false);
        acceptAffiliateRefVerdict('true', 'ABC123', null);
        expect(isAffiliateRefActive()).toBe(true);
    });

    it.each(['?ref=%3Cscript%3E', '?ref=A&ref=B', '?ref=' + 'A'.repeat(51), '?utm_source=ABC123'])('rejects unsafe/ambiguous or unrelated values: %s', search => {
        landing(search);
        expect(getAffiliateRefCode()).toBeNull();
        expect(getConfirmedAffiliateRefCode()).toBeNull();
    });

    it('uses the server-selected first touch rather than a later valid/invalid URL hint', () => {
        landing('?ref=SECOND');
        acceptAffiliateRefVerdict('true', 'FIRST', null);
        expect(getAffiliateRefCode()).toBe('FIRST');
        expect(storage.get('affiliate_ref_code:shopixy')).toBe('FIRST');
        // Late bootstrap requests started before first-touch admission.
        acceptAffiliateRefVerdict('false', undefined, null);
        expect(getConfirmedAffiliateRefCode()).toBe('FIRST');
    });

    it('restores the navigation candidate on refresh but requires server readmission', () => {
        landing('?ref=FIRST'); acceptAffiliateRefVerdict('true', 'FIRST', null);
        landing('', '/checkout');
        expect(getAffiliateRefCode()).toBe('FIRST');
        expect(isAffiliateRefActive()).toBe(false);
        acceptAffiliateRefVerdict('true', 'FIRST', null);
        expect(isAffiliateRefActive()).toBe(true);
    });

    it('isolates merchant navigation hints', () => {
        landing('?ref=SHOP'); acceptAffiliateRefVerdict('true', 'SHOP', null);
        scope.tenant = 'greatone'; landing();
        expect(getAffiliateRefCode()).toBeNull();
        expect(isAffiliateRefActive()).toBe(false);
        landing('?ref=GREAT'); acceptAffiliateRefVerdict('true', 'GREAT', null);
        scope.tenant = 'shopixy'; landing();
        expect(getAffiliateRefCode()).toBe('SHOP');
    });

    it('does not treat failed/missing responses as successful capture', () => {
        landing('?ref=INVALID');
        acceptAffiliateRefVerdict(undefined, undefined, null);
        acceptAffiliateRefVerdict('false', undefined, null);
        expect(isAffiliateRefActive()).toBe(false);
    });

    it('notifies presentation on stale admission and on cleared state', () => {
        landing('?ref=FIRST'); const listener = vi.fn(); const unsubscribe = subscribeAffiliateRef(listener);
        acceptAffiliateRefVerdict('true', 'FIRST', null);
        acceptAffiliateRefVerdict('false', undefined, 'FIRST');
        expect(isAffiliateRefActive()).toBe(false);
        expect(listener).toHaveBeenCalledTimes(2);
        unsubscribe(); clearAffiliateRef();
        expect(listener).toHaveBeenCalledTimes(2);
    });

    it('supports legacy landing candidates and unavailable optional storage', () => {
        vi.stubGlobal('sessionStorage', { getItem: () => { throw new Error('storage unavailable'); },
            setItem: () => { throw new Error('storage unavailable'); } });
        landing('', '/p/legacy123/product');
        expect(getAffiliateRefCode()).toBe('LEGACY123');
        acceptAffiliateRefVerdict('true', 'LEGACY123', null);
        expect(isAffiliateRefActive()).toBe(true);
    });
});
