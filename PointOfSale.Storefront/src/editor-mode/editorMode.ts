/**
 * Storefront Editor Mode — ARCH-1.3 (Theme-Native Canvas Integration).
 *
 * Integration-only module: detects editor mode from the URL, and starts the
 * (frozen, ARCH-1.2 certified) ChildRuntimeBridge against the embedding host.
 *
 * Rules honoured:
 * - NO rendering changes. The storefront renders exactly what production
 *   renders; the bridge only observes anchors and applies opaque documents.
 * - Fail closed: editor mode never activates without an explicit, valid
 *   `editorHostOrigin`. No implicit referrer trust, no wildcard origins.
 * - The candidate document received via config.apply is interpreted HERE
 *   (the injected applier) — never inside the bridge (opaque-document rule).
 */

import { ChildRuntimeBridge } from '@/editor-bridge';
import { isTenantResolved, getTenantId } from '@/bootstrap';
import { useStoreConfigStore } from '@/store';
import { applyTheme } from '@/theme';
import type { StoreConfig } from '@/types';

export const EDITOR_MODE_PARAM = 'editorMode';
export const EDITOR_HOST_ORIGIN_PARAM = 'editorHostOrigin';

export interface EditorModeParams {
    hostOrigin: string;
}

/**
 * Parse editor-mode activation from a query string. Pure & fail-closed:
 * returns null unless BOTH editorMode=1 and a well-formed absolute origin
 * are present. A malformed origin never "degrades" to a permissive default.
 */
export function readEditorModeParams(search: string): EditorModeParams | null {
    const params = new URLSearchParams(search);
    if (params.get(EDITOR_MODE_PARAM) !== '1') {
        return null;
    }

    const rawOrigin = params.get(EDITOR_HOST_ORIGIN_PARAM)?.trim();
    if (!rawOrigin) {
        return null;
    }

    try {
        const parsed = new URL(rawOrigin);
        if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
            return null;
        }
        // Normalize to a pure origin (drops any path/query smuggling).
        return { hostOrigin: parsed.origin };
    } catch {
        return null;
    }
}

/**
 * Default opaque-document applier. The host sends a full StoreConfig-shaped
 * candidate document; we commit it to the runtime store and re-apply theme
 * tokens. Throws (=> bridge reports runtime.error, recoverable) when the
 * candidate is not plausibly a StoreConfig — the previous document stays
 * rendered. NO synthetic fallback rendering, ever (P5).
 */
export function applyCandidateStoreConfig(candidate: unknown): void {
    if (!candidate || typeof candidate !== 'object') {
        throw new Error('Editor candidate document rejected: not an object.');
    }

    const config = candidate as StoreConfig;
    if (!config.theme || typeof config.theme !== 'object') {
        throw new Error('Editor candidate document rejected: missing theme.');
    }

    useStoreConfigStore.getState().setConfig(config);
    applyTheme(config.theme);
}

export interface CreateEditorModeBridgeOptions {
    hostOrigin: string;
    tenantId?: string;
    /** Injectable for tests (storefront vitest runs in node env). */
    windowRef?: Window;
    documentRef?: Document;
    /** Injectable applier for tests; defaults to applyCandidateStoreConfig. */
    applyDocument?: (document: unknown, revision: number) => Promise<void> | void;
    onSelectionSet?: (anchorId: string | null) => void;
}

/** Single constructor-call integration point mandated by ARCH-1.2 (M1). */
export function createEditorModeBridge(options: CreateEditorModeBridgeOptions): ChildRuntimeBridge {
    return new ChildRuntimeBridge({
        expectedParentOrigin: options.hostOrigin,
        tenantId: options.tenantId,
        applyDocument: options.applyDocument ?? ((document) => applyCandidateStoreConfig(document)),
        onSelectionSet: options.onSelectionSet,
        windowRef: options.windowRef,
        documentRef: options.documentRef,
    });
}

let activeBridge: ChildRuntimeBridge | null = null;

/** True when this runtime is embedded in the builder host. */
export function isEditorModeActive(): boolean {
    return activeBridge !== null;
}

/** Test-only reset hook. */
export function __resetEditorModeForTests(): void {
    activeBridge?.stop();
    activeBridge = null;
}

/**
 * Idempotent editor-mode bootstrap. Called once after the normal storefront
 * bootstrap succeeds (App.tsx). When the URL does not request editor mode
 * this is a no-op — the production path is untouched.
 */
export function startEditorModeIfRequested(): ChildRuntimeBridge | null {
    if (typeof window === 'undefined') {
        return null;
    }
    if (activeBridge) {
        return activeBridge;
    }

    const params = readEditorModeParams(window.location.search);
    if (!params) {
        return null;
    }

    const bridge = createEditorModeBridge({
        hostOrigin: params.hostOrigin,
        tenantId: isTenantResolved() ? getTenantId() : undefined,
    });
    bridge.start();
    activeBridge = bridge;

    if (import.meta.env.DEV) {
        console.log('[EditorMode] ChildRuntimeBridge started for host origin:', params.hostOrigin);
    }

    return bridge;
}
