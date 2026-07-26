/**
 * API Error Response Type
 * Matches backend ApiErrorResponse
 */

export interface ApiErrorResponse {
    code: string;
    message: string;
    traceId?: string;
}
