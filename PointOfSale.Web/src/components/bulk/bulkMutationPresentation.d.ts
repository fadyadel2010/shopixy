import type { BulkProductResult } from '../../services/bulkProductApi';
export type BulkTone = 'success' | 'info' | 'warning' | 'error';
export const BULK_MUTATION_TONES: Readonly<Record<'SUCCESS' | 'INFO' | 'WARNING' | 'ERROR', BulkTone>>;
export interface BulkPresentationInput {
    action: string | null; isMutating?: boolean; processingCount?: number;
    result?: Partial<Omit<BulkProductResult, 'results'>> & { results?: { productId: string; status: string; code?: string | null; message?: string }[] } | null;
    error?: unknown; refreshError?: unknown; resolveProductName?(id: string): string | undefined;
}
export interface BulkActionPresentation { actionLabelKey: string; processingKey: string; successTitleKey: string; processedCountKey: string; unchangedCountKey: string; refreshErrorKey: string; unchangedCodes: Readonly<Record<string, string>> }
export function resolveBulkActionPresentation(action: string): BulkActionPresentation;
export function resolveBulkItemReasonKey(input: { action: string; code?: string | null }): string | null;
export function buildBulkMutationPresentation(input: BulkPresentationInput & { t(key: string, params?: Record<string, unknown>): string }): null | { kind: 'processing'; processingText: string } | {
    kind: 'result' | 'requestError'; tone: BulkTone; toneClass: string; heading: string;
    counts: { requested: number; accepted: number; processed: number; unchanged: number; failed: number } | null;
    countLines: { key: string; text: string }[]; items: { key: string; namePrefix: string | null; text: string; statusLabel: string; statusClass: string }[];
    requestMessage: string | null; refreshWarning: string | null;
};
