import type { ApiJson } from './apiJson';
export interface AffiliateProductSettings { productId: string; isEnabled: boolean; isPolicyEnabled: boolean; commissionRate: number | null; maxCommissionPerItem: number | null; templateType: string; affiliateCategoryId: string | null }
declare const adminProductAffiliateApi: {
    listProducts(page?: number, pageSize?: number, params?: Record<string, unknown>): Promise<{ items: ApiJson[]; totalCount: number; page: number; pageSize: number; totalPages: number }>;
    getSettings(id: string): Promise<AffiliateProductSettings>; getCategoryTree(): Promise<ApiJson>;
    enableWithCategory(id: string, rate: number, categoryId: string, cap?: number | null): Promise<AffiliateProductSettings>;
    enable(id: string, rate: number, cap?: number | null): Promise<AffiliateProductSettings>; disable(id: string): Promise<AffiliateProductSettings>;
    updateCategory(id: string, categoryId: string | null): Promise<AffiliateProductSettings>; updateTemplate(id: string, templateType: string): Promise<AffiliateProductSettings>;
};
export default adminProductAffiliateApi;
