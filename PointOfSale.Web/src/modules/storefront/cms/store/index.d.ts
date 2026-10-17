import type { StoreApi, UseBoundStore } from 'zustand';
import type { NavigationLink, NavigationSnapshot } from '../services/navigationAuthoringContract';
export interface CmsStoreState { pages: unknown[]; currentPage: unknown; isLoadingPages: boolean; editingPage: unknown; isDirty: boolean; isSaving: boolean; isPublishing: boolean; navigation: NavigationSnapshot | null; isLoadingNavigation: boolean; isNavigationDirty: boolean; isNavigationSaving: boolean; isNavigationPublishing: boolean; error: unknown;
    setPages(pages: unknown[]): void; setCurrentPage(page: unknown): void; setLoadingPages(value: boolean): void; setEditingPage(page: unknown): void; updateEditingPage(updates: unknown): void; updateEditingPageSeo(updates: unknown): void;
    setNavigation(value: NavigationSnapshot | null): void; setLoadingNavigation(value: boolean): void; updateNavigationHeader(links: NavigationLink[]): void; updateNavigationFooter(links: NavigationLink[]): void;
    addNavigationLink(location: string, link: NavigationLink): void; removeNavigationLink(location: string, index: number): void; reorderNavigationLinks(location: string, oldIndex: number, newIndex: number): void;
    setNavigationDirty(value: boolean): void; setNavigationSaving(value: boolean): void; setNavigationPublishing(value: boolean): void; markDirty(): void; markClean(): void; setSaving(value: boolean): void; setPublishing(value: boolean): void; setError(error: unknown): void; clearError(): void; reset(): void;
}
export const useCmsStore: UseBoundStore<StoreApi<CmsStoreState>>;
export default useCmsStore;
export function selectPages(state: CmsStoreState): unknown[];
export function selectEditingPage(state: CmsStoreState): unknown;
export function selectNavigation(state: CmsStoreState): NavigationSnapshot | null;
export function selectIsDirty(state: CmsStoreState): boolean;
