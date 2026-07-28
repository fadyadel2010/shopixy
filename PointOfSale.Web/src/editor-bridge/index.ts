/**
 * Editor Bridge foundation — public API surface (ARCH-1.2).
 * Consumers must import from this barrel only.
 */
export * from './protocol';
export { BridgeDiagnostics, type BridgeDiagnosticsSnapshot, type HandshakeLogEntry } from './diagnostics';
export {
    EditorBridge,
    createIframeTransport,
    type BridgeEventMap,
    type BridgeState,
    type EditorBridgeOptions,
    type MessageTransport,
} from './EditorBridge';
export {
    AnchorResolver,
    CoordinateMapper,
    OverlayRuntime,
    RectStore,
    SelectionStore,
    ViewportTracker,
    type DropIndicator,
    type OverlayLifecycle,
    type OverlayPoint,
    type OverlayRect,
    type RectStoreState,
    type SelectionState,
    type ToolbarAnchor,
    type ViewportState,
} from './OverlayRuntime';
