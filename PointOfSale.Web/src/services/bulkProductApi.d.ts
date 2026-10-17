export interface BulkProductItem { productId: string; expectedVersion: string }
export interface BulkProductResultItem {
    productId: string; status: 'processed' | 'unchanged' | 'failed'; code: string | null; message: string | null;
    previousVersion: string | null; newVersion: string | null;
}
export interface BulkProductResult {
    requested: number; accepted: number; processed: number; unchanged: number; failed: number;
    correlationId: string; results: BulkProductResultItem[];
}
export type SafeErrorDetail = null | string | number | boolean | SafeErrorDetail[] | { [key: string]: SafeErrorDetail };
export class BulkProductApiError extends Error {
    constructor(message: string, options?: { status?: number | null; code?: string | number | null; correlationId?: string | null; details?: SafeErrorDetail; cause?: unknown });
    status: number | null; code: string | number | null; correlationId: string | null; details: SafeErrorDetail; cause: unknown;
}
export function toBulkProductItem(product: unknown): BulkProductItem;
export function bulkActivate(items: unknown, idempotencyKey?: unknown): Promise<BulkProductResult>;
export const bulkDeactivate: typeof bulkActivate;
export const bulkPublish: typeof bulkActivate;
export const bulkUnpublish: typeof bulkActivate;
export const bulkDelete: typeof bulkActivate;
declare const bulkProductApi: { bulkActivate: typeof bulkActivate; bulkDeactivate: typeof bulkActivate; bulkPublish: typeof bulkActivate; bulkUnpublish: typeof bulkActivate; bulkDelete: typeof bulkActivate; toBulkProductItem: typeof toBulkProductItem };
export default bulkProductApi;
