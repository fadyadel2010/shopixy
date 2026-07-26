/**
 * Search Empty State Component
 * 
 * A search-specific empty state with:
 * - Appropriate icon
 * - Helpful messaging
 * - Clear action button
 * 
 * Phase 7.6.1: Search UX Enhancements
 */

import React from 'react';

interface SearchEmptyStateProps {
    /** The search query that produced no results */
    query: string;
    /** Callback to clear search */
    onClear: () => void;
    /** Additional CSS classes */
    className?: string;
}

/**
 * Search Empty State Component
 */
export const SearchEmptyState: React.FC<SearchEmptyStateProps> = ({
    query,
    onClear,
    className = '',
}) => {
    return (
        <div
            className={`flex flex-col items-center justify-center py-16 px-4 text-center ${className}`}
            role="status"
            aria-live="polite"
            aria-label="No search results"
        >
            {/* Icon */}
            <div className="w-24 h-24 mb-6 flex items-center justify-center bg-gradient-to-br from-muted/5 to-muted/15 rounded-full">
                <svg
                    className="w-12 h-12 text-muted/60"
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                    aria-hidden="true"
                >
                    <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        strokeWidth={1.5}
                        d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z"
                    />
                    <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        strokeWidth={1.5}
                        d="M10 7v6m0 0l-3-3m3 3l3-3"
                        className="opacity-50"
                    />
                </svg>
            </div>

            {/* Title */}
            <h3 className="text-xl font-semibold text-text mb-2">
                No products match your search
            </h3>

            {/* Message */}
            <p className="text-muted text-sm max-w-md mb-8 leading-relaxed">
                We couldn't find any products matching "
                <span className="font-medium text-text">{query}</span>
                ". Try a different keyword or browse our categories.
            </p>

            {/* Suggestions */}
            <div className="mb-8 text-left text-sm text-muted max-w-sm">
                <p className="font-medium mb-2">Search tips:</p>
                <ul className="space-y-1 list-disc list-inside">
                    <li>Check for spelling errors</li>
                    <li>Try more general keywords</li>
                    <li>Use fewer words in your search</li>
                </ul>
            </div>

            {/* Action button */}
            <button
                onClick={onClear}
                className="px-6 py-3 bg-primary text-white rounded-xl hover:bg-primary/90 transition-all font-medium text-sm shadow-md hover:shadow-lg hover:-translate-y-0.5 focus-visible:ring-2 focus-visible:ring-primary/50 focus-visible:ring-offset-2"
            >
                Clear Search
            </button>
        </div>
    );
};
