/**
 * ARCH-1.3 certification — Theme-Native Canvas integration (host side).
 *
 * Two layers:
 *  1. Behavioural: flag resolution + candidate document composition +
 *     editor frame URL (jsdom).
 *  2. Architectural (source certification, same style as
 *     builderWorkspaceAuthority.test.ts): the canvas swap is flag-gated,
 *     the new canvas talks ONLY to the sanctioned editor-bridge barrel,
 *     and no synthetic fallback exists in the theme-native path.
 */

import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { buildCandidateDocument, buildEditorFrameUrl, computeReorderIndices, resolveSectionIndexForDrop } from './ThemeNativeCanvas'
import { isThemeNativeCanvasEnabled, setThemeNativeCanvasFlag, THEME_NATIVE_CANVAS_FLAG } from './themeNativeCanvasFlag'

const currentDir = dirname(fileURLToPath(import.meta.url))
const themeNativeCanvasSource = readFileSync(join(currentDir, 'ThemeNativeCanvas.jsx'), 'utf8')
const builderCanvasSource = readFileSync(join(currentDir, 'BuilderCanvas.jsx'), 'utf8')

afterEach(() => {
    window.localStorage.clear()
    vi.unstubAllEnvs()
})

describe('builder.themeNativeCanvas feature flag', () => {
    it('defaults OFF (legacy canvas remains the default until cutover)', () => {
        expect(isThemeNativeCanvasEnabled()).toBe(false)
    })

    it('localStorage override wins in both directions (instant rollback)', () => {
        setThemeNativeCanvasFlag(true)
        expect(isThemeNativeCanvasEnabled()).toBe(true)

        setThemeNativeCanvasFlag(false)
        expect(isThemeNativeCanvasEnabled()).toBe(false)

        setThemeNativeCanvasFlag(null)
        expect(window.localStorage.getItem(THEME_NATIVE_CANVAS_FLAG)).toBeNull()
    })

    it('honours the deploy-time env default when no local override exists', () => {
        vi.stubEnv('VITE_BUILDER_THEME_NATIVE_CANVAS', 'true')
        expect(isThemeNativeCanvasEnabled()).toBe(true)

        // local override still beats env (rollback without redeploy)
        setThemeNativeCanvasFlag(false)
        expect(isThemeNativeCanvasEnabled()).toBe(false)
    })
})

describe('editor frame URL', () => {
    it('activates child editor mode with explicit host origin and tenant', () => {
        const url = new URL(buildEditorFrameUrl('tenant-9'))
        expect(url.searchParams.get('editorMode')).toBe('1')
        expect(url.searchParams.get('editorHostOrigin')).toBe(window.location.origin)
        expect(url.searchParams.get('tenant')).toBe('tenant-9')
    })

    it('omits tenant param when unknown (child resolves via its own bootstrap)', () => {
        const url = new URL(buildEditorFrameUrl(''))
        expect(url.searchParams.get('tenant')).toBeNull()
    })
})

describe('candidate document composition (config.apply payload)', () => {
    const storeConfig = {
        storeName: 'Demo',
        pages: {
            home: { sections: [{ id: 'a', type: 'hero', order: 0, enabled: true, settings: {} }] },
            category: { sections: [] },
        },
    }

    it('substitutes ONLY the edited page draft sections', () => {
        const draft = [{ id: 'b', type: 'banner', order: 0, enabled: true, settings: {} }]
        const document = buildCandidateDocument(storeConfig, 'home', draft)

        expect(document.pages.home.sections).toEqual(draft)
        expect(document.pages.category.sections).toEqual([])
        expect(document.storeName).toBe('Demo')
    })

    it('keeps the frozen section contract fields untouched', () => {
        const draft = [{ id: 's1', type: 'hero', order: 3, enabled: false, settings: { x: 1 } }]
        const document = buildCandidateDocument(storeConfig, 'home', draft)
        expect(document.pages.home.sections[0]).toEqual(draft[0])
    })

    it('normalizes missing theme sub-objects so the child applier never rejects an admitted config', () => {
        const document = buildCandidateDocument({ pages: {} }, 'home', [])
        expect(document.theme).toBeTruthy()
        expect(document.theme.colors).toBeTruthy()
    })
})

