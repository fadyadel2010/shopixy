import type { ApiJson } from './apiJson';
import type { AdminOrderDetails, getAdminOrders } from './adminOrdersApi';
export interface VendorActionAuthority { tenantId: string; storeId: string; subOrderId: string; lifecycleVersion: number; paymentReady: boolean; inventoryReady: boolean; availableActions: { code: string; targetStatus: string; requiresReason: boolean }[] }
export function requireCommerceHubVendorOrderListAuthority<T>(response: T, user: unknown): T;
export function requireCommerceHubVendorOrderAuthority<T>(order: T, user: unknown, subOrderId: string): T;
export function requireCommerceHubVendorReturnAuthority<T>(request: T, user: unknown, returnId: string): T;
export function getCommerceHubVendorReturnActions(request: unknown): { code: string; label: string; isFailure: boolean }[];
export function requireCommerceHubVendorActionAuthority(authority: unknown, user: unknown, subOrderId: string): VendorActionAuthority;
export function getCommerceHubVendorOrders(filters: Record<string, unknown>, signal?: AbortSignal): ReturnType<typeof getAdminOrders>;
export function getCommerceHubVendorOrder(id: string, signal?: AbortSignal): Promise<AdminOrderDetails>;
export function getCommerceHubVendorOrderActions(id: string, signal?: AbortSignal): Promise<VendorActionAuthority>;
export function runCommerceHubVendorOrderAction(id: string, code: string, reason?: string): Promise<ApiJson>;
export function runCommerceHubVendorReturnAction(id: string, returnId: string, code: string, payload?: { timestampUtc?: string; trackingNumber?: string; courierName?: string; notes?: string }): Promise<ApiJson>;
export { confirmAdminReturnReceipt as confirmCommerceHubVendorReturnReceipt } from './adminOrdersApi';
