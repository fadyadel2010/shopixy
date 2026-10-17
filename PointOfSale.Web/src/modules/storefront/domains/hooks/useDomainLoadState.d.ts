export const LOAD_STATES: { IDLE: 'idle'; LOADING: 'loading'; REFRESHING: 'refreshing'; SUCCESS: 'success'; EMPTY: 'empty'; ERROR: 'error'; UNAUTHORIZED: 'unauthorized'; NOT_FOUND: 'not-found' };
export type DomainLoadState = typeof LOAD_STATES[keyof typeof LOAD_STATES];
export function classifyError(error: unknown): DomainLoadState;
export function getDomainErrorMessage(error: unknown, fallback?: string): string;
