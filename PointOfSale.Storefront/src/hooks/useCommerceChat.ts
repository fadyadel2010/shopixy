/**
 * AI Commerce Assistant Hook
 * Phase 1: Router-Ready, Storefront-Safe, Order-Aware
 * 
 * Manages chat state, message history, and communication with the backend.
 */

import { useState, useCallback, useEffect, useRef } from 'react';
import type { ChatMessage, ChatRequest, OrderDraft } from '../types/chat';
import {
    sendMessage,
    getChatStatus,
    detectLanguage,
    generateMessageId,
} from '../services/api/chatService';
import { tryGetTenantScopedStorageKey } from '@/bootstrap/tenantScopedStorage';

interface UseChatOptions {
    /** Maximum messages to keep in history */
    maxHistory?: number;
    /** Current page context (e.g., "product:123") */
    currentPage?: string;
    /** Product IDs currently visible to the user */
    productIdsInView?: string[];
    /** Auto-check status on mount */
    checkStatusOnMount?: boolean;
}

interface UseChatReturn {
    /** Chat messages history */
    messages: ChatMessage[];
    /** Whether the assistant is available */
    isAvailable: boolean;
    /** Whether a message is being processed */
    isLoading: boolean;
    /** Error message if any */
    error: string | null;
    /** Send a message to the assistant */
    sendUserMessage: (content: string) => Promise<void>;
    /** Clear the chat history */
    clearHistory: () => void;
    /** Current order draft from the assistant */
    currentDraft: OrderDraft | null;
    /** Accept the current draft (navigate to checkout) */
    acceptDraft: () => void;
    /** Dismiss the current draft */
    dismissDraft: () => void;
}

const CHAT_STORAGE_KEY = 'commerce_ai_chat_history';
const SESSION_ID_KEY = 'commerce_ai_session_id';

/**
 * Generate or retrieve a persistent session ID
 */
function getOrCreateSessionId(): string {
    const storageKey = tryGetTenantScopedStorageKey(SESSION_ID_KEY);
    if (!storageKey) {
        return `session_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
    }

    let sessionId = sessionStorage.getItem(storageKey);
    if (!sessionId) {
        sessionId = `session_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
        sessionStorage.setItem(storageKey, sessionId);
    }
    return sessionId;
}

/**
 * Load chat history from localStorage
 */
function loadHistory(): ChatMessage[] {
    try {
        const storageKey = tryGetTenantScopedStorageKey(CHAT_STORAGE_KEY);
        const stored = storageKey ? localStorage.getItem(storageKey) : null;
        if (stored) {
            const parsed = JSON.parse(stored);
            return parsed.map((msg: any) => ({
                ...msg,
                timestamp: new Date(msg.timestamp),
            }));
        }
    } catch {
        // Ignore parse errors
    }
    return [];
}

/**
 * Save chat history to localStorage
 */
function saveHistory(messages: ChatMessage[], maxHistory: number): void {
    try {
        const storageKey = tryGetTenantScopedStorageKey(CHAT_STORAGE_KEY);
        if (!storageKey) {
            return;
        }

        const toSave = messages.slice(-maxHistory);
        localStorage.setItem(storageKey, JSON.stringify(toSave));
    } catch {
        // Ignore storage errors
    }
}

export function useCommerceChat(options: UseChatOptions = {}): UseChatReturn {
    const {
        maxHistory = 50,
        currentPage,
        productIdsInView,
        checkStatusOnMount = true,
    } = options;

    const [messages, setMessages] = useState<ChatMessage[]>(() => loadHistory());
    const [isAvailable, setIsAvailable] = useState(false);
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [currentDraft, setCurrentDraft] = useState<OrderDraft | null>(null);

    const sessionIdRef = useRef(getOrCreateSessionId());

    // Check status on mount
    useEffect(() => {
        if (checkStatusOnMount) {
            getChatStatus()
                .then((status) => setIsAvailable(status.available))
                .catch(() => setIsAvailable(false));
        }
    }, [checkStatusOnMount]);

    // Save history when messages change
    useEffect(() => {
        saveHistory(messages, maxHistory);
    }, [messages, maxHistory]);

    const sendUserMessage = useCallback(
        async (content: string) => {
            if (!content.trim()) return;

            setError(null);
            setIsLoading(true);

            // Add user message immediately
            const userMessage: ChatMessage = {
                id: generateMessageId(),
                role: 'user',
                content: content.trim(),
                timestamp: new Date(),
            };

            setMessages((prev) => [...prev, userMessage]);

            try {
                const request: ChatRequest = {
                    message: content.trim(),
                    sessionId: sessionIdRef.current,
                    currentPage,
                    productIdsInView,
                    language: detectLanguage(content),
                };

                const response = await sendMessage(request);

                // Add assistant message
                const assistantMessage: ChatMessage = {
                    id: generateMessageId(),
                    role: 'assistant',
                    content: response.message,
                    timestamp: new Date(),
                    orderDraft: response.orderDraft,
                    missingInfo: response.missingInfo,
                };

                setMessages((prev) => [...prev, assistantMessage]);

                // Update current draft if present
                if (response.orderDraft && response.orderDraft.intent !== 'none') {
                    setCurrentDraft(response.orderDraft);
                }
            } catch (err) {
                const errorMessage = err instanceof Error ? err.message : 'An error occurred';
                setError(errorMessage);

                // Add error message from assistant
                const errorAssistantMessage: ChatMessage = {
                    id: generateMessageId(),
                    role: 'assistant',
                    content: 'I encountered an issue. Please try again.',
                    timestamp: new Date(),
                    isFallback: true,
                };

                setMessages((prev) => [...prev, errorAssistantMessage]);
            } finally {
                setIsLoading(false);
            }
        },
        [currentPage, productIdsInView]
    );

    const clearHistory = useCallback(() => {
        setMessages([]);
        setCurrentDraft(null);
        const storageKey = tryGetTenantScopedStorageKey(CHAT_STORAGE_KEY);
        if (storageKey) {
            localStorage.removeItem(storageKey);
        }
    }, []);

    const acceptDraft = useCallback(() => {
        if (currentDraft) {
            // Navigate to checkout with draft
            // For now, just log - actual navigation would be implemented based on router
            console.log('Accepting draft:', currentDraft);
            // TODO: Navigate to checkout with pre-populated items
            setCurrentDraft(null);
        }
    }, [currentDraft]);

    const dismissDraft = useCallback(() => {
        setCurrentDraft(null);
    }, []);

    return {
        messages,
        isAvailable,
        isLoading,
        error,
        sendUserMessage,
        clearHistory,
        currentDraft,
        acceptDraft,
        dismissDraft,
    };
}
