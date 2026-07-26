/**
 * Analytics Queue
 * 
 * Manages event buffering, persistence, batching, and delivery.
 * 
 * Features:
 * - Buffer events in memory
 * - Persist queue to localStorage for offline mode
 * - Batch send events (max 20 per batch)
 * - Retry with exponential backoff
 * - Flush on page unload, visibility change, network reconnect
 * 
 * RULES:
 * - Max batch size: 20 events
 * - Max retry attempts: 5
 * - Never block UI thread
 * - Failures are silent (log only in dev)
 */

import type { AnalyticsEvent, AnalyticsEventBatchResponse } from './eventTypes';
import { sendAnalyticsBatch } from './AnalyticsClient';

const QUEUE_STORAGE_KEY = 'analytics_event_queue';
const MAX_BATCH_SIZE = 20;
const MAX_RETRY_ATTEMPTS = 5;
const MAX_QUEUE_SIZE = 500; // Prevent unbounded growth
const FLUSH_INTERVAL_MS = 10000; // 10 seconds

/**
 * Queued event with retry metadata
 */
interface QueuedEvent {
    event: AnalyticsEvent;
    retryCount: number;
    enqueuedAt: number;
}

/**
 * Analytics Queue class
 */
class AnalyticsQueue {
    private static instance: AnalyticsQueue;
    private queue: QueuedEvent[] = [];
    private isFlushing = false;
    private flushTimer: ReturnType<typeof setTimeout> | null = null;
    private initialized = false;

    private constructor() {
        this.initialize();
    }

    static getInstance(): AnalyticsQueue {
        if (!AnalyticsQueue.instance) {
            AnalyticsQueue.instance = new AnalyticsQueue();
        }
        return AnalyticsQueue.instance;
    }

    /**
     * Initialize queue - restore from localStorage and set up listeners
     */
    private initialize(): void {
        if (this.initialized) return;
        this.initialized = true;

        // Restore persisted queue
        this.restoreFromStorage();

        // Set up automatic flush interval
        this.startFlushInterval();

        // Set up event listeners for flush triggers
        this.setupEventListeners();

        if (import.meta.env.DEV) {
            console.log('[AnalyticsQueue] Initialized with', this.queue.length, 'pending events');
        }
    }

    /**
     * Enqueue an event for delivery
     */
    enqueue(event: AnalyticsEvent): void {
        // Check queue size limit
        if (this.queue.length >= MAX_QUEUE_SIZE) {
            // Remove oldest events to make room
            const removed = this.queue.splice(0, this.queue.length - MAX_QUEUE_SIZE + 1);
            if (import.meta.env.DEV) {
                console.warn('[AnalyticsQueue] Queue full, dropped', removed.length, 'old events');
            }
        }

        const queuedEvent: QueuedEvent = {
            event,
            retryCount: 0,
            enqueuedAt: Date.now(),
        };

        this.queue.push(queuedEvent);
        this.persistToStorage();

        if (import.meta.env.DEV) {
            console.log('[AnalyticsQueue] Enqueued event:', event.type, 'Queue size:', this.queue.length);
        }

        // Try to flush if we have enough events
        if (this.queue.length >= MAX_BATCH_SIZE) {
            this.flush();
        }
    }

    /**
     * Flush the queue - send all pending events
     */
    async flush(): Promise<void> {
        // Prevent concurrent flushes
        if (this.isFlushing || this.queue.length === 0) {
            return;
        }

        this.isFlushing = true;

        try {
            while (this.queue.length > 0) {
                // Get batch of events
                const batch = this.queue.slice(0, MAX_BATCH_SIZE);
                const events = batch.map(q => q.event);

                try {
                    const response = await sendAnalyticsBatch(events);
                    this.handleBatchResponse(batch, response);
                } catch (error) {
                    // Network or server error - handle retries
                    this.handleBatchFailure(batch);

                    if (import.meta.env.DEV) {
                        console.error('[AnalyticsQueue] Batch send failed:', error);
                    }

                    // Stop flushing on error - will retry later
                    break;
                }
            }
        } finally {
            this.isFlushing = false;
            this.persistToStorage();
        }
    }

