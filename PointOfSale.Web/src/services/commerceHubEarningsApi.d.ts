import type { ApiJson } from './apiJson';
export const COMMERCEHUB_PAYOUT_METHOD_TYPES: string[];
export function getCommerceHubEarningsDashboard(): Promise<ApiJson>;
export function getVendorPayoutMethods(): Promise<ApiJson>;
export function createVendorPayoutMethod(payload: { methodType: string; accountName?: string; accountNumber?: string; bankName?: string; walletNumber?: string; isDefault?: boolean }): Promise<ApiJson>;
