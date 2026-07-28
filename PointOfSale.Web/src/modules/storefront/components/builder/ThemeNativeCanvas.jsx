/**
 * Theme-Native Builder Canvas — ARCH-1.3 integration + ARCH-1.4R parity closure.
 *
 * Renders the REAL storefront runtime inside an iframe and drives it
 * exclusively through the frozen ARCH-1.2 Editor Bridge + Overlay Runtime.
 *
 * Frozen dependencies (imported from the sanctioned barrels only):
 *   @/editor-bridge  → EditorBridge, createIframeTransport, OverlayRuntime
 *
 * ARCH-1.4R additions (ALL host chrome — zero frozen-surface changes):
 *   G1 toolbar actions (hide/show/duplicate/delete/edit) → Builder Store actions
 *   G2 pointer drag reorder → AnchorResolver.resolveDropIndicator + reorderSections
 *   G3 keyboard reorder (Alt+Arrow) + Delete/Escape → Builder Store actions
 *   G4 ARIA toolbar/tray semantics + aria-live announcements + focus handling
 *   G5 hidden-section tray (host chrome; disabled sections are never rendered
 *      synthetically — the real runtime skips them, the tray only LISTS them)
 *   G6 native empty state + Load Demo (host chrome over the live frame)
 *
 * Fail-closed (P5): when the bridge degrades or fails there is NO synthetic
 * section rendering. The canvas shows an explicit degraded/failed banner and
 * a reconnect affordance; the last committed runtime frame stays visible.
 */

