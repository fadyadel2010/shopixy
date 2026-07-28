/**
 * AI Commerce Assistant - Chat Widget
 * 
 * PHASE 6: Commerce AI Admin Control
 * 
 * This is the main floating chat widget for AI commerce assistance.
 * 
 * SAFETY RULES (NON-NEGOTIABLE):
 * - Widget only renders if commerceAiEnabled is true
 * - All AI interactions are read-only (no direct order placement)
 * - Customer initiates all conversations
 * - Admin controls feature visibility via /ecommerce/commerce-ai
 */

import React, { useState, useEffect, useRef, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import {
    useStoreConfigStore,
    selectCommerceAiEnabled,
    selectCommerceAiWidgetStyle,
    selectCommerceAiWidgetPosition,
    selectCommerceAiAutoGreetingEnabled,
    selectCommerceAiAutoGreetingMessage
} from '@/store';
import ChatMessage from './ChatMessage';
import ChatInput from './ChatInput';
import OrderDraftPreview from './OrderDraftPreview';
import type { ChatMessage as ChatMessageType, ChatRequest, ChatResponse, OrderDraft } from '../../types/chat';
import { httpClient } from '@/services/api/httpClient';
import { tryGetTenantScopedStorageKey } from '@/bootstrap/tenantScopedStorage';
import './ChatWidget.css';

// Generate unique session ID
const generateSessionId = (): string => {
    return `session_${Date.now()}_${Math.random().toString(36).substring(2, 11)}`;
};

// Get or create session ID from localStorage
const getSessionId = (): string => {
    const key = tryGetTenantScopedStorageKey('commerce_ai_session_id');
    if (!key) {
        return generateSessionId();
    }

    let sessionId = localStorage.getItem(key);
    if (!sessionId) {
        sessionId = generateSessionId();
        localStorage.setItem(key, sessionId);
    }
    return sessionId;
};

function hasSessionFlag(key: string): boolean {
    const storageKey = tryGetTenantScopedStorageKey(key);
    return storageKey ? sessionStorage.getItem(storageKey) === 'true' || sessionStorage.getItem(storageKey) === '1' : false;
}

function setSessionFlag(key: string, value: string): void {
    const storageKey = tryGetTenantScopedStorageKey(key);
    if (storageKey) {
        sessionStorage.setItem(storageKey, value);
    }
}

/**
 * Suggestion item for quick actions
 */
interface Suggestion {
    key: string;
    textEn: string;
    textAr: string;
}

const SUGGESTIONS: Suggestion[] = [
    { key: 'products', textEn: 'What products do you have?', textAr: 'ما هي المنتجات المتوفرة؟' },
    { key: 'shipping', textEn: 'Tell me about shipping options', textAr: 'أخبرني عن خيارات الشحن' },
    { key: 'help', textEn: 'Help me find something', textAr: 'ساعدني في البحث عن شيء' },
];

const ChatWidget: React.FC = () => {
    const { t, i18n } = useTranslation();
    const isRTL = i18n.language === 'ar';

    // PHASE 6: Feature gating - widget won't render if AI is disabled
    const commerceAiEnabled = useStoreConfigStore(selectCommerceAiEnabled);
    const widgetStyle = useStoreConfigStore(selectCommerceAiWidgetStyle);
    const widgetPosition = useStoreConfigStore(selectCommerceAiWidgetPosition);
    const autoGreetingEnabled = useStoreConfigStore(selectCommerceAiAutoGreetingEnabled);
    const autoGreetingMessage = useStoreConfigStore(selectCommerceAiAutoGreetingMessage);

    // Debug logging
    useEffect(() => {
        console.log('[ChatWidget] Commerce AI Enabled:', commerceAiEnabled);
    }, [commerceAiEnabled]);

    // Widget state
    const [isOpen, setIsOpen] = useState(false);
    const [isMinimized, setIsMinimized] = useState(false);
    const [messages, setMessages] = useState<ChatMessageType[]>([]);
    const [isLoading, setIsLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [orderDraft, setOrderDraft] = useState<OrderDraft | null>(null);
    const [sessionId] = useState(() => getSessionId());

    // Auto-greeting tracking
    const GREETING_KEY = `commerce_ai_greeting_shown_${sessionId}`;

    // Initial greeting effect
    useEffect(() => {
        if (!autoGreetingEnabled || !autoGreetingMessage || !commerceAiEnabled) return;

        // Check session storage
        if (hasSessionFlag(GREETING_KEY)) return;

        // Check if we already have messages
        if (messages.length > 0) return;

        // Add auto greeting
        const greeting: ChatMessageType = {
            id: `msg_greeting_${Date.now()}`,
            role: 'assistant',
            content: autoGreetingMessage,
            timestamp: new Date()
        };

        // Small delay for natural feel
        const timer = setTimeout(() => {
            setMessages([greeting]);
            setSessionFlag(GREETING_KEY, 'true');
        }, 1000);

        return () => clearTimeout(timer);
    }, [autoGreetingEnabled, autoGreetingMessage, commerceAiEnabled, messages.length, sessionId, GREETING_KEY]);

    // Refs
    const messagesEndRef = useRef<HTMLDivElement>(null);

    // Scroll to bottom when messages change
    useEffect(() => {
        messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
    }, [messages]);

    /**
     * Phase 9: Track Commerce AI Events (Privacy-Safe)
     */
    const trackEvent = useCallback(async (eventType: 'ChatOpened' | 'OrderDraftCreated') => {
        console.log(`[ChatWidget] trackEvent called for: ${eventType}`);
        try {
            const response = await httpClient.post('/api/storefront/commerce-ai/event', { type: eventType });
            console.log(`[ChatWidget] Event response status: ${response.status}`);
        } catch (err) {
            // Fire-and-forget, ignore errors
            console.warn('[ChatWidget] Tracking failed', err);
        }
    }, []);

    /**
     * Send message to AI backend
     */
    const sendMessage = useCallback(async (content: string) => {
        if (!content.trim() || isLoading) return;

        // Add user message immediately
        const userMessage: ChatMessageType = {
            id: `msg_${Date.now()}`,
            role: 'user',
            content: content.trim(),
            timestamp: new Date(),
        };
        setMessages(prev => [...prev, userMessage]);
        setIsLoading(true);
        setError(null);

        try {
            // STORE-AWARE CONTEXT (Fix #1)
            // Query all visible product cards on the page (max 6)
            const productIdsInView = Array.from(
                document.querySelectorAll('[data-ai-product-id]')
            )
                .map(el => el.getAttribute('data-ai-product-id'))
                .filter(Boolean)
                .slice(0, 6);

            const request: ChatRequest = {
                message: content.trim(),
                sessionId,
                // Cast to any because the type definition might expect string[] but we are sending (string | null)[] filtered to string[]
                // actually .filter(Boolean) returns string[] but TS might need help
                productIdsInView: productIdsInView as string[],
                currentPage: window.location.pathname,
                language: i18n.language,
            };

            const response = await httpClient.post<ChatResponse>('/api/storefront/chat', request);
            const data = response.data;

            // Add assistant message
            const assistantMessage: ChatMessageType = {
                id: `msg_${Date.now()}_assistant`,
                role: 'assistant',
                content: data.message,
                timestamp: new Date(),
                orderDraft: data.orderDraft,
                missingInfo: data.missingInfo,
            };
            setMessages(prev => [...prev, assistantMessage]);

            // Handle order draft
            if (data.orderDraft && data.orderDraft.intent !== 'none') {
                setOrderDraft(data.orderDraft);
                trackEvent('OrderDraftCreated');
            }
        } catch (err) {
            console.error('[ChatWidget] Error sending message:', err);
            setError(t('chat.error.send', 'Failed to send message. Please try again.'));

            // Add fallback message
            const fallbackMessage: ChatMessageType = {
                id: `msg_${Date.now()}_fallback`,
                role: 'assistant',
                content: t('chat.error.fallback', 'I\'m having trouble connecting. Please try again in a moment.'),
                timestamp: new Date(),
                isFallback: true,
            };
            setMessages(prev => [...prev, fallbackMessage]);
        } finally {
            setIsLoading(false);
        }
    }, [isLoading, sessionId, i18n.language, t, trackEvent]);

    /**
     * Handle suggestion click
     */
    const handleSuggestionClick = (suggestion: Suggestion) => {
        sendMessage(isRTL ? suggestion.textAr : suggestion.textEn);
    };

    /**
     * Handle order draft accept - redirect to checkout
     */
    /**
     * Handle order draft accept - just clear the state, navigation is handled by OrderDraftPreview
     */
    const handleDraftAccept = () => {
        setOrderDraft(null);
    };

    /**
     * Handle order draft dismiss
     */
    const handleDraftDismiss = () => {
        setOrderDraft(null);
    };

    /**
     * Toggle widget open/closed
     */
    const toggleWidget = () => {
        if (isMinimized) {
            setIsMinimized(false);
        } else {
            const newOpenState = !isOpen;
            setIsOpen(newOpenState);

            // Track ChatOpened once per session
            if (newOpenState && !hasSessionFlag('commerce_ai_chat_opened')) {
                trackEvent('ChatOpened');
                setSessionFlag('commerce_ai_chat_opened', '1');
            }
        }
    };

    /**
     * Minimize widget
     */
    const minimizeWidget = () => {
        setIsMinimized(true);
        setIsOpen(false);
    };

    /**
     * Close widget completely
     */
    const closeWidget = () => {
        setIsOpen(false);
        setIsMinimized(false);
    };

    // =========================================================================
    // PHASE 6: FEATURE GATING
    // Widget MUST NOT render if commerceAiEnabled is false
    // =========================================================================
    if (!commerceAiEnabled) {
        return null;
    }

    return (
        <div className={`chat-widget ${isRTL ? 'chat-widget--rtl' : ''} chat-widget--pos-${widgetPosition}`}>
            {/* Main chat window */}
            {isOpen && (
                <div className="chat-widget__window">
                    {/* Header */}
                    <div className="chat-widget__header">
                        <div className="chat-widget__header-info">
                            <div className="chat-widget__avatar">
                                <span>🤖</span>
                            </div>
                            <div className="chat-widget__header-text">
                                <span className="chat-widget__title">
                                    {t('chat.title', 'AI Assistant')}
                                </span>
                                <span className="chat-widget__status">
                                    {t('chat.status.online', 'Online')}
                                </span>
                            </div>
                        </div>
                        <div className="chat-widget__header-actions">
                            <button
                                className="chat-widget__action"
                                onClick={minimizeWidget}
                                aria-label={t('chat.minimize', 'Minimize')}
                            >
                                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                    <line x1="5" y1="12" x2="19" y2="12" />
                                </svg>
                            </button>
                            <button
                                className="chat-widget__action"
                                onClick={closeWidget}
                                aria-label={t('chat.close', 'Close')}
                            >
                                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                    <line x1="18" y1="6" x2="6" y2="18" />
                                    <line x1="6" y1="6" x2="18" y2="18" />
                                </svg>
                            </button>
                        </div>
                    </div>

                    {/* Messages area */}
                    <div className="chat-widget__messages">
                        {messages.length === 0 ? (
                            <div className="chat-widget__welcome">
                                <p>
                                    {t('chat.welcome', 'Hi! 👋 How can I help you today?')}
                                </p>
                                <div className="chat-widget__suggestions">
                                    {SUGGESTIONS.map((suggestion) => (
                                        <button
                                            key={suggestion.key}
                                            className="chat-widget__suggestion"
                                            onClick={() => handleSuggestionClick(suggestion)}
                                        >
                                            {isRTL ? suggestion.textAr : suggestion.textEn}
                                        </button>
                                    ))}
                                </div>
                            </div>
                        ) : (
                            messages.map((msg) => (
                                <ChatMessage
                                    key={msg.id}
                                    message={msg}
                                    isRTL={isRTL}
                                />
                            ))
                        )}

                        {/* Typing indicator */}
                        {isLoading && (
                            <div className="chat-widget__typing">
                                <span></span>
                                <span></span>
                                <span></span>
                            </div>
                        )}

                        <div ref={messagesEndRef} />
                    </div>

                    {/* Order draft preview */}
                    {orderDraft && orderDraft.intent !== 'none' && (
                        <OrderDraftPreview
                            draft={orderDraft}
                            onAccept={handleDraftAccept}
                            onDismiss={handleDraftDismiss}
                            isRTL={isRTL}
                        />
                    )}

                    {/* Error display */}
                    {error && (
                        <div className="chat-widget__error">
                            {error}
                        </div>
                    )}

                    {/* Input */}
                    <ChatInput
                        onSend={sendMessage}
                        disabled={isLoading}
                        isRTL={isRTL}
                    />
                </div>
            )}

            {/* Minimized state */}
            {isMinimized && !isOpen && (
                <button
                    className="chat-widget__minimized"
                    onClick={toggleWidget}
                >
                    <span>🤖</span>
                    <span>{t('chat.minimized', 'Chat with us')}</span>
                </button>
            )}

            {/* Floating trigger button */}
            {!isOpen && !isMinimized && (
                <button
                    className={`chat-widget__trigger chat-widget__trigger--${widgetStyle}`}
                    onClick={toggleWidget}
                    aria-label={t('chat.open', 'Open chat')}
                >
                    {widgetStyle === 'floating' ? (
                        <>
                            <span className="text-xl">🤖</span>
                            <span className="font-medium whitespace-nowrap">{t('chat.open', 'Chat Now')}</span>
                        </>
                    ) : (
                        <span className="text-2xl">
                            {widgetStyle === 'sales' ? '🛍️' :
                                widgetStyle === 'compact' ? '✨' :
                                    '💬'}
                        </span>
                    )}
                </button>
            )}
        </div>
    );
};

export default ChatWidget;