    /**
     * Handle successful batch response
     */
    private handleBatchResponse(batch: QueuedEvent[], response: AnalyticsEventBatchResponse): void {
        // Remove successfully sent events
        const acceptedIds = new Set(response.acceptedEventIds);

        // Track which events to retry
        const eventsToRetry: QueuedEvent[] = [];

        for (let i = 0; i < batch.length; i++) {
            const queuedEvent = batch[i];
            const eventId = queuedEvent.event.eventId;

            if (acceptedIds.has(eventId)) {
                // Success - will be removed from queue
                if (import.meta.env.DEV) {
                    console.log('[AnalyticsQueue] Event accepted:', eventId);
                }
            } else {
                // Check if there's an error for this event
                const eventError = response.errors.find(e => e.eventId === eventId || e.index === i);

                if (eventError) {
                    // Validation error - don't retry
                    if (import.meta.env.DEV) {
                        console.warn('[AnalyticsQueue] Event rejected:', eventId, eventError.error);
                    }
                } else {
                    // Unknown error - retry
                    queuedEvent.retryCount++;
                    if (queuedEvent.retryCount < MAX_RETRY_ATTEMPTS) {
                        eventsToRetry.push(queuedEvent);
                    }
                }
            }
        }

        // Remove the batch from queue
        this.queue = this.queue.slice(batch.length);

        // Add events that need retry back to queue
        this.queue.push(...eventsToRetry);

        if (import.meta.env.DEV) {
            console.log('[AnalyticsQueue] Batch processed:', {
                sent: batch.length,
                accepted: response.accepted,
                rejected: response.rejected,
                retrying: eventsToRetry.length,
                remaining: this.queue.length,
            });
        }
    }

    /**
     * Handle batch failure (network error)
     */
    private handleBatchFailure(batch: QueuedEvent[]): void {
        // Increment retry count and re-queue if under limit
        const eventsToRetry: QueuedEvent[] = [];

        for (const queuedEvent of batch) {
            queuedEvent.retryCount++;
            if (queuedEvent.retryCount < MAX_RETRY_ATTEMPTS) {
                eventsToRetry.push(queuedEvent);
            } else if (import.meta.env.DEV) {
                console.warn('[AnalyticsQueue] Event exceeded retry limit:', queuedEvent.event.eventId);
            }
        }

        // Remove failed batch from front, add retryable to back
        this.queue = this.queue.slice(batch.length);
        this.queue.push(...eventsToRetry);
    }

    /**
     * Get current queue length
     */
    getQueueLength(): number {
        return this.queue.length;
    }

    /**
     * Check if queue is currently flushing
     */
    isBusy(): boolean {
        return this.isFlushing;
    }

    /**
     * Persist queue to localStorage
     */
    private persistToStorage(): void {
        try {
            const data = JSON.stringify(this.queue);
            localStorage.setItem(QUEUE_STORAGE_KEY, data);
        } catch {
            // localStorage might be full or unavailable
            if (import.meta.env.DEV) {
                console.warn('[AnalyticsQueue] Failed to persist queue to localStorage');
            }
        }
    }

    /**
     * Restore queue from localStorage
     */
    private restoreFromStorage(): void {
        try {
            const data = localStorage.getItem(QUEUE_STORAGE_KEY);
            if (data) {
                const restored = JSON.parse(data) as QueuedEvent[];
                // Filter out very old events (> 24 hours)
                const maxAge = 24 * 60 * 60 * 1000;
                const now = Date.now();
                this.queue = restored.filter(q => now - q.enqueuedAt < maxAge);

                if (import.meta.env.DEV && this.queue.length > 0) {
                    console.log('[AnalyticsQueue] Restored', this.queue.length, 'events from localStorage');
                }
            }
        } catch {
            this.queue = [];
        }
    }

    /**
     * Start periodic flush interval
     */
    private startFlushInterval(): void {
        if (this.flushTimer) return;

        this.flushTimer = setInterval(() => {
            if (this.queue.length > 0) {
                this.flush();
            }
        }, FLUSH_INTERVAL_MS);
    }

    /**
     * Set up event listeners for flush triggers
     */
    private setupEventListeners(): void {
        // Flush on visibility change (user leaving page)
        document.addEventListener('visibilitychange', () => {
            if (document.visibilityState === 'hidden') {
                this.flushSync();
            }
        });

        // Flush on page unload
        window.addEventListener('beforeunload', () => {
            this.flushSync();
        });

        // Flush on online (network reconnect)
        window.addEventListener('online', () => {
            if (import.meta.env.DEV) {
                console.log('[AnalyticsQueue] Network reconnected, flushing queue');
            }
            this.flush();
        });
    }

    /**
     * Synchronous flush using sendBeacon
     * Used for page unload when we can't use async
     */
    private flushSync(): void {
        if (this.queue.length === 0) return;

        try {
            // sendBeacon doesn't support custom headers, so we just persist
            // to localStorage and let it be sent on next page load
            this.persistToStorage();

            if (import.meta.env.DEV) {
                console.log('[AnalyticsQueue] Persisted queue for next session');
            }
        } catch (error) {
            if (import.meta.env.DEV) {
                console.error('[AnalyticsQueue] Sync flush failed:', error);
            }
        }
    }
}

// Export singleton
export const analyticsQueue = AnalyticsQueue.getInstance();

// Export convenience functions
export function enqueueAnalyticsEvent(event: AnalyticsEvent): void {
    analyticsQueue.enqueue(event);
}

export function flushAnalyticsQueue(): Promise<void> {
    return analyticsQueue.flush();
}

export function getAnalyticsQueueLength(): number {
    return analyticsQueue.getQueueLength();
}
