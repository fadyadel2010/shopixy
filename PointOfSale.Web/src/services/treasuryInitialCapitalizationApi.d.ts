import type { ApiJson } from './apiJson';
export const TREASURY_CAPITALIZATION_DECLARATION: 'TREASURY_CUTOVER_FUNDS_V1';
export function createCapitalizationOperationId(): string;
export interface CapitalizationAttempt { fingerprint: string; operationId: string }
export function resolveCapitalizationAttempt(previousAttempt: CapitalizationAttempt | null | undefined, fingerprint: string): CapitalizationAttempt;
export function getCapitalizationErrorMessage(error: unknown, t: (key: string) => string, fallbackKey?: string): string;
export const treasuryInitialCapitalizationApi: {
    listDestinations(options?: { signal?: AbortSignal }): Promise<ApiJson[]>; list(options?: { signal?: AbortSignal }): Promise<ApiJson[]>;
    create(data: ApiJson): Promise<ApiJson>; reverse(id: string, data: ApiJson): Promise<ApiJson>;
};
export default treasuryInitialCapitalizationApi;
