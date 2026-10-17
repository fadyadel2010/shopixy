import type { ApiJson } from '../../services/apiJson';
export const marketplaceCmsService: {
    listPages(): Promise<ApiJson>; getPage(id: string): Promise<ApiJson>; createPage(data: unknown): Promise<ApiJson>;
    updatePage(id: string, data: unknown, expectedVersion: unknown): Promise<ApiJson>; publishPage(id: string, expectedVersion: unknown): Promise<ApiJson>;
    previewPage(id: string): Promise<ApiJson>; deletePage(id: string, expectedVersion: unknown): Promise<ApiJson>;
    getPageVersion(page: unknown): number | null; getListItemVersion(page: unknown): number | null; isConcurrencyConflict(error: unknown): boolean;
    translateContent(content: string, from?: string, to?: string): Promise<string>;
};
export default marketplaceCmsService;