describe('ARCH-1.4R G2/G3 reorder helpers (pure, Builder Store index math)', () => {
    const S = (id, enabled = true) => ({ id, type: `t-${id}`, order: 0, enabled, settings: {} })

    it('maps drop index among visible anchors to the full draft insertion index (hidden sections skipped by runtime)', () => {
        const sections = [S('a'), S('h1', false), S('b'), S('c')]
        const anchorOrder = ['a', 'b', 'c'] // hidden h1 has no anchor
        expect(resolveSectionIndexForDrop(sections, anchorOrder, 0)).toBe(0)
        expect(resolveSectionIndexForDrop(sections, anchorOrder, 1)).toBe(2) // before b (after hidden h1)
        expect(resolveSectionIndexForDrop(sections, anchorOrder, 3)).toBe(4) // end
    })

    it('falls back to appending when anchors are unknown', () => {
        expect(resolveSectionIndexForDrop([S('a')], [], 0)).toBe(1)
        expect(resolveSectionIndexForDrop([S('a')], ['ghost'], 0)).toBe(1)
    })

    it('computes reorderSections indices and suppresses no-op moves', () => {
        const sections = [S('a'), S('b'), S('c')]
        expect(computeReorderIndices(sections, 'a', 2)).toEqual({ oldIndex: 0, newIndex: 1 })
        expect(computeReorderIndices(sections, 'c', 0)).toEqual({ oldIndex: 2, newIndex: 0 })
        expect(computeReorderIndices(sections, 'a', 0)).toBeNull() // no-op
        expect(computeReorderIndices(sections, 'a', 1)).toBeNull() // insert after itself = no-op
        expect(computeReorderIndices(sections, 'missing', 1)).toBeNull()
    })
})

