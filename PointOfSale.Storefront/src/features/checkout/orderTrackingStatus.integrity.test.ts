import { readFileSync } from 'node:fs';
import ts from 'typescript';
import { describe, expect, it } from 'vitest';

// Exercise the real hook's private, pure adapter (analogous to the backend's
// private-writer reflection regressions) without mocking authentication/network.
const source = readFileSync(new URL('../../hooks/useOrder.ts', import.meta.url), 'utf8');
const start = source.indexOf('function normalizeStatus(');
const end = source.indexOf('function parseError(', start);
if (start < 0 || end < 0) throw new Error('Production tracking adapter not found');
const javascript = ts.transpileModule(source.slice(start, end), {
    compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.None },
}).outputText;
const normalize = new Function(`${javascript}\nreturn normalizeStatus;`)() as (status: string, shipping?: string) => string;

describe('authorized order/shipment tracking presentation', () => {
    it.each([
        ['Completed', 'Delivered', 'delivered'],
        ['Delivered', 'Returned', 'returned'],
        ['Shipped', 'Failed', 'failed'],
        ['Shipped', 'OutForDelivery', 'out_for_delivery'],
        ['Cancelled', 'Pending', 'cancelled'],
        ['Confirmed', 'Pending', 'confirmed'],
        ['Delivered', 'Delivered', 'delivered'],
    ])('%s / %s remains %s, never inferred from payment', (status, shipping, expected) => {
        expect(normalize(status, shipping)).toBe(expected);
    });
});
