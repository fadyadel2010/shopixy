/**
 * AI Commerce Assistant Types
 * Phase 1: Router-Ready, Storefront-Safe, Order-Aware
 */

/**
 * Chat message in the conversation
 */
export interface ChatMessage {
    id: string;
    role: 'user' | 'assistant';
    content: string;
    timestamp: Date;
    orderDraft?: OrderDraft;
    missingInfo?: string[];
    isFallback?: boolean;
}

/**
 * Order draft from AI assistant
 */
export interface OrderDraft {
    intent: 'none' | 'create' | 'update' | 'confirm';
    items: OrderDraftItem[];
    shippingMethodId?: string;
}

/**
 * Item in an order draft
 */
export interface OrderDraftItem {
    productId: string;
    variantId?: string;
    quantity: number;
    productName?: string;
    productImage?: string;
}

/**
 * Request to the chat endpoint
 */
export interface ChatRequest {
    message: string;
    sessionId?: string;
    productIdsInView?: string[];
    currentPage?: string;
    language?: string;
}

/**
 * Response from the chat endpoint
 */
export interface ChatResponse {
    type: 'message' | 'question' | 'order_draft';
    message: string;
    orderDraft?: OrderDraft;
    missingInfo?: string[];
}

/**
 * Chat status response
 */
export interface ChatStatus {
    enabled: boolean;
    available: boolean;
}

/**
 * Valid response types
 */
export const CHAT_RESPONSE_TYPES = {
    MESSAGE: 'message',
    QUESTION: 'question',
    ORDER_DRAFT: 'order_draft',
} as const;

/**
 * Valid order draft intents
 */
export const ORDER_DRAFT_INTENTS = {
    NONE: 'none',
    CREATE: 'create',
    UPDATE: 'update',
    CONFIRM: 'confirm',
} as const;

/**
 * Missing info types
 */
export const MISSING_INFO_TYPES = {
    QUANTITY: 'quantity',
    SHIPPING_METHOD: 'shipping_method',
    ADDRESS: 'address',
    VARIANT_SELECTION: 'variant_selection',
    PRODUCT_SELECTION: 'product_selection',
    CONTACT_INFO: 'contact_info',
} as const;
