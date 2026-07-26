/**
 * Search Status Indicator Component
 * 
 * Visual indicator showing current search status:
 * - Typing indicator (dots animation)
 * - Searching indicator (spinner)
 * - Results count
 * - Error state
 * 
 * Phase 7.6.1: Search UX Enhancements
 */

import React from 'react';
import type { SearchStatus } from '@/hooks';

interface SearchStatusIndicatorProps {
    /** Current search status */
    status: SearchStatus;
    /** Query being searched */
    query: string;
    /** Number of results (when success) */
    resultCount?: number;
    /** Error message (when error) */
    errorMessage?: string;
    /** Callback to retry search */
    onRetry?: () => void;
    /** Additional CSS classes */
    className?: string;
}

/**
 * Typing dots animation component
 */
const TypingDots: React.FC = () => (
    <span className="inline-flex items-center gap-0.5" aria-label="Typing">
        {[0, 1, 2].map((i) => (
            <span
                key={i}
                className="w-1.5 h-1.5 bg-primary/60 rounded-full animate-bounce"
                style={{ animationDelay: `${i * 150}ms` }}
            />
        ))}
    </span>
);

/**
 * Spinner component
 */
const Spinner: React.FC<{ className?: string }> = ({ className = '' }) => (
    <svg
        className={`w-4 h-4 animate-spin ${className}`}
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
 * Search Status Indicator Component
 */
export const SearchStatusIndicator: React.FC<SearchStatusIndicatorProps> = ({
    status,
    query,
    resultCount,
    errorMessage,
    onRetry,
    className = '',
}) => {
    // Don't show anything in idle state
    if (status === 'idle') return null;

    const renderContent = () => {
        switch (status) {
            case 'typing':
                return (
                    <div className="flex items-center gap-2 text-muted text-sm">
                        <TypingDots />
                        <span>Typing...</span>
                    </div>
                );

            case 'searching':
                return (
                    <div className="flex items-center gap-2 text-primary text-sm" role="status" aria-live="polite">
                        <Spinner />
                        <span>Searching for "{query}"</span>
                    </div>
                );

            case 'success':
                return (
                    <div className="flex items-center gap-2 text-muted text-sm" role="status" aria-live="polite">
                        <svg className="w-4 h-4 text-green-500" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                        </svg>
                        <span>
                            Found <strong className="font-medium text-text">{resultCount}</strong> product{resultCount !== 1 ? 's' : ''}
                        </span>
                    </div>
                );

            case 'empty':
                return (
                    <div className="flex items-center gap-2 text-muted text-sm" role="status" aria-live="polite">
                        <svg className="w-4 h-4 text-amber-500" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                        </svg>
                        <span>No results for "{query}"</span>
                    </div>
                );

            case 'error':
                return (
                    <div className="flex items-center gap-2 text-sm">
                        <svg className="w-4 h-4 text-red-500" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                        </svg>
                        <span className="text-red-600">{errorMessage || 'Search failed'}</span>
                        {onRetry && (
                            <button
                                onClick={onRetry}
                                className="ml-2 text-primary hover:underline font-medium"
                            >
                                Retry
                            </button>
                        )}
                    </div>
                );

            default:
                return null;
        }
    };

    return (
        <div className={`transition-all duration-200 ${className}`}>
            {renderContent()}
        </div>
    );
};
