import type { HttpRequestOptions } from '../../../core/httpContracts';
import type { ApiJson } from '../../../services/apiJson';
export const storeConfigService: {
    getStoreConfig(options?: HttpRequestOptions): Promise<ApiJson>; getRuntimeStoreConfig(options?: HttpRequestOptions): Promise<ApiJson>;
    updateStoreConfig(config: unknown, options?: HttpRequestOptions): Promise<ApiJson>; updateStorefrontStatus(config: unknown, options?: HttpRequestOptions): Promise<ApiJson>;
    updatePageLayout(id: string, config: unknown, options?: HttpRequestOptions): Promise<ApiJson>; publishPage(id: string, options?: HttpRequestOptions): Promise<ApiJson>;
    deleteProductTemplate(id: string, options?: HttpRequestOptions): Promise<ApiJson>; getThemePresets(): Promise<ApiJson>; selectThemePreset(key: string, options?: HttpRequestOptions): Promise<ApiJson>;
    updateThemeOverrides(overrides: unknown, options?: HttpRequestOptions): Promise<ApiJson>; activateTheme(id: string, options?: HttpRequestOptions): Promise<ApiJson>; publishTheme(options?: HttpRequestOptions): Promise<ApiJson>;
    createThemePreviewLink(id: string): Promise<{ previewUrl: string; previewToken: string; expiresAtUtc: string }>;
    getPreviewConfig(options?: HttpRequestOptions): Promise<ApiJson>; getVersionHistory(options?: HttpRequestOptions): Promise<ApiJson>; rollback(version: number, request: unknown, options?: HttpRequestOptions): Promise<ApiJson>;
};
export default storeConfigService;
