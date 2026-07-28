/**
 * Editor Bridge foundation — storefront-side public API surface (ARCH-1.2).
 * Consumers must import from this barrel only.
 */
export * from './protocol';
export { BridgeDiagnostics, type BridgeDiagnosticsSnapshot, type HandshakeLogEntry } from './diagnostics';
export {
    ANCHOR_ATTRIBUTE,
    ANCHOR_KIND_ATTRIBUTE,
    ChildRuntimeBridge,
    type ChildRuntimeBridgeOptions,
} from './ChildRuntimeBridge';
