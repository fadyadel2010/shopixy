import { describe, expect, it } from 'vitest';
import { getHomepageProductIdsIdentity } from './homepageProductIdentity';

describe('getHomepageProductIdsIdentity', () => {
    it('is stable for equivalent arrays created on separate renders', () => {
        const first = [' 11111111-1111-4111-8111-111111111111 ', '22222222-2222-4222-8222-222222222222'];
        const second = ['11111111-1111-4111-8111-111111111111', '22222222-2222-4222-8222-222222222222'];

        expect(getHomepageProductIdsIdentity(first)).toBe(getHomepageProductIdsIdentity(second));
    });

    it('preserves the distinction between an omitted and explicitly empty selection', () => {
        expect(getHomepageProductIdsIdentity()).toBe('null');
        expect(getHomepageProductIdsIdentity([])).toBe('[]');
    });

    it('deduplicates selected product identifiers without changing their order', () => {
        expect(getHomepageProductIdsIdentity(['second', 'first', 'second'])).toBe('["second","first"]');
    });
});
