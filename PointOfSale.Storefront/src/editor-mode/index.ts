/**
 * Editor Mode (ARCH-1.3) — public surface. Import from this barrel only.
 */
export {
    EDITOR_MODE_PARAM,
    EDITOR_HOST_ORIGIN_PARAM,
    readEditorModeParams,
    applyCandidateStoreConfig,
    createEditorModeBridge,
    startEditorModeIfRequested,
    isEditorModeActive,
    __resetEditorModeForTests,
    type EditorModeParams,
    type CreateEditorModeBridgeOptions,
} from './editorMode';
