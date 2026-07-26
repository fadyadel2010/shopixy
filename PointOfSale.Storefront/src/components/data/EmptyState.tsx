/**
 * Empty State Component
 * 
 * Displays a friendly empty state with icon, title, and message.
 * Theme-aware and accessible.
 */

import React from 'react';

type EmptyStateVariant = 'default' | 'search' | 'category' | 'error';

interface EmptyStateProps {
    /** Title text */
    title: string;
    /** Description message */
    message?: string;
    /** Variant for different empty states */
    variant?: EmptyStateVariant;
    /** Optional action button */
    action?: {
        label: string;
        onClick: () => void;
    };
    /** Custom icon (overrides variant icon) */
    icon?: React.ReactNode;
    /** Additional CSS classes */
    className?: string;
}

/**
 * Get icon based on variant
 */
const getVariantIcon = (variant: EmptyStateVariant): React.ReactNode => {
    switch (variant) {
        case 'search':
            return (
                <svg className="w-12 h-12" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                </svg>
            );
        case 'category':
            return (
                <svg className="w-12 h-12" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" />
                </svg>
            );
        case 'error':
            return (
                <svg className="w-12 h-12" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                </svg>
            );
        default:
            return (
                <svg className="w-12 h-12" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M20 7l-8-4-8 4m16 0l-8 4m8-4v10l-8 4m0-10L4 7m8 4v10M4 7v10l8 4" />
                </svg>
            );
    }
};

/**
 * Empty State Component
 */
export const DataEmptyState: React.FC<EmptyStateProps> = ({
    title,
    message,
    variant = 'default',
    action,
    icon,
    className = '',
}) => {
    const displayIcon = icon || getVariantIcon(variant);

    return (
        <div
            className={`flex flex-col items-center justify-center py-12 px-4 text-center ${className}`}
            role="status"
            aria-label={title}
        >
            {/* Icon */}
            <div className="w-20 h-20 mb-6 flex items-center justify-center bg-muted/10 rounded-full text-muted/50">
                {displayIcon}
            </div>

            {/* Title */}
            <h3 className="text-lg font-semibold text-text mb-2">
                {title}
            </h3>

            {/* Message */}
            {message && (
                <p className="text-muted text-sm max-w-sm mb-6">
                    {message}
                </p>
            )}

            {/* Action button */}
            {action && (
                <button
                    onClick={action.onClick}
                    className="px-6 py-2.5 bg-primary text-white rounded-lg hover:bg-primary/90 transition-colors font-medium text-sm"
                >
                    {action.label}
                </button>
            )}
        </div>
    );
};
