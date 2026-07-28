/**
 * Feature flag: builder.themeNativeCanvas — ARCH-1.3.
 *
 * Controls whether the Builder canvas renders the REAL storefront runtime
 * (iframe + EditorBridge + OverlayRuntime) or the legacy synthetic canvas.
 *
 * Resolution order (first defined wins):
 *   1. localStorage 'builder.themeNativeCanvas' = 'on' | 'off'  (instant rollback)
 *   2. VITE_BUILDER_THEME_NATIVE_CANVAS = 'true' | 'false'      (deploy-time)
 *   3. default: OFF (legacy canvas — migration safety)
 *
 * Rollback is a single flag change; no code path is removed while this
 * flag exists (ARCH-1.3 migration safety requirement).
 */

export const THEME_NATIVE_CANVAS_FLAG = 'builder.themeNativeCanvas'

export function isThemeNativeCanvasEnabled() {
    try {
        const local = window.localStorage?.getItem(THEME_NATIVE_CANVAS_FLAG)
        if (local === 'on') return true
        if (local === 'off') return false
    } catch {
        // storage unavailable (private mode) — fall through to env default
    }

    const env = import.meta.env.VITE_BUILDER_THEME_NATIVE_CANVAS
    if (env === 'true') return true
    if (env === 'false') return false

    return false
}

/** Programmatic override (used by rollout tooling / QA). */
export function setThemeNativeCanvasFlag(enabled) {
    try {
        if (enabled === null || enabled === undefined) {
            window.localStorage?.removeItem(THEME_NATIVE_CANVAS_FLAG)
        } else {
            window.localStorage?.setItem(THEME_NATIVE_CANVAS_FLAG, enabled ? 'on' : 'off')
        }
    } catch {
        // ignore — env default applies
    }
}
