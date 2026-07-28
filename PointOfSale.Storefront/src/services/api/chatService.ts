/**
 * AI Commerce Assistant API Service
 * Phase 1: Router-Ready, Storefront-Safe, Order-Aware
 */

import type { ChatRequest, ChatResponse, ChatStatus } from '../../types/chat';
import { httpClient } from './httpClient';

const API_BASE = '/api/storefront/chat';

/**
 * Send a message to the AI Commerce Assistant
 */
export async function sendMessage(request: ChatRequest): Promise<ChatResponse> {
    const response = await httpClient.post<ChatResponse>(API_BASE, request);
    return response.data;
}

/**
 * Check if the AI assistant is available
 */
export async function getChatStatus(): Promise<ChatStatus> {
    try {
        const response = await httpClient.get<ChatStatus>(`${API_BASE}/status`);
        return response.data;
    } catch {
        return { enabled: false, available: false };
    }
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
