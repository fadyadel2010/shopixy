import type { ApiJson } from './apiJson';
export const AFFILIATE_ADMIN_READ_HEADERS: { 'Cache-Control': string; Pragma: string };
declare const adminAffiliateApi: {
    listAffiliates(page?: number, pageSize?: number, status?: string, search?: string): Promise<ApiJson>;
    getAffiliateDetail(id: string): Promise<ApiJson>; getAffiliateOrders(id: string, page?: number, pageSize?: number, status?: string): Promise<ApiJson>;
    getAffiliateEarnings(id: string): Promise<ApiJson>; getAffiliateWithdrawals(id: string, status?: string, page?: number, pageSize?: number): Promise<ApiJson>;
    approveCommissions(id: string, orderIds?: string[], applyAll?: boolean): Promise<ApiJson>; recordPayment(id: string, orderIds?: string[], applyAll?: boolean): Promise<ApiJson>;
    suspendAffiliate(id: string, reason: string): Promise<ApiJson>; activateAffiliate(id: string): Promise<ApiJson>;
    listPayoutRequests(status?: string, page?: number, pageSize?: number): Promise<ApiJson>; approvePayoutRequest(id: string, notes?: string): Promise<ApiJson>;
    rejectPayoutRequest(id: string, notes?: string): Promise<ApiJson>; markPayoutPaid(id: string, reference?: string, notes?: string): Promise<ApiJson>;
};
export default adminAffiliateApi;
