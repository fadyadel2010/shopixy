/**
 * ARCH-1.2 Certification — Protocol duplication rule.
 * protocol.ts must be byte-identical in both apps (the protocol IS the
 * boundary; no cross-app runtime imports are permitted).
 */
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

describe('Bridge protocol parity', () => {
    it('PointOfSale.Web and PointOfSale.Storefront share a byte-identical protocol.ts', () => {
        const webProtocol = readFileSync(resolve(__dirname, './protocol.ts'), 'utf8');
        const storefrontProtocol = readFileSync(
            resolve(__dirname, '../../../PointOfSale.Storefront/src/editor-bridge/protocol.ts'),
            'utf8'
        );
        expect(storefrontProtocol).toBe(webProtocol);
    });

    it('diagnostics.ts is likewise byte-identical in both apps', () => {
        const web = readFileSync(resolve(__dirname, './diagnostics.ts'), 'utf8');
        const storefront = readFileSync(
            resolve(__dirname, '../../../PointOfSale.Storefront/src/editor-bridge/diagnostics.ts'),
            'utf8'
        );
        expect(storefront).toBe(web);
    });
});
