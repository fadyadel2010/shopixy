import type { BulkProductItem, BulkProductResult } from '../services/bulkProductApi';
export const BULK_MUTATION_STATUSES: readonly string[];
export const BULK_MUTATION_ACTIONS: readonly string[];
export const BULK_MUTATION_LOCAL_ERROR_CODES: Readonly<Record<'INVALID_ACTION' | 'EMPTY_SELECTION' | 'MISSING_ROW_VERSION' | 'SELECTED_ROW_STALE' | 'REQUEST_ID_UNAVAILABLE' | 'REFRESH_FAILED', string>>;
export class BulkMutationValidationError extends Error { constructor(code: string, message: string, options?: { details?: unknown }); code: string; details: unknown }
export interface BulkMutationState { status: string; action: string | null; requestId: string | null; selectedItems: readonly BulkProductItem[]; result: Partial<BulkProductResult> | null; error: unknown; refreshError: unknown }
export function bulkMutationReducer(state: BulkMutationState, action: { type: string; action?: string | null; requestId?: string; selectedItems?: readonly BulkProductItem[]; outcome?: string; result?: Partial<BulkProductResult>; error?: unknown }): BulkMutationState;
export function useBulkProductMutation(options?: { products?: { id: string; rowVersion?: string | number | null }[]; selectedProductIds?: string[]; refreshProducts?(): unknown }): BulkMutationState & { isBulkMutating: boolean; startBulkMutation(action: string): boolean; resetBulkMutation(): boolean };
