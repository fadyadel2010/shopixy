/**
 * SearchInput Component
 * 
 * Unified search input with:
 * - Debounced input
 * - Clear button
 * - Loading indicator
 * - Keyboard shortcuts
 * - Accessible labels
 * 
 * Language-aware placeholders based on document direction.
 */

import React, { useCallback, useRef, useEffect } from 'react';
import { useDebounce } from '@/utils';
import { AUTOCOMPLETE_DEBOUNCE_MS } from '@/services/api/searchApi';

interface SearchInputProps {
    value: string;
    onChange: (value: string) => void;
    onSearch?: (value: string) => void;
    onFocus?: () => void;
    onBlur?: () => void;
    onKeyDown?: (event: React.KeyboardEvent<HTMLInputElement>) => void;
    isLoading?: boolean;
    autoFocus?: boolean;
    placeholder?: string;
    className?: string;
    inputClassName?: string;
    showClearButton?: boolean;
    size?: 'sm' | 'md' | 'lg';
}

export const SearchInput: React.FC<SearchInputProps> = ({
    value,
    onChange,
    onSearch,
    onFocus,
    onBlur,
    onKeyDown,
    isLoading = false,
    autoFocus = false,
    placeholder,
    className = '',
    inputClassName = '',
    showClearButton = true,
    size = 'md',
}) => {
    const inputRef = useRef<HTMLInputElement>(null);
    const debouncedValue = useDebounce(value, AUTOCOMPLETE_DEBOUNCE_MS);

    // Trigger onSearch when debounced value changes
    // Smart header search works from 1 character!
    useEffect(() => {
        if (onSearch && debouncedValue.trim().length >= 1) {
            onSearch(debouncedValue);
        }
    }, [debouncedValue, onSearch]);

    // Get language-aware placeholder
    const getPlaceholder = (): string => {
        if (placeholder) return placeholder;

        // Check if RTL (Arabic)
        const isRtl = document.documentElement.dir === 'rtl';
        return isRtl
            ? 'ابحث عن منتجات أو أقسام أو صفحات...'
            : 'Search products, categories, pages...';
    };

    const handleChange = useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
        onChange(e.target.value);
    }, [onChange]);

    const handleClear = useCallback(() => {
        onChange('');
        inputRef.current?.focus();
    }, [onChange]);

    const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
        if (e.key === 'Escape' && value) {
            e.preventDefault();
            handleClear();
        }
        onKeyDown?.(e);
    };

    // Size classes
    const sizeClasses = {
        sm: 'h-9 text-sm px-3',
        md: 'h-11 text-base px-4',
        lg: 'h-14 text-lg px-5',
    };

    return (
        <div className={`relative flex items-center ${className}`}>
            {/* Search Icon */}
            <div className="absolute left-3 top-1/2 -translate-y-1/2 text-muted pointer-events-none">
                {isLoading ? (
                    <svg
                        className="w-5 h-5 animate-spin"
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
                ) : (
                    <svg
                        className="w-5 h-5"
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
                )}
            </div>

            {/* Input */}
            <input
                ref={inputRef}
                type="search"
                value={value}
                onChange={handleChange}
                onFocus={onFocus}
                onBlur={onBlur}
                onKeyDown={handleKeyDown}
                placeholder={getPlaceholder()}
                autoFocus={autoFocus}
                autoComplete="off"
                autoCorrect="off"
                autoCapitalize="off"
                spellCheck={false}
                aria-label="Search"
                className={`
                    w-full pl-11 pr-10
                    bg-background border border-muted/30
                    rounded-lg
                    text-text placeholder:text-muted/60
                    focus:outline-none focus:ring-2 focus:ring-primary/50 focus:border-primary
                    transition-all duration-200
                    ${sizeClasses[size]}
                    ${inputClassName}
                `}
            />

            {/* Clear Button */}
            {showClearButton && value && (
                <button
                    type="button"
                    onClick={handleClear}
                    className="absolute right-3 top-1/2 -translate-y-1/2 p-1 text-muted hover:text-text transition-colors rounded-full hover:bg-muted/10"
                    aria-label="Clear search"
                >
                    <svg
                        className="w-4 h-4"
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
                </button>
            )}
        </div>
    );
};
