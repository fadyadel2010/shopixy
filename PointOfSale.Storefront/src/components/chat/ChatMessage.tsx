/**
 * AI Commerce Assistant - Chat Message Component
 * Renders individual messages in the chat conversation.
 */

import React from 'react';
import type { ChatMessage as ChatMessageType } from '../../types/chat';

interface ChatMessageProps {
    message: ChatMessageType;
    isRTL: boolean;
}

const ChatMessage: React.FC<ChatMessageProps> = ({ message, isRTL }) => {
    const isUser = message.role === 'user';

    return (
        <div
            className={`chat-message ${isUser ? 'chat-message--user' : 'chat-message--assistant'} ${message.isFallback ? 'chat-message--fallback' : ''
                }`}
            dir={isRTL ? 'rtl' : 'ltr'}
        >
            <div className="chat-message__bubble">
                <p className="chat-message__content">{message.content}</p>

                {/* Missing info list */}
                {message.missingInfo && message.missingInfo.length > 0 && (
                    <div className="chat-message__missing-info">
                        <ul>
                            {message.missingInfo.map((info, index) => (
                                <li key={index}>{formatMissingInfo(info, isRTL)}</li>
                            ))}
                        </ul>
                    </div>
                )}
            </div>

            <span className="chat-message__time">
                {formatTime(message.timestamp, isRTL)}
            </span>
        </div>
    );
};

/**
 * Format timestamp for display
 */
function formatTime(date: Date, isRTL: boolean): string {
    return new Intl.DateTimeFormat(isRTL ? 'ar-EG' : 'en-US', {
        hour: 'numeric',
        minute: '2-digit',
        hour12: true,
    }).format(date);
}

/**
 * Format missing info type for display
 */
function formatMissingInfo(info: string, isRTL: boolean): string {
    const labels: Record<string, { en: string; ar: string }> = {
        quantity: { en: 'Quantity', ar: 'الكمية' },
        shipping_method: { en: 'Shipping method', ar: 'طريقة الشحن' },
        address: { en: 'Delivery address', ar: 'عنوان التوصيل' },
        variant_selection: { en: 'Size/Color selection', ar: 'اختيار المقاس/اللون' },
        product_selection: { en: 'Product selection', ar: 'اختيار المنتج' },
        contact_info: { en: 'Contact information', ar: 'معلومات الاتصال' },
    };

    const label = labels[info];
    return label ? (isRTL ? label.ar : label.en) : info;
}

export default ChatMessage;
