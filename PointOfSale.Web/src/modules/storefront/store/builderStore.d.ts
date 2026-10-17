import type { StoreApi, UseBoundStore } from 'zustand';
export interface BuilderSection { id: string; type: string; order: number; enabled: boolean; settings: Record<string, unknown> }
export interface SectionRegistry { get(type: string): unknown }
export interface BuilderState { currentPage: string; sections: BuilderSection[]; selectedSectionId: string | null; isDirty: boolean; isSaving: boolean; isPublishing: boolean; isPreviewMode: boolean; undoStack: BuilderSection[][]; redoStack: BuilderSection[][];
    setCurrentPage(page: string): void; setSections(sections: BuilderSection[]): void; admitSections(sections: BuilderSection[], options?: { resetHistory?: boolean; preserveSelection?: boolean }): void;
    selectSection(id: string | null): void; addSection(type: string, index: number | undefined, registry: SectionRegistry): void;
    removeSection(id: string): void; duplicateSection(id: string): void; reorderSections(oldIndex: number, newIndex: number): void; updateSectionSettings(id: string, settings: Record<string, unknown>): void; toggleSection(id: string): void;
    markDirty(): void; markClean(): void; setSaving(value: boolean): void; setPublishing(value: boolean): void; setPreviewMode(value: boolean): void; undo(): void; redo(): void; canUndo(): boolean; canRedo(): boolean; reset(): void;
}
export const useBuilderStore: UseBoundStore<StoreApi<BuilderState>>;
export function generateSectionId(): string;
export function selectSections(state: BuilderState): BuilderSection[];
export function selectCurrentPage(state: BuilderState): string;
export function selectSelectedSectionId(state: BuilderState): string | null;
export function selectSelectedSection(state: BuilderState): BuilderSection | null;
export function selectIsDirty(state: BuilderState): boolean;
export const selectIsSaving: typeof selectIsDirty;
export const selectIsPublishing: typeof selectIsDirty;
export default useBuilderStore;