import { useCallback, useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react'
import { useTranslation } from 'react-i18next'
import { useDndMonitor, useDroppable } from '@dnd-kit/core'
import {
    AlertTriangle, ArrowDown, ArrowUp, Copy, Eye, EyeOff, GripVertical,
    Loader2, Monitor, RefreshCw, Settings, Smartphone, Tablet, Trash2,
} from 'lucide-react'

import { EditorBridge, OverlayRuntime, createIframeTransport } from '@/editor-bridge'
import { useBuilderStore, selectSections, selectSelectedSectionId, selectCurrentPage } from '../../store'
import { normalizeStoreConfig } from './normalizeStoreConfig'

const STOREFRONT_BASE_URL = import.meta.env.VITE_STOREFRONT_URL || 'http://localhost:5174'

const VIEWPORTS = [
    { key: 'desktop', icon: Monitor, width: 1280, frameWidth: '100%', label: { en: 'Desktop', ar: 'سطح المكتب' } },
    { key: 'tablet', icon: Tablet, width: 768, frameWidth: '768px', label: { en: 'Tablet', ar: 'لوحي' } },
    { key: 'mobile', icon: Smartphone, width: 375, frameWidth: '375px', label: { en: 'Mobile', ar: 'جوال' } },
]

/**
 * Build the iframe URL for editor mode. The child validates BOTH params and
 * refuses to start the bridge without them (storefront fail-closed rule).
 */
export function buildEditorFrameUrl(tenantId) {
    const url = new URL(STOREFRONT_BASE_URL)
    url.searchParams.set('editorMode', '1')
    url.searchParams.set('editorHostOrigin', window.location.origin)
    if (tenantId) {
        url.searchParams.set('tenant', tenantId)
    }
    return url.toString()
}

/**
 * Compose the opaque candidate document sent over config.apply.
 * Shape = runtime StoreConfig with the DRAFT sections of the page being
 * edited substituted in. The bridge never inspects this (opaque rule);
 * only the storefront applier interprets it.
 */
export function buildCandidateDocument(storeConfig, currentPage, sections) {
    const normalized = normalizeStoreConfig(storeConfig)
    return {
        ...normalized,
        pages: {
            ...(normalized.pages || {}),
            [currentPage]: {
                ...((normalized.pages || {})[currentPage] || {}),
                sections,
            },
        },
    }
}

/**
 * G2 helper — map a drop indicator index (position among VISIBLE anchors in
 * rect order) to an insertion index in the FULL draft sections array
 * (hidden sections produce no anchors — the real runtime skips them).
 */
export function resolveSectionIndexForDrop(sections, anchorOrder, dropIndex) {
    if (anchorOrder.length === 0) return sections.length
    if (dropIndex >= anchorOrder.length) {
        const lastVisibleId = anchorOrder[anchorOrder.length - 1]
        const idx = sections.findIndex((s) => s.id === lastVisibleId)
        return idx === -1 ? sections.length : idx + 1
    }
    const anchorId = anchorOrder[dropIndex]
    const idx = sections.findIndex((s) => s.id === anchorId)
    return idx === -1 ? sections.length : idx
}

/**
 * G2/G3 helper — convert an insertion index into reorderSections(old, new)
 * splice indices. Returns null when the move is a no-op.
 */
export function computeReorderIndices(sections, draggedId, insertionIndex) {
    const oldIndex = sections.findIndex((s) => s.id === draggedId)
    if (oldIndex === -1) return null
    let newIndex = insertionIndex
    if (insertionIndex > oldIndex) newIndex = insertionIndex - 1
    newIndex = Math.max(0, Math.min(newIndex, sections.length - 1))
    if (newIndex === oldIndex) return null
    return { oldIndex, newIndex }
}

function useStoreSlice(store, selector) {
    const subscribe = useCallback((listener) => store.subscribe(listener), [store])
    const getSnapshot = useCallback(() => selector(store.getState()), [store, selector])
    return useSyncExternalStore(subscribe, getSnapshot)
}

const selectSelf = (state) => state

export function ThemeNativeCanvas({ storeConfig, tenantId, onLoadDemo, onInsertSection }) {
    const { i18n } = useTranslation()
    const isRTL = i18n.language === 'ar'
    const t = useCallback((en, ar) => (isRTL ? ar : en), [isRTL])

    const sections = useBuilderStore(selectSections)
    const selectedSectionId = useBuilderStore(selectSelectedSectionId)
    const currentPage = useBuilderStore(selectCurrentPage)
    const selectSection = useBuilderStore((s) => s.selectSection)
    const toggleSection = useBuilderStore((s) => s.toggleSection)
    const duplicateSection = useBuilderStore((s) => s.duplicateSection)
    const removeSection = useBuilderStore((s) => s.removeSection)
    const reorderSections = useBuilderStore((s) => s.reorderSections)

    const [bridgeState, setBridgeState] = useState('idle')
    const [lifecycle, setLifecycle] = useState('detached')
    const [lastError, setLastError] = useState(null)
    const [activeViewport, setActiveViewport] = useState('desktop')
    const [frameEpoch, setFrameEpoch] = useState(0)
    const [dropIndicator, setDropIndicator] = useState(null)
    const [draggingAnchorId, setDraggingAnchorId] = useState(null)
    const [announcement, setAnnouncement] = useState('')

    const iframeRef = useRef(null)
    const scrollHostRef = useRef(null)
    const frameWrapperRef = useRef(null)
    const bridgeRef = useRef(null)
    const overlayRef = useRef(null)
    const toolbarRef = useRef(null)
    const dragRef = useRef(null)
    const dragCleanupRef = useRef(null)
    const frameLoadedRef = useRef(false)

    // Always-current storeConfig for reconnect replay (the bridge lifecycle
    // effect intentionally does NOT depend on storeConfig — a ref keeps the
    // ready handler from closing over a stale value).
    const storeConfigRef = useRef(storeConfig)
    storeConfigRef.current = storeConfig

    // Stable overlay runtime for the lifetime of the canvas mount.
    const overlay = useMemo(() => new OverlayRuntime(), [])
    overlayRef.current = overlay

    const frameUrl = useMemo(() => buildEditorFrameUrl(tenantId), [tenantId])

    const sectionLabel = useCallback(
        (sectionId) => sections.find((s) => s.id === sectionId)?.type || sectionId,
        [sections]
    )

    const announce = useCallback((message) => setAnnouncement(message), [])

    /* ---------------- bridge lifecycle ---------------- */

    useEffect(() => {
        const iframe = iframeRef.current
        if (!iframe) return undefined

        const childOrigin = new URL(STOREFRONT_BASE_URL).origin
        const bridge = new EditorBridge({
            transport: createIframeTransport(iframe, childOrigin),
            expectedOrigin: childOrigin,
            hostKind: 'pos-admin-builder',
            requestedCapabilities: ['rects', 'selection', 'hover', 'scroll', 'viewport', 'navigation-blocking', 'diagnostics'],
            expectedTenantId: tenantId || undefined,
        })
        bridgeRef.current = bridge

        const disposers = [
            bridge.on('stateChanged', (state) => setBridgeState(state)),
            bridge.on('failed', (reason) => setLastError(reason)),
            bridge.on('handshakeError', (payload) => setLastError(`${payload.code}: ${payload.message}`)),
            bridge.on('ready', () => {
                setLastError(null)
                // Initial document push: current draft over the CURRENT admitted
                // config (ref — never a stale closure on reconnect).
                const store = useBuilderStore.getState()
                bridge.applyConfig(buildCandidateDocument(storeConfigRef.current, store.currentPage, store.sections))
                // Re-assert host-owned selection after (re)connect.
                bridge.setSelection(useBuilderStore.getState().selectedSectionId ?? null)
            }),
            bridge.on('message', (message) => {
                switch (message.type) {
                    case 'selection.changed':
                        // Child PROPOSAL → host decides. Host accepts proposals
                        // that reference known draft sections (or clears).
                        {
                            const proposed = message.payload?.anchorId ?? null
                            const known = proposed === null
                                || useBuilderStore.getState().sections.some((s) => s.id === proposed)
                            if (known) selectSection(proposed)
                        }
                        return
                    case 'navigation.blocked':
                        if (import.meta.env.DEV) {
                            console.log('[ThemeNativeCanvas] navigation blocked in editor mode:', message.payload?.href)
                        }
                        return
                    case 'runtime.error':
                        setLastError(message.payload?.message || 'Runtime error inside storefront.')
                        return
                    default:
                        return
                }
            }),
        ]

        overlay.attach(bridge)
        const offLifecycle = overlay.onLifecycle(setLifecycle)
        setLifecycle(overlay.getLifecycle())

        const onFrameLoad = () => bridge.connect()
        iframe.addEventListener('load', onFrameLoad)
        // Load-race guard: if the (possibly cached) frame already fired its
        // load event before this effect attached, connect immediately.
        // EditorBridge.connect() is idempotent (no-op while connecting/connected).
        if (frameLoadedRef.current && iframe.contentWindow) {
            bridge.connect()
        }

        return () => {
            iframe.removeEventListener('load', onFrameLoad)
            offLifecycle()
            for (const dispose of disposers) dispose()
            overlay.detach()
            bridge.shutdown('host-closed')
            bridgeRef.current = null
        }
        // frameEpoch forces a full bridge session rebuild on manual reload.
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [tenantId, frameEpoch])

    /* ---------------- host state → bridge ---------------- */

    // Draft document sync (config.apply). Bridge revision monotonicity +
    // rAF-coalesced rects make rapid edits safe without extra debouncing.
    useEffect(() => {
        const bridge = bridgeRef.current
        if (!bridge || bridge.getState() !== 'connected') return
        bridge.applyConfig(buildCandidateDocument(storeConfig, currentPage, sections))
    }, [storeConfig, currentPage, sections])

    // Host-owned selection → child mirror (selection.set).
    useEffect(() => {
        const bridge = bridgeRef.current
        if (!bridge || bridge.getState() !== 'connected') return
        bridge.setSelection(selectedSectionId ?? null)
        overlay.applyHostSelection(selectedSectionId ?? null)
        if (selectedSectionId) bridge.scrollTo(selectedSectionId)
    }, [selectedSectionId, overlay])

    // Viewport switching (viewport.set).
    useEffect(() => {
        const bridge = bridgeRef.current
        if (!bridge || bridge.getState() !== 'connected') return
        const viewport = VIEWPORTS.find((entry) => entry.key === activeViewport)
        if (viewport) bridge.setViewport(viewport.width)
    }, [activeViewport, bridgeState])

    // Keep the coordinate mapper aligned with the iframe position.
    // CONTRACT (CoordinateMapper): frameOffset is the iframe's offset inside
    // the OVERLAY POSITIONING CONTEXT — the frame wrapper (overlay chrome is
    // absolute inset-0 within it) — NOT the scroll host. Wrapper-relative
    // offset is border/layout deltas only; host padding/centering/scroll
    // never leak into overlay space.
    useEffect(() => {
        const iframe = iframeRef.current
        const wrapper = frameWrapperRef.current
        const host = scrollHostRef.current
        if (!iframe || !wrapper) return undefined

        const syncOffset = () => {
            const frameRect = iframe.getBoundingClientRect()
            const wrapperRect = wrapper.getBoundingClientRect()
            overlay.viewport.setFrameOffset({
                x: frameRect.left - wrapperRect.left,
                y: frameRect.top - wrapperRect.top,
            })
        }

        syncOffset()
        const resizeObserver = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(syncOffset) : null
        resizeObserver?.observe(iframe)
        resizeObserver?.observe(wrapper)
        host?.addEventListener('scroll', syncOffset, { passive: true })
        return () => {
            resizeObserver?.disconnect()
            host?.removeEventListener('scroll', syncOffset)
        }
    }, [overlay, activeViewport, frameEpoch])

    // Cancel any in-flight pointer drag if the canvas unmounts (listener-leak guard).
    useEffect(() => {
        return () => {
            dragCleanupRef.current?.()
            dragCleanupRef.current = null
        }
    }, [])

    /* ---------------- G1/G3: toolbar + keyboard actions (Builder Store only) ---------------- */

    const handleToggle = useCallback((sectionId) => {
        const target = useBuilderStore.getState().sections.find((s) => s.id === sectionId)
        toggleSection(sectionId)
        announce(target?.enabled
            ? t(`Section ${target?.type || sectionId} hidden.`, `تم إخفاء القسم ${target?.type || sectionId}.`)
            : t(`Section ${target?.type || sectionId} shown.`, `تم إظهار القسم ${target?.type || sectionId}.`))
    }, [toggleSection, announce, t])

    const handleDuplicate = useCallback((sectionId) => {
        duplicateSection(sectionId)
        announce(t(`Section ${sectionLabel(sectionId)} duplicated.`, `تم تكرار القسم ${sectionLabel(sectionId)}.`))
    }, [duplicateSection, announce, sectionLabel, t])

    const handleDelete = useCallback((sectionId) => {
        const label = sectionLabel(sectionId)
        removeSection(sectionId)
        announce(t(`Section ${label} deleted.`, `تم حذف القسم ${label}.`))
    }, [removeSection, announce, sectionLabel, t])

    const handleEdit = useCallback((sectionId) => {
        // Edit = host-owned selection; the Settings panel renders the editor
        // for the selected section (same contract as the legacy canvas).
        selectSection(sectionId)
        announce(t(`Editing section ${sectionLabel(sectionId)}.`, `جارٍ تعديل القسم ${sectionLabel(sectionId)}.`))
    }, [selectSection, announce, sectionLabel, t])

    const moveSectionBy = useCallback((sectionId, delta) => {
        const draft = useBuilderStore.getState().sections
        const oldIndex = draft.findIndex((s) => s.id === sectionId)
        if (oldIndex === -1) return
        const newIndex = Math.max(0, Math.min(draft.length - 1, oldIndex + delta))
        if (newIndex === oldIndex) return
        reorderSections(oldIndex, newIndex)
        announce(t(
            `Section ${sectionLabel(sectionId)} moved to position ${newIndex + 1} of ${draft.length}.`,
            `تم نقل القسم ${sectionLabel(sectionId)} إلى الموضع ${newIndex + 1} من ${draft.length}.`
        ))
    }, [reorderSections, announce, sectionLabel, t])

    // G3: accessible keyboard move/delete/escape on the floating toolbar.
    const handleToolbarKeyDown = useCallback((event) => {
        if (!selectedSectionId) return
        if (event.altKey && event.key === 'ArrowUp') {
            event.preventDefault()
            moveSectionBy(selectedSectionId, -1)
        } else if (event.altKey && event.key === 'ArrowDown') {
            event.preventDefault()
            moveSectionBy(selectedSectionId, 1)
        } else if (event.key === 'Delete') {
            event.preventDefault()
            handleDelete(selectedSectionId)
        } else if (event.key === 'Escape') {
            event.preventDefault()
            selectSection(null)
            announce(t('Selection cleared.', 'تم إلغاء التحديد.'))
        }
    }, [selectedSectionId, moveSectionBy, handleDelete, selectSection, announce, t])

    // G4: focus management — keep keyboard context on the toolbar when the
    // selection survives a reorder (toolbar re-renders at a new anchor).
    useEffect(() => {
        if (!selectedSectionId || !draggingAnchorId) return
        toolbarRef.current?.focus?.()
    }, [selectedSectionId, draggingAnchorId])

    /* ---------------- G2: pointer drag reorder (overlay-space, host chrome) ---------------- */

    const beginDrag = useCallback((sectionId, pointerEvent) => {
        pointerEvent.preventDefault()
        pointerEvent.stopPropagation()
        const host = scrollHostRef.current
        if (!host) return
        // Cancel any previous unfinished drag before starting a new one.
        dragCleanupRef.current?.()
        dragRef.current = { sectionId }
        setDraggingAnchorId(sectionId)
        announce(t(
            `Started moving section ${sectionLabel(sectionId)}. Drop to reorder.`,
            `بدأ نقل القسم ${sectionLabel(sectionId)}. أفلت لإعادة الترتيب.`
        ))

        const overlayEl = frameWrapperRef.current ?? host.querySelector('[data-testid="theme-native-canvas-overlay"]')

        const toOverlayY = (clientY) => {
            const reference = overlayEl?.getBoundingClientRect() ?? host.getBoundingClientRect()
            return clientY - reference.top
        }

        const removeListeners = () => {
            window.removeEventListener('pointermove', onPointerMove)
            window.removeEventListener('pointerup', finishDrag)
            window.removeEventListener('pointercancel', cancelDrag)
            dragCleanupRef.current = null
        }

        const onPointerMove = (moveEvent) => {
            const indicator = overlay.anchors.resolveDropIndicator(toOverlayY(moveEvent.clientY))
            setDropIndicator(indicator)
        }

        const finishDrag = (upEvent) => {
            removeListeners()

            const active = dragRef.current
            dragRef.current = null
            setDraggingAnchorId(null)
            setDropIndicator(null)
            if (!active) return

            const indicator = overlay.anchors.resolveDropIndicator(toOverlayY(upEvent.clientY))
            if (!indicator) return

            const draft = useBuilderStore.getState().sections
            const anchorOrder = overlay.rects.getState().order
            const insertionIndex = resolveSectionIndexForDrop(draft, anchorOrder, indicator.index)
            const move = computeReorderIndices(draft, active.sectionId, insertionIndex)
            if (!move) {
                announce(t('Section position unchanged.', 'لم يتغير موضع القسم.'))
                return
            }
            reorderSections(move.oldIndex, move.newIndex)
            announce(t(
                `Section ${sectionLabel(active.sectionId)} moved to position ${move.newIndex + 1} of ${draft.length}.`,
                `تم نقل القسم ${sectionLabel(active.sectionId)} إلى الموضع ${move.newIndex + 1} من ${draft.length}.`
            ))
        }

        const cancelDrag = () => {
            removeListeners()
            dragRef.current = null
            setDraggingAnchorId(null)
            setDropIndicator(null)
            announce(t('Move cancelled.', 'تم إلغاء النقل.'))
        }

        window.addEventListener('pointermove', onPointerMove)
        window.addEventListener('pointerup', finishDrag)
        window.addEventListener('pointercancel', cancelDrag)
        // Unmount safety: expose a cleanup that detaches listeners and state.
        dragCleanupRef.current = cancelDrag
    }, [overlay, reorderSections, announce, sectionLabel, t])

    /* ---------------- G2: palette insertion (dnd-kit drop target over the frame) ---------------- */

    // Native canvas registers as a droppable inside the page-level DndContext
    // so palette items have a valid drop target over the iframe. Insertion is
    // committed by the HOST (onInsertSection → existing addSection) — no new
    // APIs, no bridge involvement.
    const { setNodeRef: setPaletteDropRef, isOver: isPaletteOver } = useDroppable({ id: 'theme-native-canvas-dropzone' })

    useDndMonitor({
        onDragMove(event) {
            const activeId = String(event.active?.id ?? '')
            if (!activeId.startsWith('palette-')) return
            const wrapper = frameWrapperRef.current
            if (!wrapper) return
            const pointerY = (event.active.rect?.current?.translated?.top ?? 0)
                + ((event.active.rect?.current?.translated?.height ?? 0) / 2)
            const overlayY = pointerY - wrapper.getBoundingClientRect().top
            setDropIndicator(overlay.anchors.resolveDropIndicator(overlayY))
        },
        onDragEnd(event) {
            const activeId = String(event.active?.id ?? '')
            if (!activeId.startsWith('palette-')) return
            const indicator = dropIndicator
            setDropIndicator(null)
            if (!onInsertSection) return
            if (event.over?.id !== 'theme-native-canvas-dropzone') return
            const sectionType = activeId.replace('palette-', '')
            const draft = useBuilderStore.getState().sections
            const anchorOrder = overlay.rects.getState().order
            const insertionIndex = indicator
                ? resolveSectionIndexForDrop(draft, anchorOrder, indicator.index)
                : draft.length
            onInsertSection(sectionType, insertionIndex)
            announce(t(
                `Section ${sectionType} inserted at position ${insertionIndex + 1}.`,
                `تم إدراج القسم ${sectionType} في الموضع ${insertionIndex + 1}.`
            ))
        },
        onDragCancel() {
            setDropIndicator(null)
        },
    })

    /* ---------------- overlay-derived UI state ---------------- */

    const selectionState = useStoreSlice(overlay.selection, selectSelf)
    const rectState = useStoreSlice(overlay.rects, selectSelf)

    const selectedOverlayRect = selectedSectionId ? overlay.anchors.resolveRect(selectedSectionId) : null
    const hoveredOverlayRect = selectionState.hoveredAnchorId && selectionState.hoveredAnchorId !== selectedSectionId
        ? overlay.anchors.resolveRect(selectionState.hoveredAnchorId)
        : null
    const toolbar = selectedSectionId ? overlay.anchors.resolveToolbar(selectedSectionId, 36) : null

    const isDegraded = lifecycle === 'degraded' || bridgeState === 'failed' || bridgeState === 'reconnecting'
    const isConnecting = bridgeState === 'idle' || bridgeState === 'connecting'
    const currentViewport = VIEWPORTS.find((entry) => entry.key === activeViewport)

    // G5: hidden sections never produce anchors (the real runtime skips
    // disabled sections) — surface them in a HOST-side tray. No synthetic
    // rendering of section content; the tray lists metadata only.
    const hiddenSections = sections.filter((section) => !section.enabled)
    const selectedSection = sections.find((section) => section.id === selectedSectionId) || null
    const isEmptyDraft = sections.length === 0

    const handleReload = useCallback(() => {
        setLastError(null)
        setBridgeState('idle')
        frameLoadedRef.current = false // new iframe element (keyed by epoch)
        setFrameEpoch((epoch) => epoch + 1)
    }, [])

    const toolbarButtonClass = 'flex h-6 w-6 items-center justify-center rounded transition-colors hover:bg-white/20 focus:outline-none focus-visible:ring-2 focus-visible:ring-white'

    return (
        <div className="flex-1 h-full bg-gray-100 overflow-hidden flex flex-col" data-testid="theme-native-canvas">
            {/* G4: screen-reader announcements for every editing operation */}
            <div aria-live="polite" role="status" className="sr-only" data-testid="theme-native-canvas-announcer">
                {announcement}
            </div>

            {/* Canvas toolbar: viewport switch + bridge status */}
            <div className="flex h-11 items-center justify-between border-b border-gray-200 bg-white px-4">
                <div className="flex items-center gap-2 rounded-lg bg-gray-100 p-1" role="group" aria-label={t('Preview viewport', 'إطار المعاينة')}>
                    {VIEWPORTS.map((viewport) => {
                        const Icon = viewport.icon
                        return (
                            <button
                                key={viewport.key}
                                onClick={() => setActiveViewport(viewport.key)}
                                aria-pressed={activeViewport === viewport.key}
                                className={`flex items-center gap-1.5 rounded px-2.5 py-1 text-xs font-medium transition-colors ${activeViewport === viewport.key
                                    ? 'bg-indigo-600 text-white'
                                    : 'text-gray-600 hover:text-gray-900'}`}
                                title={isRTL ? viewport.label.ar : viewport.label.en}
                            >
                                <Icon size={14} />
                                <span className="hidden lg:inline">{isRTL ? viewport.label.ar : viewport.label.en}</span>
                            </button>
                        )
                    })}
                </div>

                <div className="flex items-center gap-3">
                    <span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium ${bridgeState === 'connected'
                        ? 'bg-emerald-100 text-emerald-700'
                        : isDegraded
                            ? 'bg-amber-100 text-amber-700'
                            : 'bg-gray-100 text-gray-600'}`}
                    >
                        <span className={`h-1.5 w-1.5 rounded-full ${bridgeState === 'connected' ? 'bg-emerald-500' : isDegraded ? 'bg-amber-500' : 'bg-gray-400'}`} />
                        {bridgeState}
                    </span>
                    <button
                        onClick={handleReload}
                        className="flex items-center gap-1.5 rounded px-2 py-1 text-xs text-gray-600 transition-colors hover:bg-gray-100 hover:text-gray-900"
                        title={t('Reload preview', 'إعادة تحميل المعاينة')}
                    >
                        <RefreshCw size={13} />
                        {t('Reload', 'إعادة تحميل')}
                    </button>
                </div>
            </div>

            {/* Degraded / failed banner — explicit, never synthetic fallback */}
            {(isDegraded || lastError) && (
                <div className="flex items-center justify-between border-b border-amber-200 bg-amber-50 px-4 py-2">
                    <div className="flex items-center gap-2 text-sm text-amber-800">
                        <AlertTriangle size={14} />
                        <span>
                            {lastError
                                || t('Editor connection to the storefront is degraded. Retrying...', 'اتصال المحرر بالمتجر متدهور. تتم إعادة المحاولة...')}
                        </span>
                    </div>
                    <button
                        onClick={handleReload}
                        className="rounded bg-amber-600 px-3 py-1 text-xs font-medium text-white transition-colors hover:bg-amber-700"
                    >
                        {t('Reconnect', 'إعادة الاتصال')}
                    </button>
                </div>
            )}

            {/* G5: host-side hidden-section tray (metadata only — no synthetic rendering) */}
            {hiddenSections.length > 0 && (
                <div
                    className="flex flex-wrap items-center gap-2 border-b border-gray-200 bg-gray-50 px-4 py-2"
                    role="region"
                    aria-label={t('Hidden sections', 'الأقسام المخفية')}
                    data-testid="theme-native-canvas-hidden-tray"
                >
                    <span className="flex items-center gap-1.5 text-xs font-medium text-gray-500">
                        <EyeOff size={13} />
                        {t('Hidden sections', 'الأقسام المخفية')} ({hiddenSections.length})
                    </span>
                    {hiddenSections.map((section) => (
                        <span
                            key={section.id}
                            className={`inline-flex items-center gap-1 rounded-full border px-2 py-0.5 text-xs ${section.id === selectedSectionId
                                ? 'border-indigo-400 bg-indigo-50 text-indigo-700'
                                : 'border-gray-300 bg-white text-gray-600'}`}
                        >
                            <button
                                onClick={() => handleEdit(section.id)}
                                className="font-medium hover:text-indigo-700 focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-400"
                                title={t('Edit section', 'تعديل القسم')}
                            >
                                {section.type}
                            </button>
                            <button
                                onClick={() => handleToggle(section.id)}
                                className="rounded p-0.5 hover:bg-emerald-100 hover:text-emerald-700 focus:outline-none focus-visible:ring-2 focus-visible:ring-emerald-400"
                                title={t('Show section', 'إظهار القسم')}
                                aria-label={t(`Show section ${section.type}`, `إظهار القسم ${section.type}`)}
                            >
                                <Eye size={12} />
                            </button>
                            <button
                                onClick={() => handleDelete(section.id)}
                                className="rounded p-0.5 hover:bg-red-100 hover:text-red-600 focus:outline-none focus-visible:ring-2 focus-visible:ring-red-400"
                                title={t('Delete section', 'حذف القسم')}
                                aria-label={t(`Delete section ${section.type}`, `حذف القسم ${section.type}`)}
                            >
                                <Trash2 size={12} />
                            </button>
                        </span>
                    ))}
                </div>
            )}

            {/* Scroll host: iframe + absolutely-positioned overlay chrome */}
            <div ref={scrollHostRef} className="relative flex-1 overflow-auto bg-gray-200 p-4">
                <div
                    ref={(node) => {
                        frameWrapperRef.current = node
                        setPaletteDropRef(node)
                    }}
                    className={`relative mx-auto transition-all duration-300 ${isPaletteOver ? 'ring-2 ring-indigo-400 ring-offset-2 rounded-xl' : ''}`}
                    style={{ width: currentViewport?.frameWidth ?? '100%', maxWidth: '100%' }}
                    data-testid="theme-native-canvas-dropzone"
                >
                    <iframe
                        key={frameEpoch}
                        ref={iframeRef}
                        src={frameUrl}
                        onLoad={() => {
                            // Deterministic load signal (no addEventListener race):
                            // mark loaded and connect via the current bridge.
                            frameLoadedRef.current = true
                            bridgeRef.current?.connect()
                        }}
                        title={t('Live storefront preview', 'معاينة المتجر الحية')}
                        sandbox="allow-scripts allow-same-origin allow-forms"
                        className="h-[calc(100vh-220px)] min-h-[480px] w-full rounded-xl border border-gray-300 bg-white shadow-lg"
                        data-testid="theme-native-canvas-frame"
                    />

                    {isConnecting && (
                        <div className="absolute inset-0 grid place-items-center rounded-xl bg-white/70 backdrop-blur-[1px]">
                            <div className="flex items-center gap-2 text-sm font-medium text-gray-600">
                                <Loader2 className="h-4 w-4 animate-spin" />
                                {t('Connecting to storefront runtime...', 'جارٍ الاتصال بواجهة المتجر...')}
                            </div>
                        </div>
                    )}

                    {/* G6: native empty state — host chrome ABOVE the live frame.
                        The real runtime renders whatever the (empty) draft says;
                        this card only offers Load Demo. No synthetic sections. */}
                    {isEmptyDraft && bridgeState === 'connected' && (
                        <div className="absolute inset-0 grid place-items-center rounded-xl bg-white/85 backdrop-blur-[1px]" data-testid="theme-native-canvas-empty-state">
                            <div className="mx-auto max-w-md text-center">
                                <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-2xl bg-gray-100">
                                    <Settings size={32} className="text-gray-400" />
                                </div>
                                <h3 className="mb-2 text-xl font-semibold text-gray-900">
                                    {t('Start Building Your Page', 'ابدأ في بناء صفحتك')}
                                </h3>
                                <p className="mb-6 text-gray-500">
                                    {t(
                                        'Add sections from the palette, or load a demo template into the live storefront preview.',
                                        'أضف الأقسام من اللوحة الجانبية، أو حمّل قالباً تجريبياً في المعاينة الحية للمتجر.'
                                    )}
                                </p>
                                {onLoadDemo && (
                                    <button
                                        onClick={onLoadDemo}
                                        className="w-full rounded-lg bg-indigo-600 px-4 py-2 font-medium text-white transition-colors hover:bg-indigo-700"
                                    >
                                        {t('Load Demo Template', 'تحميل قالب تجريبي')}
                                    </button>
                                )}
                            </div>
                        </div>
                    )}

                    {/* Overlay chrome (host-rendered; child stays pixel-pure) */}
                    <div className="pointer-events-none absolute inset-0 overflow-hidden rounded-xl" data-testid="theme-native-canvas-overlay">
                        {hoveredOverlayRect && (
                            <div
                                className="absolute rounded border-2 border-indigo-300"
                                style={{
                                    left: hoveredOverlayRect.x,
                                    top: hoveredOverlayRect.y,
                                    width: hoveredOverlayRect.width,
                                    height: hoveredOverlayRect.height,
                                }}
                            />
                        )}
                        {selectedOverlayRect && (
                            <div
                                className={`absolute rounded border-2 ${draggingAnchorId === selectedSectionId ? 'border-indigo-400 opacity-60' : 'border-indigo-600'} ring-2 ring-indigo-200`}
                                style={{
                                    left: selectedOverlayRect.x,
                                    top: selectedOverlayRect.y,
                                    width: selectedOverlayRect.width,
                                    height: selectedOverlayRect.height,
                                }}
                            />
                        )}

                        {/* G2: drop indicator line (AnchorResolver.resolveDropIndicator) */}
                        {dropIndicator && (
                            <div
                                className="absolute h-1 rounded-full bg-indigo-500 shadow"
                                data-testid="theme-native-canvas-drop-indicator"
                                style={{
                                    left: dropIndicator.x,
                                    top: dropIndicator.y - 2,
                                    width: dropIndicator.width,
                                }}
                            />
                        )}

                        {/* G1/G3/G4: floating section toolbar — Builder Store actions only */}
                        {toolbar && selectedSection && (
                            <div
                                ref={toolbarRef}
                                role="toolbar"
                                tabIndex={-1}
                                aria-label={t(`Section ${selectedSection.type} actions`, `إجراءات القسم ${selectedSection.type}`)}
                                onKeyDown={handleToolbarKeyDown}
                                className="pointer-events-auto absolute flex h-8 items-center gap-1 rounded-md bg-indigo-600 px-2 text-xs font-medium text-white shadow-lg focus:outline-none focus-visible:ring-2 focus-visible:ring-indigo-300"
                                style={{ left: toolbar.position.x, top: toolbar.position.y }}
                                data-testid="theme-native-canvas-toolbar"
                            >
                                <button
                                    onPointerDown={(event) => beginDrag(selectedSection.id, event)}
                                    className={`${toolbarButtonClass} cursor-grab active:cursor-grabbing`}
                                    title={t('Drag to reorder', 'اسحب لإعادة الترتيب')}
                                    aria-label={t('Drag to reorder section', 'اسحب لإعادة ترتيب القسم')}
                                >
                                    <GripVertical size={14} />
                                </button>
                                <span className="max-w-[120px] truncate px-1">{selectedSection.type}</span>
                                <button
                                    onClick={() => moveSectionBy(selectedSection.id, -1)}
                                    className={toolbarButtonClass}
                                    title={t('Move up (Alt+ArrowUp)', 'نقل لأعلى (Alt+سهم لأعلى)')}
                                    aria-label={t('Move section up', 'نقل القسم لأعلى')}
                                >
                                    <ArrowUp size={14} />
                                </button>
                                <button
                                    onClick={() => moveSectionBy(selectedSection.id, 1)}
                                    className={toolbarButtonClass}
                                    title={t('Move down (Alt+ArrowDown)', 'نقل لأسفل (Alt+سهم لأسفل)')}
                                    aria-label={t('Move section down', 'نقل القسم لأسفل')}
                                >
                                    <ArrowDown size={14} />
                                </button>
                                <button
                                    onClick={() => handleToggle(selectedSection.id)}
                                    className={toolbarButtonClass}
                                    title={selectedSection.enabled ? t('Hide', 'إخفاء') : t('Show', 'إظهار')}
                                    aria-pressed={!selectedSection.enabled}
                                    aria-label={selectedSection.enabled
                                        ? t('Hide section', 'إخفاء القسم')
                                        : t('Show section', 'إظهار القسم')}
                                >
                                    {selectedSection.enabled ? <Eye size={14} /> : <EyeOff size={14} />}
                                </button>
                                <button
                                    onClick={() => handleDuplicate(selectedSection.id)}
                                    className={toolbarButtonClass}
                                    title={t('Duplicate', 'تكرار')}
                                    aria-label={t('Duplicate section', 'تكرار القسم')}
                                >
                                    <Copy size={14} />
                                </button>
                                <button
                                    onClick={() => handleEdit(selectedSection.id)}
                                    className={toolbarButtonClass}
                                    title={t('Edit', 'تعديل')}
                                    aria-label={t('Edit section settings', 'تعديل إعدادات القسم')}
                                >
                                    <Settings size={14} />
                                </button>
                                <button
                                    onClick={() => handleDelete(selectedSection.id)}
                                    className={`${toolbarButtonClass} hover:bg-red-500/60`}
                                    title={t('Delete (Delete)', 'حذف (Delete)')}
                                    aria-label={t('Delete section', 'حذف القسم')}
                                >
                                    <Trash2 size={14} />
                                </button>
                            </div>
                        )}

                        {rectState.stale && bridgeState === 'connected' && (
                            <div className="absolute right-2 top-2 rounded bg-gray-900/70 px-2 py-1 text-[10px] font-medium text-white">
                                {t('Syncing...', 'مزامنة...')}
                            </div>
                        )}
                    </div>
                </div>
            </div>
        </div>
    )
}
