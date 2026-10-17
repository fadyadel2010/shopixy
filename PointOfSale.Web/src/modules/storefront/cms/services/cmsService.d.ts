import type { HttpRequestOptions } from '../../../../core/httpContracts';
import type { ApiJson } from '../../../../services/apiJson';
import type { NavigationSnapshot, admitNavigationSnapshot, admitNavigationTargets } from './navigationAuthoringContract';
export function extractCmsConflict(error: unknown): { code: string; message: string; latestVersion: unknown } | null;
export const cmsService: {
    listPages(): Promise<ApiJson>; getPage(id: string): Promise<ApiJson>; getVersionHistory(id: string): Promise<ApiJson>;
    getPageBySlug(slug: string): Promise<ApiJson>; createPage(data: unknown): Promise<ApiJson>; updatePage(id: string, data: unknown): Promise<ApiJson>;
    publishPage(id: string, expectedVersion: string | number): Promise<ApiJson>; rollbackPage(id: string, version: number, expectedVersion: string | number, reason?: string): Promise<ApiJson>;
    previewPage(id: string): Promise<ApiJson>; createPreviewLink(id: string): Promise<{ slug: string; previewToken: string; previewUrl: string; expiresAtUtc: string }>;
    deletePage(id: string, expectedVersion: string | number): Promise<void>; seedDefaultPages(): Promise<ApiJson>;
    getNavigation(options?: HttpRequestOptions): Promise<ReturnType<typeof admitNavigationSnapshot>>;
    getNavigationTargets(options?: HttpRequestOptions): Promise<ReturnType<typeof admitNavigationTargets>>;
    updateNavigation(snapshot: NavigationSnapshot, options?: HttpRequestOptions): Promise<ReturnType<typeof admitNavigationSnapshot>>;
    publishNavigation(rowVersion: string, options?: HttpRequestOptions): Promise<ReturnType<typeof admitNavigationSnapshot>>;
};
export default cmsService;
