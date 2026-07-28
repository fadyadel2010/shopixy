/**
 * Analytics Client
 * 
 * Network layer for sending analytics events to the backend.
 * 
 * RULES:
 * - All analytics network calls go through this ONE place
 * - Never throw in UI (errors are logged only)
 * - Never retry synchronously
 * - Respect backend rate limits
 * - Include required request-scoped headers (X-Tenant-Id, X-Session-Id, optional X-Customer-Id)
 * 
 * Endpoints:
 * - POST /api/storefront/analytics/event - Single event
 * - POST /api/storefront/analytics/events - Batch events
 */

import type { AnalyticsEvent, AnalyticsEventResponse, AnalyticsEventBatchResponse } from './eventTypes';
import { getRequestHeaders, isRequestContextReady } from '@/bootstrap';

const ANALYTICS_BASE_URL = import.meta.env.VITE_API_URL || '';
const ANALYTICS_ENDPOINT = '/api/storefront/analytics';
const REQUEST_TIMEOUT_MS = 10000;

function assertAnalyticsContext(event: AnalyticsEvent, headers: ReturnType<typeof getRequestHeaders>): void {
    if (!event.context.tenantId || event.context.tenantId.toLowerCase() !== headers['X-Tenant-Id'].toLowerCase()) {
        throw new Error('[AnalyticsClient] Event tenant context does not match request tenant.');
    }

    if (!event.actor.sessionId || event.actor.sessionId !== headers['X-Session-Id']) {
        throw new Error('[AnalyticsClient] Event session context does not match request session.');
    }
}

/**
 * Send a single analytics event
 * 
 * @param event - The event to send
 * @returns Response from the server
 * @throws Error if the request fails
 */
export async function sendAnalyticsEvent(event: AnalyticsEvent): Promise<AnalyticsEventResponse> {
    if (!isRequestContextReady()) {
        throw new Error('[AnalyticsClient] Cannot send event - tenant not resolved');
    }

    const headers = getRequestHeaders();
    assertAnalyticsContext(event, headers);
    const url = `${ANALYTICS_BASE_URL}${ANALYTICS_ENDPOINT}/event`;

    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

    try {
        const response = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-Tenant-Id': headers['X-Tenant-Id'],
                'X-Session-Id': headers['X-Session-Id'],
                ...(headers['X-Customer-Id'] ? { 'X-Customer-Id': headers['X-Customer-Id'] } : {}),
            },
            body: JSON.stringify(event),
            signal: controller.signal,
            credentials: 'include',
        });

        clearTimeout(timeoutId);

        if (response.status === 429) {
            throw new Error('[AnalyticsClient] Rate limited');
        }

        if (!response.ok && response.status !== 202) {
            const errorData = await response.json().catch(() => ({}));
            throw new Error(`[AnalyticsClient] Request failed: ${response.status} ${errorData.error || ''}`);
        }

        const data: AnalyticsEventResponse = await response.json();

        if (import.meta.env.DEV) {
            console.log('[AnalyticsClient] Event sent:', event.type, data.accepted ? '✓' : '✗');
        }

        return data;
    } catch (error) {
        clearTimeout(timeoutId);

        if (error instanceof Error && error.name === 'AbortError') {
            throw new Error('[AnalyticsClient] Request timeout');
        }

        throw error;
    }
}

/**
 * Send a batch of analytics events
 * 
 * @param events - Array of events to send
 * @returns Batch response from the server
 * @throws Error if the request fails
 */
export async function sendAnalyticsBatch(events: AnalyticsEvent[]): Promise<AnalyticsEventBatchResponse> {
    if (!isRequestContextReady()) {
        throw new Error('[AnalyticsClient] Cannot send batch - tenant not resolved');
    }

    if (events.length === 0) {
        return {
            accepted: 0,
            rejected: 0,
            acceptedEventIds: [],
            errors: [],
        };
    }

    const headers = getRequestHeaders();
    events.forEach((event) => assertAnalyticsContext(event, headers));
    const url = `${ANALYTICS_BASE_URL}${ANALYTICS_ENDPOINT}/events`;

    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

    try {
        const response = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-Tenant-Id': headers['X-Tenant-Id'],
                'X-Session-Id': headers['X-Session-Id'],
                ...(headers['X-Customer-Id'] ? { 'X-Customer-Id': headers['X-Customer-Id'] } : {}),
            },
            body: JSON.stringify({ events }),
            signal: controller.signal,
            credentials: 'include',
        });

        clearTimeout(timeoutId);

        if (response.status === 429) {
            throw new Error('[AnalyticsClient] Rate limited');
        }

        if (!response.ok && response.status !== 202) {
            await response.json().catch(() => ({}));
            throw new Error(`[AnalyticsClient] Batch request failed: ${response.status}`);
        }

        const data: AnalyticsEventBatchResponse = await response.json();

        if (import.meta.env.DEV) {
            console.log('[AnalyticsClient] Batch sent:', {
                total: events.length,
                accepted: data.accepted,
                rejected: data.rejected,
            });
        }

        return data;
    } catch (error) {
        clearTimeout(timeoutId);

        if (error instanceof Error && error.name === 'AbortError') {
            throw new Error('[AnalyticsClient] Batch request timeout');
        }

        throw error;
    }
}

/**
 * Check if analytics endpoint is reachable
 */
export async function checkAnalyticsHealth(): Promise<boolean> {
    try {
        const url = `${ANALYTICS_BASE_URL}${ANALYTICS_ENDPOINT}/health`;
        const response = await fetch(url, {
            method: 'GET',
            headers: {
                'Content-Type': 'application/json',
            },
        });

        return response.ok;
    } catch {
        return false;
    }
}
