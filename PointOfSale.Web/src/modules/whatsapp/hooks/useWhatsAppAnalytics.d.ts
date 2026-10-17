export function useWhatsAppAnalytics(): {
    loading: boolean;
    analyticsError: string | null;
    logsError: string | null;
    error: string | null;
    getAnalytics(from?: string, to?: string): Promise<Record<string, unknown>>;
    getLogs(filters?: { type?: string; status?: string }, page?: number, pageSize?: number): Promise<Record<string, unknown>>;
};
