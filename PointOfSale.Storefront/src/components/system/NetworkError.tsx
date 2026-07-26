/**
 * Network Error Component
 * 
 * Shown when there's a network connectivity issue.
 * System-level error screen with retry - no header/footer.
 */

import React, { useState } from 'react';

interface NetworkErrorProps {
    /** Error message to display */
    message?: string;
    /** Retry callback */
    onRetry?: () => void | Promise<void>;
}

export const NetworkError: React.FC<NetworkErrorProps> = ({
    message = 'Unable to connect to the server.',
    onRetry
}) => {
    const [retrying, setRetrying] = useState(false);

    const handleRetry = async () => {
        if (onRetry) {
            setRetrying(true);
            try {
                await onRetry();
            } finally {
                setRetrying(false);
            }
        } else {
            window.location.reload();
        }
    };

    return (
        <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-slate-50 to-slate-100 p-4">
            <div className="max-w-md w-full text-center p-8 bg-white rounded-2xl shadow-lg">
                {/* Icon */}
                <div className="w-20 h-20 mx-auto mb-6 bg-orange-100 rounded-full flex items-center justify-center">
                    <svg
                        className="w-10 h-10 text-orange-600"
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24"
                    >
                        <path
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            strokeWidth={2}
                            d="M18.364 5.636a9 9 0 010 12.728m0 0l-2.829-2.829m2.829 2.829L21 21M15.536 8.464a5 5 0 010 7.072m0 0l-2.829-2.829m-4.243 2.829a4.978 4.978 0 01-1.414-2.83m-1.414 5.658a9 9 0 01-2.167-9.238m7.824 2.167a1 1 0 111.414 1.414m-1.414-1.414L3 3m8.293 8.293l1.414 1.414"
                        />
                    </svg>
                </div>

                {/* Title */}
                <h1 className="text-2xl font-bold text-slate-900 mb-3">
                    Connection Problem
                </h1>

                {/* Description */}
                <p className="text-slate-600 mb-6">
                    {message}
                </p>

                {/* Tips */}
                <div className="text-left bg-slate-50 rounded-lg p-4 mb-6">
                    <p className="text-sm font-medium text-slate-700 mb-2">
                        Things to try:
                    </p>
                    <ul className="text-sm text-slate-600 space-y-1.5">
                        <li className="flex items-start gap-2">
                            <span className="text-slate-400 mt-0.5">•</span>
                            Check your internet connection
                        </li>
                        <li className="flex items-start gap-2">
                            <span className="text-slate-400 mt-0.5">•</span>
                            Disable VPN if you're using one
                        </li>
                        <li className="flex items-start gap-2">
                            <span className="text-slate-400 mt-0.5">•</span>
                            Try refreshing the page
                        </li>
                    </ul>
                </div>

                {/* Retry button */}
                <button
                    onClick={handleRetry}
                    disabled={retrying}
                    className="inline-flex items-center gap-2 px-6 py-3 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:bg-blue-400 transition-colors font-medium"
                >
                    {retrying ? (
                        <>
                            <svg
                                className="w-5 h-5 animate-spin"
                                fill="none"
                                viewBox="0 0 24 24"
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
                            Retrying...
                        </>
                    ) : (
                        <>
                            <svg
                                className="w-5 h-5"
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
                        </>
                    )}
                </button>

                {/* Status */}
                <p className="mt-8 text-xs text-slate-400">
                    The server might be temporarily unavailable.
                </p>
            </div>
        </div>
    );
};
