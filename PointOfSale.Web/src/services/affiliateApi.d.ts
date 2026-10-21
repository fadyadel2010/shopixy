import type { ApiJson } from './apiJson';
export const affiliateApi: {
    debugClaims(): Promise<ApiJson>; getDashboard(): Promise<ApiJson>; getOrders(page?: number, pageSize?: number, status?: string): Promise<ApiJson>;
    getEarningsSummary(): Promise<ApiJson>; getProfile(): Promise<ApiJson>; register(displayName: string): Promise<ApiJson>;
    updatePayoutWallet(walletNumber: string, walletType: string): Promise<ApiJson>;
    discoverProducts(options?: { q?: string; categoryId?: string; page?: number; pageSize?: number }): Promise<ApiJson>;
    discoverProductsIntelligence(options?: { search?: string; minCommissionRate?: number; inStockOnly?: boolean; storeTenantId?: string; categorySlug?: string; sort?: string; page?: number; pageSize?: number }): Promise<ApiJson>;
    getDiscoverCategories(storeTenantId?: string): Promise<ApiJson>; getProductIntelligence(id: string): Promise<ApiJson>;
    getAnalyticsSummary(range?: number): Promise<ApiJson>; getAnalyticsTrend(range?: number): Promise<ApiJson>;
    getAnalyticsTopProducts(range?: number, limit?: number): Promise<ApiJson>; getAnalyticsTopStores(range?: number, limit?: number): Promise<ApiJson>;
    getAnalyticsClicks(page?: number, pageSize?: number): Promise<ApiJson>; createPromotionLink(tenantId: string, productId: string): Promise<ApiJson>;
    listPromotionLinks(): Promise<ApiJson>; deactivatePromotionLink(id: string): Promise<ApiJson>; getPayouts(page?: number, pageSize?: number): Promise<ApiJson>; requestPayout(amount: number): Promise<ApiJson>;
};
export default affiliateApi;
