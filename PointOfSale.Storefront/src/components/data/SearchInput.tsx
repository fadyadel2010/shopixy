/**
 * Search Input Component
 * 
 * Enhanced search input with:
 * - Keyboard shortcuts (Esc to clear, Enter to search)
 * - Visual feedback for all states
 * - Animated clear button
 * - Mobile-friendly design
 * - Full accessibility support
 * 
 * Phase 7.6.1: Search UX Enhancements
 */

import React, { useRef, useEffect, forwardRef, useImperativeHandle } from 'react';
import type { SearchStatus } from '@/hooks';
import { MIN_SEARCH_LENGTH } from '@/hooks/useProductSearch';

// ============================================================================
// Types
// ============================================================================

interface SearchInputProps {
    /** Current search query */
    value: string;
    /** Change callback */
    onChange: (value: string) => void;
    /** Clear callback */
    onClear: () => void;
    /** Force search callback (bypasses debounce) */
    onForceSearch?: () => void;
    /** Search status */
    status: SearchStatus;
    /** Placeholder text */
    placeholder?: string;
    /** Minimum characters hint */
    minChars?: number;
    /** Show keyboard shortcut hint */
    showShortcutHint?: boolean;
    /** Additional CSS classes */
    className?: string;
    /** Auto focus on mount */
    autoFocus?: boolean;
    /** ID for the input element */
    id?: string;
}

export interface SearchInputHandle {
    focus: () => void;
    blur: () => void;
    select: () => void;
}

// ============================================================================
// Sub-components
// ============================================================================

/**
 * Spinner icon for loading state
 */
const SpinnerIcon: React.FC<{ className?: string }> = ({ className = '' }) => (
    <svg
        className={`animate-spin ${className}`}
        fill="none"
        viewBox="0 0 24 24"
        aria-hidden="true"
    >
        <circle
            className="opacity-25"
            cx="12"
            cy="12"
            r="10"
            stroke="currentColor"
            strokeWidth="4"
        />
        <path
            className="opacity-75"
            fill="currentColor"
            d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
        />
    </svg>
);

/**
 * Search icon
 */
const SearchIcon: React.FC<{ className?: string }> = ({ className = '' }) => (
    <svg
        className={className}
        fill="none"
        stroke="currentColor"
        viewBox="0 0 24 24"
        aria-hidden="true"
    >
        <path
            strokeLinecap="round"
            strokeLinejoin="round"
            strokeWidth={2}
            d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"
        />
    </svg>
);

/**
 * Clear (X) icon
 */
const ClearIcon: React.FC<{ className?: string }> = ({ className = '' }) => (
    <svg
        className={className}
        fill="none"
        stroke="currentColor"
        viewBox="0 0 24 24"
        aria-hidden="true"
    >
        <path
            strokeLinecap="round"
            strokeLinejoin="round"
            strokeWidth={2}
            d="M6 18L18 6M6 6l12 12"
        />
    </svg>
);

/**
 * Typing dots animation
 */
const TypingIndicator: React.FC = () => (
    <div className="flex items-center gap-0.5" aria-hidden="true">
        {[0, 1, 2].map((i) => (
            <span
                key={i}
                className="w-1 h-1 bg-primary rounded-full animate-bounce"
                style={{ animationDelay: `${i * 100}ms`, animationDuration: '0.6s' }}
            />
        ))}
    </div>
);

// ============================================================================
// Main Component
// ============================================================================

/**
 * Search Input Component
 */
