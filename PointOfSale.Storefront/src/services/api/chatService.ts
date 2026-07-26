/**
 * AI Commerce Assistant API Service
 * Phase 1: Router-Ready, Storefront-Safe, Order-Aware
 */

import type { ChatRequest, ChatResponse, ChatStatus } from '../../types/chat';

const API_BASE = '/api/storefront/chat';

/**
 * Get the cart session ID from localStorage
 */
function getCartSessionId(): string | undefined {
    return localStorage.getItem('cart_session_id') || undefined;
}

/**
 * Send a message to the AI Commerce Assistant
 */
export async function sendMessage(request: ChatRequest): Promise<ChatResponse> {
    const response = await fetch(API_BASE, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'X-Cart-Session-Id': getCartSessionId() || '',
        },
        credentials: 'include',
        body: JSON.stringify(request),
    });

    if (!response.ok) {
        const error = await response.json().catch(() => ({}));
        throw new Error(error.message || error.error || 'Failed to send message');
    }

    return response.json();
}

/**
 * Check if the AI assistant is available
 */
export async function getChatStatus(): Promise<ChatStatus> {
    const response = await fetch(`${API_BASE}/status`, {
        method: 'GET',
        headers: {
            'Content-Type': 'application/json',
        },
    });

    if (!response.ok) {
        return { enabled: false, available: false };
    }

    return response.json();
}

/**
 * Helper to detect language from text
 */
export function detectLanguage(text: string): 'ar' | 'en' {
    // Simple Arabic detection based on character ranges
    const arabicPattern = /[\u0600-\u06FF\u0750-\u077F\u08A0-\u08FF]/;
    return arabicPattern.test(text) ? 'ar' : 'en';
}

/**
 * Generate a unique message ID
 */
export function generateMessageId(): string {
    return `msg_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
}