describe('architecture certification (source rules)', () => {
    it('BuilderCanvas swap is exclusively flag-gated and legacy path remains intact', () => {
        expect(builderCanvasSource).toContain('isThemeNativeCanvasEnabled()')
        expect(builderCanvasSource).toContain('<ThemeNativeCanvas')
        expect(builderCanvasSource).toContain('onInsertSection={(sectionType, insertionIndex) => {')
        expect(builderCanvasSource).toContain('isSectionAvailableForPage(sectionType, currentPage)')
        expect(builderCanvasSource).toContain('addSection(sectionType, insertionIndex, sectionRegistry)')
        // Legacy synthetic canvas is preserved for flag-OFF rollback.
        expect(builderCanvasSource).toContain('SortableSectionCard')
        expect(builderCanvasSource).toContain('StorefrontSectionRenderer')
    })

    it('ThemeNativeCanvas imports the bridge only via the sanctioned barrel', () => {
        expect(themeNativeCanvasSource).toContain("from '@/editor-bridge'")
        expect(themeNativeCanvasSource).not.toContain('editor-bridge/EditorBridge')
        expect(themeNativeCanvasSource).not.toContain('editor-bridge/OverlayRuntime')
        expect(themeNativeCanvasSource).not.toContain('editor-bridge/protocol')
    })

    it('ThemeNativeCanvas contains NO synthetic section rendering fallback (P5)', () => {
        expect(themeNativeCanvasSource).not.toContain('SectionRenderer')
        expect(themeNativeCanvasSource).not.toContain('sectionRegistry')
        expect(themeNativeCanvasSource).toContain('never synthetic fallback')
    })

    it('selection ownership stays with the host (child messages are proposals)', () => {
        expect(themeNativeCanvasSource).toContain("case 'selection.changed'")
        expect(themeNativeCanvasSource).toContain('PROPOSAL')
        expect(themeNativeCanvasSource).toContain('bridge.setSelection')
    })

    it('wires the full approved protocol surface and nothing beyond it', () => {
        expect(themeNativeCanvasSource).toContain('bridge.applyConfig')
        expect(themeNativeCanvasSource).toContain('bridge.setSelection')
        expect(themeNativeCanvasSource).toContain('bridge.setViewport')
        expect(themeNativeCanvasSource).toContain("case 'navigation.blocked'")
        // hover + rects flow through OverlayRuntime.attach, not bespoke handlers.
        expect(themeNativeCanvasSource).toContain('overlay.attach(bridge)')
    })

    it('ARCH-1.4R G1: toolbar drives Builder Store actions only (no new APIs)', () => {
        expect(themeNativeCanvasSource).toContain('toggleSection')
        expect(themeNativeCanvasSource).toContain('duplicateSection')
        expect(themeNativeCanvasSource).toContain('removeSection')
        expect(themeNativeCanvasSource).toContain('reorderSections')
        expect(themeNativeCanvasSource).toContain('selectSection')
    })

    it('ARCH-1.4R G2: drag reorder uses the frozen AnchorResolver.resolveDropIndicator', () => {
        expect(themeNativeCanvasSource).toContain('overlay.anchors.resolveDropIndicator')
        expect(themeNativeCanvasSource).toContain('theme-native-canvas-drop-indicator')
    })

    it('ARCH-1.4R G2 (review closure): palette insertion is a host droppable committing via onInsertSection only', () => {
        // dnd-kit droppable registered over the frame; insert delegated to the
        // host (BuilderCanvas → addSection). The canvas itself must NOT import
        // sectionRegistry (P5 source rule above stays authoritative).
        expect(themeNativeCanvasSource).toContain('useDroppable')
        expect(themeNativeCanvasSource).toContain('useDndMonitor')
        expect(themeNativeCanvasSource).toContain("'theme-native-canvas-dropzone'")
        expect(themeNativeCanvasSource).toContain('onInsertSection')
        expect(themeNativeCanvasSource).not.toContain("from '../../builder'")
    })

    it('review closure: reconnect replay reads the CURRENT storeConfig via ref', () => {
        expect(themeNativeCanvasSource).toContain('storeConfigRef.current')
        expect(themeNativeCanvasSource).toContain('storeConfigRef = useRef(storeConfig)')
    })

    it('review closure: iframe load uses React onLoad + idempotent connect (no listener race)', () => {
        expect(themeNativeCanvasSource).toContain('onLoad={()')
        expect(themeNativeCanvasSource).toContain('frameLoadedRef.current = true')
        expect(themeNativeCanvasSource).toContain('bridgeRef.current?.connect()')
    })

    it('review closure: frame offset is computed against the frame wrapper (overlay context), not the scroll host', () => {
        expect(themeNativeCanvasSource).toContain('wrapper.getBoundingClientRect()')
        expect(themeNativeCanvasSource).toContain('frameRect.left - wrapperRect.left')
        expect(themeNativeCanvasSource).not.toContain('+ host.scrollLeft')
    })

    it('review closure: active drag is cancelled on unmount via dragCleanupRef', () => {
        expect(themeNativeCanvasSource).toContain('dragCleanupRef.current?.()')
        expect(themeNativeCanvasSource).toContain('dragCleanupRef.current = cancelDrag')
    })

    it('ARCH-1.4R G3/G4: keyboard reorder + ARIA + live announcements exist', () => {
        expect(themeNativeCanvasSource).toContain("role=\"toolbar\"")
        expect(themeNativeCanvasSource).toContain('aria-live="polite"')
        expect(themeNativeCanvasSource).toContain("'ArrowUp'")
        expect(themeNativeCanvasSource).toContain("'ArrowDown'")
        expect(themeNativeCanvasSource).toContain("'Delete'")
        expect(themeNativeCanvasSource).toContain("'Escape'")
    })

    it('ARCH-1.4R G5: hidden-section tray is host chrome (metadata only, no synthetic rendering)', () => {
        expect(themeNativeCanvasSource).toContain('theme-native-canvas-hidden-tray')
        expect(themeNativeCanvasSource).toContain('hiddenSections')
    })

    it('ARCH-1.4R G6: native empty state with Load Demo is host chrome', () => {
        expect(themeNativeCanvasSource).toContain('theme-native-canvas-empty-state')
        expect(themeNativeCanvasSource).toContain('onLoadDemo')
    })

    it('ARCH-1.4R Gate E: no import from the legacy BuilderThemeProvider stack', () => {
        expect(themeNativeCanvasSource).not.toContain('BuilderThemeProvider')
        expect(themeNativeCanvasSource).toContain("from './normalizeStoreConfig'")
    })
})
