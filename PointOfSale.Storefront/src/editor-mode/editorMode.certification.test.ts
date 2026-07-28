/**
 * ARCH-1.3 certification — Storefront editor-mode bootstrap.
 *
 * Runs in node env (no jsdom): uses pure functions + injected fake
 * window/document, matching the ARCH-1.2 test conventions.
 */

import { describe, expect, it, vi } from 'vitest';

// Node-env suite: stub browser-coupled modules that editorMode.ts pulls in
// transitively (@/store → analytics touches `document` at import time).
vi.mock('@/store', () => ({
    useStoreConfigStore: { getState: () => ({ setConfig: () => undefined }) },
}));
vi.mock('@/theme', () => ({ applyTheme: () => undefined }));
vi.mock('@/bootstrap', () => ({
    isTenantResolved: () => false,
    getTenantId: () => 'tenant-test',
}));

import { ChildRuntimeBridge } from '@/editor-bridge';
import {
    applyCandidateStoreConfig,
    createEditorModeBridge,
    readEditorModeParams,
} from './editorMode';

describe('readEditorModeParams (fail-closed activation)', () => {
    it('returns null when editorMode is absent', () => {
        expect(readEditorModeParams('')).toBeNull();
        expect(readEditorModeParams('?tenant=abc')).toBeNull();
    });

    it('returns null when editorMode=1 but host origin is missing', () => {
        expect(readEditorModeParams('?editorMode=1')).toBeNull();
        expect(readEditorModeParams('?editorMode=1&editorHostOrigin=')).toBeNull();
        expect(readEditorModeParams('?editorMode=1&editorHostOrigin=%20')).toBeNull();
    });

    it('returns null for malformed or non-http(s) origins (never degrades)', () => {
        expect(readEditorModeParams('?editorMode=1&editorHostOrigin=not-a-url')).toBeNull();
        expect(readEditorModeParams('?editorMode=1&editorHostOrigin=javascript:alert(1)')).toBeNull();
        expect(readEditorModeParams('?editorMode=1&editorHostOrigin=file:///etc')).toBeNull();
    });

    it('returns null when editorMode is not exactly "1"', () => {
        expect(readEditorModeParams('?editorMode=true&editorHostOrigin=https://pos.example.com')).toBeNull();
        expect(readEditorModeParams('?editorMode=yes&editorHostOrigin=https://pos.example.com')).toBeNull();
    });

    it('accepts a valid activation and normalizes to a pure origin', () => {
        const params = readEditorModeParams('?editorMode=1&editorHostOrigin=https://pos.example.com/some/path?x=1');
        expect(params).toEqual({ hostOrigin: 'https://pos.example.com' });
    });

    it('accepts http origins for local development', () => {
        const params = readEditorModeParams('?editorMode=1&editorHostOrigin=http://localhost:5173');
        expect(params).toEqual({ hostOrigin: 'http://localhost:5173' });
    });
});

describe('applyCandidateStoreConfig (opaque document applier)', () => {
    it('rejects non-object candidates (fail closed, previous doc stays)', () => {
        expect(() => applyCandidateStoreConfig(null)).toThrow(/not an object/);
        expect(() => applyCandidateStoreConfig('nope')).toThrow(/not an object/);
        expect(() => applyCandidateStoreConfig(42)).toThrow(/not an object/);
    });

    it('rejects candidates without a theme object', () => {
        expect(() => applyCandidateStoreConfig({})).toThrow(/missing theme/);
        expect(() => applyCandidateStoreConfig({ theme: 'dark' })).toThrow(/missing theme/);
    });
});

describe('createEditorModeBridge (single-constructor integration point)', () => {
    it('instantiates the frozen ChildRuntimeBridge with the approved API only', () => {
        const fakeWindow = {
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        } as unknown as Window;
        const fakeDocument = {} as Document;

        const bridge = createEditorModeBridge({
            hostOrigin: 'https://pos.example.com',
            tenantId: 'tenant-1',
            windowRef: fakeWindow,
            documentRef: fakeDocument,
            applyDocument: () => undefined,
        });

        expect(bridge).toBeInstanceOf(ChildRuntimeBridge);
        expect(bridge.isConnected()).toBe(false);
    });

    it('start/stop lifecycle is idempotent and leak-free', () => {
        const listeners: string[] = [];
        const fakeWindow = {
            addEventListener: (type: string) => listeners.push(type),
            removeEventListener: (type: string) => {
                const index = listeners.indexOf(type);
                if (index >= 0) listeners.splice(index, 1);
            },
        } as unknown as Window;

        const bridge = createEditorModeBridge({
            hostOrigin: 'https://pos.example.com',
            windowRef: fakeWindow,
            documentRef: {} as Document,
            applyDocument: () => undefined,
        });

        bridge.start();
        bridge.start(); // idempotent
        expect(listeners).toContain('message');
        bridge.stop();
        expect(listeners).toHaveLength(0);
    });
});
