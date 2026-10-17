export const POS_RUNTIME_CACHE_TTL_MS: number;
export const POS_RUNTIME_CACHE_MAX_ENTRIES: number;
export const POS_RUNTIME_CACHE_MAX_BYTES: number;
export function readPosRuntimeCache<T>(key: string, loader: () => T | Promise<T>, options?: {
    ttlMs?: number; force?: boolean; maxEntries?: number; maxBytes?: number;
}): Promise<T>;
export function getPosRuntimeCacheStats(): { entries: number; requestSequences: number; activeRequests: number; approxBytes: number; generation: number };
export interface CacheInvalidation { reason: string; timestamp: number; token: string }
export function invalidatePosRuntimeCache(options?: { reason?: string; broadcast?: boolean }): CacheInvalidation;
export function subscribeToPosRuntimeInvalidation(callback?: (payload: CacheInvalidation) => void): () => void;