export const SearchInput = forwardRef<SearchInputHandle, SearchInputProps>(({
    value,
    onChange,
    onClear,
    onForceSearch,
    status,
    placeholder = 'Search products...',
    minChars = MIN_SEARCH_LENGTH,
    showShortcutHint = true,
    className = '',
    autoFocus = false,
    id = 'search-input',
}, ref) => {
    const inputRef = useRef<HTMLInputElement>(null);
    const showClear = value.length > 0;
    const isSearching = status === 'searching';
    const isTyping = status === 'typing';
    const needsMoreChars = value.length > 0 && value.length < minChars;
    const isFocused = useRef(false);

    // Expose methods via ref
    useImperativeHandle(ref, () => ({
        focus: () => inputRef.current?.focus(),
        blur: () => inputRef.current?.blur(),
        select: () => inputRef.current?.select(),
    }), []);

    // Auto focus on mount
    useEffect(() => {
        if (autoFocus && inputRef.current) {
            inputRef.current.focus();
        }
    }, [autoFocus]);

    /**
     * Handle keyboard events
     */
    const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
        switch (e.key) {
            case 'Escape':
                e.preventDefault();
                onClear();
                inputRef.current?.blur();
                break;
            case 'Enter':
                e.preventDefault();
                if (onForceSearch && value.length >= minChars) {
                    onForceSearch();
                }
                break;
        }
    };

    /**
     * Handle input change
     */
    const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
        onChange(e.target.value);
    };

    /**
     * Handle clear button click
     */
    const handleClear = () => {
        onClear();
        inputRef.current?.focus();
    };

    /**
     * Get status-based border color
     */
    const getBorderClass = (): string => {
        switch (status) {
            case 'error':
                return 'border-red-300 focus:border-red-500 focus:ring-red-500/30';
            case 'success':
                return 'border-green-200 focus:border-primary focus:ring-primary/30';
            case 'searching':
            case 'typing':
                return 'border-primary/40 focus:border-primary focus:ring-primary/30';
            default:
                return 'border-muted/20 focus:border-primary focus:ring-primary/30';
        }
    };

    /**
     * Get icon based on status
     */
    const renderLeftIcon = () => {
        if (isSearching) {
            return <SpinnerIcon className="w-5 h-5 text-primary" />;
        }
        if (isTyping) {
            return <TypingIndicator />;
        }
        return <SearchIcon className="w-5 h-5" />;
    };

    // Detect platform for shortcut hint
    const isMac = typeof navigator !== 'undefined' && /Mac|iPod|iPhone|iPad/.test(navigator.platform);
    const shortcutKey = isMac ? '⌘K' : 'Ctrl+K';

    return (
        <div className={`relative ${className}`}>
            {/* Left icon (search / loading / typing) */}
            <div
                className={`absolute left-4 top-1/2 -translate-y-1/2 transition-colors duration-200 ${isSearching || isTyping ? 'text-primary' : 'text-muted'
                    }`}
            >
                {renderLeftIcon()}
            </div>

            {/* Input field */}
            <input
                ref={inputRef}
                id={id}
                type="search"
                value={value}
                onChange={handleChange}
                onKeyDown={handleKeyDown}
                onFocus={() => { isFocused.current = true; }}
                onBlur={() => { isFocused.current = false; }}
                placeholder={placeholder}
                autoComplete="off"
                autoCorrect="off"
                autoCapitalize="off"
                spellCheck={false}
                className={`
                    w-full h-12 md:h-11
                    pl-12 pr-${showClear ? '20' : (showShortcutHint ? '16' : '4')}
                    bg-background
                    border ${getBorderClass()}
                    rounded-xl
                    text-text text-base md:text-sm
                    placeholder:text-muted/60
                    focus:outline-none focus:ring-2
                    transition-all duration-200
                    touch-manipulation
                `}
                aria-label="Search products"
                aria-describedby={needsMoreChars ? 'search-hint' : undefined}
                role="searchbox"
            />

            {/* Right side: clear button or shortcut hint */}
            <div className="absolute right-3 top-1/2 -translate-y-1/2 flex items-center gap-2">
                {/* Shortcut hint (desktop only, when not typing) */}
                {showShortcutHint && !showClear && !isSearching && (
                    <kbd
                        className="hidden md:inline-flex items-center gap-0.5 px-2 py-1 text-xs text-muted/60 bg-muted/5 border border-muted/10 rounded-md font-mono"
                        aria-hidden="true"
                    >
                        {shortcutKey}
                    </kbd>
                )}

                {/* Clear button */}
                {showClear && (
                    <button
                        type="button"
                        onClick={handleClear}
                        className={`
                            w-8 h-8 md:w-7 md:h-7
                            flex items-center justify-center
                            text-muted hover:text-text
                            bg-transparent hover:bg-muted/10
                            rounded-full
                            transition-all duration-200
                            focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/50
                            ${isSearching ? 'animate-pulse' : ''}
                        `}
                        aria-label="Clear search"
                        tabIndex={0}
                    >
                        <ClearIcon className="w-4 h-4" />
                    </button>
                )}
            </div>

            {/* Character count hint */}
            {needsMoreChars && (
                <p
                    id="search-hint"
                    className="absolute -bottom-6 left-0 text-xs text-muted animate-fade-in"
                    role="status"
                    aria-live="polite"
                >
                    Enter at least {minChars} characters to search
                </p>
            )}

            {/* Screen reader status */}
            <div className="sr-only" role="status" aria-live="polite">
                {status === 'searching' && `Searching for ${value}`}
                {status === 'success' && 'Search complete'}
                {status === 'empty' && `No results found for ${value}`}
                {status === 'error' && 'Search failed, please try again'}
            </div>
        </div>
    );
});

SearchInput.displayName = 'SearchInput';
