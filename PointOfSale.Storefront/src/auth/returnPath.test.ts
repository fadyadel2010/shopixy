import { describe, expect, it } from 'vitest';
import { resolveStorefrontReturnPath } from './returnPath';

describe('resolveStorefrontReturnPath', () => {
    it('preserves local paths including query and hash state', () => {
        expect(resolveStorefrontReturnPath('/products?page=2#featured')).toBe('/products?page=2#featured');
    });

    it.each([
        'https://attacker.example/products',
        '//attacker.example/products',
        '/\\attacker.example/products',
        '/products\nSet-Cookie: unsafe=true',
        'products',
    ])('rejects unsafe return target %s', (target) => {
        expect(resolveStorefrontReturnPath(target)).toBe('/');
    });
});
