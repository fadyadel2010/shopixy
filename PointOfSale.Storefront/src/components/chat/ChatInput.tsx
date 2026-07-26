/**
 * AI Commerce Assistant - Chat Input Component
 * Text input with send button for the chat widget.
 */

import React, { useState, useRef, useEffect } from 'react';
import { useTranslation } from 'react-i18next';

interface ChatInputProps {
    onSend: (message: string) => void;
    disabled?: boolean;
    isRTL: boolean;
}

const ChatInput: React.FC<ChatInputProps> = ({ onSend, disabled, isRTL }) => {
    const { t } = useTranslation();
    const [value, setValue] = useState('');
    const inputRef = useRef<HTMLTextAreaElement>(null);

    // Auto-resize textarea
    useEffect(() => {
        if (inputRef.current) {
            inputRef.current.style.height = 'auto';
            inputRef.current.style.height = `${Math.min(inputRef.current.scrollHeight, 120)}px`;
        }
    }, [value]);

    const handleSubmit = (e: React.FormEvent) => {
        e.preventDefault();
        if (value.trim() && !disabled) {
            onSend(value.trim());
            setValue('');
        }
    };

    const handleKeyDown = (e: React.KeyboardEvent) => {
        // Submit on Enter (without Shift)
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            handleSubmit(e);
        }
    };

    return (
        <form className="chat-input" onSubmit={handleSubmit}>
            <textarea
                ref={inputRef}
                className="chat-input__field"
                value={value}
                onChange={(e) => setValue(e.target.value)}
                onKeyDown={handleKeyDown}
                placeholder={t('chat.input.placeholder', 'Type your message...')}
                disabled={disabled}
                dir={isRTL ? 'rtl' : 'ltr'}
                rows={1}
                maxLength={2000}
            />
            <button
                type="submit"
                className="chat-input__send"
                disabled={disabled || !value.trim()}
                aria-label={t('chat.input.send', 'Send')}
            >
                <svg
                    xmlns="http://www.w3.org/2000/svg"
                    width="20"
                    height="20"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    style={{ transform: isRTL ? 'scaleX(-1)' : 'none' }}
                >
                    <line x1="22" y1="2" x2="11" y2="13" />
                    <polygon points="22 2 15 22 11 13 2 9 22 2" />
                </svg>
            </button>
        </form>
    );
};

export default ChatInput;
