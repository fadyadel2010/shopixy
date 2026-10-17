export function getRequestErrorMessage(error: unknown, fallbackMessage: string): string;
export function createLatestRequestGuard(): {
    beginRequest(): number; isLatestRequest(requestId: number): boolean; invalidate(): void;
};
export function useLatestRequestGuard(): Pick<ReturnType<typeof createLatestRequestGuard>, 'beginRequest' | 'isLatestRequest'>;
