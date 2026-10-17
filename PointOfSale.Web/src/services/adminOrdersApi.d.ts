import type { CancellationOutcome, ShipmentActionAuthority, ShipmentPresentation } from '../modules/shipping/shippingTypes';
export interface AdminOrderFilters {
    page?: number; pageSize?: number; source?: string; status?: string; paymentStatus?: string;
    search?: string; fromDate?: string; toDate?: string; customerId?: string; sortBy?: string; sortDirection?: string;
}
export interface AdminOrderAction { code: string; label: string; targetStatus?: string; requiredFields?: string[] }
export interface AdminOrderDetails {
    orderId: string; orderNumber: string; source?: string; status: string;
    items: unknown[]; payments: unknown[]; availableOrderActions?: AdminOrderAction[]; availableShipmentActions?: AdminOrderAction[];
    shipment?: ShipmentPresentation | null; totals?: { subtotal: number; shippingCost: number; total: number; currency?: string };
    rowVersion?: string; [key: string]: unknown;
}
export interface AdminOrderCommand { actionCode: string; reason?: string }
export function mapAdminOrdersError(error: unknown, fallback: string): Error & { status: number | null; code: string | null; traceId: string | null };
export function buildTenantHeaders(): { 'X-Tenant-Id': string; 'Cache-Control': string; Pragma: string };
export function buildOrderQueryParams(filters: AdminOrderFilters): URLSearchParams;
export function getAdminOrders(filters: AdminOrderFilters, signal?: AbortSignal): Promise<{ items: AdminOrderDetails[]; page: number; pageSize: number; totalCount: number; totalPages: number }>;
export function getAdminOrderDetails(id: string, signal?: AbortSignal): Promise<AdminOrderDetails>;
export function getAdminOrderCustomerTrust(id: string, signal?: AbortSignal): Promise<unknown>;
export function getAdminOrderSessionIntelligence(id: string, signal?: AbortSignal): Promise<unknown>;
export function runAdminOrderCustomerPhoneAction(id: string, action: string, reason?: string): Promise<unknown>;
export function confirmAdminReturnReceipt(id: string, payload: unknown): Promise<unknown>;
export function updateAdminOrderStatus(id: string, request: { newStatus: string; reason?: string; rowVersion: string }): Promise<unknown>;
export function getAdminOrderFulfillmentActions(id: string, signal?: AbortSignal): Promise<ShipmentActionAuthority>;
export function requireAdminOrderPaymentActionAuthority<T>(authority: T, expectedOrderId: string): T;
export function getAdminOrderPaymentActions(id: string, signal?: AbortSignal): Promise<unknown>;
export function runAdminOrderPaymentAction(id: string, action: string, payload?: unknown): Promise<unknown>;
export function runAdminOrderFulfillmentAction(id: string, action: string, payload?: unknown): Promise<unknown>;
export function cancelAdminOrder(id: string, reason: string): Promise<unknown>;
export function cancelAdminShipment(id: string, reason: string): Promise<CancellationOutcome>;
export function getAdminOrderCheckoutAttachmentBlob(id: string, attachmentId: string, signal?: AbortSignal): Promise<Blob>;
export function downloadAdminOrderCheckoutAttachment(id: string, attachmentId: string, fileName?: string): Promise<void>;
