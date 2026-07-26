/**
 * Inline Error Component
 * 
 * Displays an inline error message with optional retry.
 * Used within content areas, not as a full-page error.
 */

import React from 'react';

interface InlineErrorProps {
    /** Error message to display */
    message: string;
    /** Whether to show retry button */
    showRetry?: boolean;
    /** Retry callback */
    onRetry?: () => void;
    /** Additional CSS classes */
    className?: string;
}

/**
 * Inline Error Component
 */
export const InlineError: React.FC<InlineErrorProps> = ({
    message,
    showRetry = true,
    onRetry,
    className = '',
}) => {
    return (
        <div
            className={`flex flex-col items-center justify-center py-8 px-4 text-center ${className}`}
            role="alert"
        >
            {/* Error icon */}
            <div className="w-14 h-14 mb-4 flex items-center justify-center bg-red-100 rounded-full">
                <svg
                    className="w-7 h-7 text-red-600"
                    fill="none"
                    stroke="currentColor"
                    viewBox="0 0 24 24"
                >
                    <path
                        strokeLinecap="round"
                        strokeLinejoin="round"
                        strokeWidth={2}
                        d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z"
                    />
                </svg>
            </div>

            {/* Error message */}
            <p className="text-red-600 font-medium mb-4">
                {message}
            </p>

            {/* Retry button */}
            {showRetry && onRetry && (
                <button
                    onClick={onRetry}
                    className="inline-flex items-center gap-2 px-4 py-2 bg-red-600 text-white rounded-lg hover:bg-red-700 transition-colors text-sm font-medium"
                >
                    <svg
                        className="w-4 h-4"
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24"
                    >
                        <path
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            strokeWidth={2}
                            d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"
                        />
                    </svg>
                    Try Again
                </button>
            )}
        </div>
    );
};
