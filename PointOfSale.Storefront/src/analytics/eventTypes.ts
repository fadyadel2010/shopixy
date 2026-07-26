/**
 * Analytics Event Types
 * 
 * POS-native event types that mirror the backend AnalyticsEventType enum.
 * These events have BUSINESS VALUE - no GA/Meta style events.
 * 
 * RULE: Every event type must have a clear business purpose.
 * NO PROVIDER-SPECIFIC EVENTS ALLOWED.
 */

export enum AnalyticsEventType {
    /** User viewed a page on the storefront */
    PageView = 'PageView',

    /** Product was displayed in a list (search, category, homepage) */
    ProductImpression = 'ProductImpression',

    /** User viewed a product detail page */
    ProductView = 'ProductView',

    /** User performed a search */
    SearchPerformed = 'SearchPerformed',

    /** User applied a filter on product listing */
    FilterApplied = 'FilterApplied',

    /** User added a product to cart */
    AddToCart = 'AddToCart',

    /** User removed a product from cart */
    RemoveFromCart = 'RemoveFromCart',

    /** User started the checkout process */
    BeginCheckout = 'BeginCheckout',

    /** User completed a purchase */
    PurchaseCompleted = 'PurchaseCompleted',

    /** Payment failed during checkout */
    PaymentFailed = 'PaymentFailed',

    /** Backend payment succeeded but post-success cleanup failed */
    CheckoutFinalizeFailure = 'CheckoutFinalizeFailure',

    /** Auth verification degraded after a transient failure */
    AuthStateDegraded = 'AuthStateDegraded',

    /** Stored checkout selection no longer matches the latest configuration */
    CheckoutSelectionInvalidated = 'CheckoutSelectionInvalidated',

    /** Cart aggregate state was detected to be inconsistent */
    CartStateMismatchDetected = 'CartStateMismatchDetected',

    /** Guest payment recovery succeeded after a refresh using persisted checkout context */
    GuestPaymentRecoverySuccess = 'GuestPaymentRecoverySuccess',

    /** A paid-order finalization attempt was prevented from clearing an unrelated cart */
    CartUnexpectedClearBlocked = 'CartUnexpectedClearBlocked',

    /** A legacy inbound tracking token was scrubbed from the browser URL */
    TrackingTokenRemovedFromUrl = 'TrackingTokenRemovedFromUrl',

    /** Auth recovered from an explicit degraded state */
    AuthDegradedRecovery = 'AuthDegradedRecovery',

    /** Checkout state was cleared to prevent cross-user reuse */
    CheckoutStateLeakPrevented = 'CheckoutStateLeakPrevented',
}

/**
 * Analytics Actor - WHO performed the event
 * 
 * NOTE: CustomerId is NOT sent from frontend.
 * Backend resolves it from JWT token.
 */
export interface AnalyticsActor {
    sessionId: string;
}

/**
 * Analytics Context - WHERE and HOW the event occurred
 * 
 * NOTE: IpAddress and UserAgent are NOT sent from frontend.
 * Backend captures them server-side for security.
 */
export interface AnalyticsContext {
    pageUrl: string;
    referrer?: string;
    language?: string;
    screenWidth?: number;
    screenHeight?: number;
}

/**
 * Analytics Payload - WHAT exactly happened
 * 
 * Different event types require different payload fields.
 * See eventTypes.ts for which fields are required per event.
 */
export interface AnalyticsPayload {
    productId?: string;
    variantId?: string;
    orderId?: string;
    categoryId?: string;
    value?: number;
    quantity?: number;
    searchQuery?: string;
    filters?: Record<string, string>;
    productName?: string;
    productSku?: string;
    extra?: Record<string, unknown>;
}

/**
 * Complete Analytics Event
 * 
 * This is the canonical event structure sent to the backend.
 * The backend's AnalyticsEventValidator will:
 * - Validate all fields
 * - Enrich with server-side data (IP, UserAgent)
 * - Resolve CustomerId from JWT
 */
export interface AnalyticsEvent {
    eventId: string;
    type: AnalyticsEventType;
    occurredAtUtc?: string;
    actor: AnalyticsActor;
    context: AnalyticsContext;
    payload?: AnalyticsPayload;
}

/**
 * Backend response for single event
 */
export interface AnalyticsEventResponse {
    accepted: boolean;
    eventId: string;
    error?: string;
}

/**
 * Backend response for batch events
 */
export interface AnalyticsEventBatchResponse {
    accepted: number;
    rejected: number;
    acceptedEventIds: string[];
    errors: Array<{
        index: number;
        eventId?: string;
        error: string;
    }>;
}
