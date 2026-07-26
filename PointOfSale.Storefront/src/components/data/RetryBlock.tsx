/**
 * Retry Block Component
 * 
 * A compact retry block for failed operations.
 * Can be inline or block-level.
 */

import React, { useState } from 'react';

interface RetryBlockProps {
    /** Error message */
    message?: string;
    /** Retry callback */
    onRetry: () => void | Promise<void>;
    /** Inline mode (compact) */
    inline?: boolean;
    /** Additional CSS classes */
    className?: string;
}

/**
 * Retry Block Component
 */
export const RetryBlock: React.FC<RetryBlockProps> = ({
    message = 'Something went wrong',
    onRetry,
    inline = false,
    className = '',
}) => {
    const [isRetrying, setIsRetrying] = useState(false);

    const handleRetry = async () => {
        setIsRetrying(true);
        try {
            await onRetry();
        } finally {
            setIsRetrying(false);
        }
    };

    if (inline) {
        return (
            <div className={`inline-flex items-center gap-3 text-sm ${className}`}>
                <span className="text-red-600">{message}</span>
                <button
                    onClick={handleRetry}
                    disabled={isRetrying}
                    className="inline-flex items-center gap-1.5 px-3 py-1.5 text-red-600 hover:text-red-700 hover:bg-red-50 rounded-md transition-colors disabled:opacity-50"
                >
                    {isRetrying ? (
                        <svg className="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24">
                            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                        </svg>
                    ) : (
                        <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                        </svg>
                    )}
                    Retry
                </button>
            </div>
        );
    }

    return (
        <div className={`p-4 bg-red-50 border border-red-200 rounded-lg ${className}`} role="alert">
            <div className="flex items-start gap-3">
                {/* Icon */}
                <div className="flex-shrink-0 w-5 h-5 text-red-600 mt-0.5">
                    <svg fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                </div>

                {/* Content */}
                <div className="flex-1 min-w-0">
                    <p className="text-sm text-red-700 font-medium">
                        {message}
                    </p>
                </div>

                {/* Retry button */}
                <button
                    onClick={handleRetry}
                    disabled={isRetrying}
                    className="flex-shrink-0 inline-flex items-center gap-1.5 px-3 py-1.5 bg-red-600 text-white text-sm font-medium rounded-md hover:bg-red-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                >
                    {isRetrying ? (
                        <>
                            <svg className="w-4 h-4 animate-spin" fill="none" viewBox="0 0 24 24">
                                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                            </svg>
                            Retrying...
                        </>
                    ) : (
                        <>
                            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                            </svg>
                            Retry
                        </>
                    )}
                </button>
            </div>
        </div>
    );
};
