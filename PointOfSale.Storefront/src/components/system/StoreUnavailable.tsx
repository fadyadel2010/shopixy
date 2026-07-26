/**
 * Store Unavailable Component
 * 
 * Shown when store config returns 404 or store is disabled.
 * System-level error screen - no header/footer.
 */

import React from 'react';

interface StoreUnavailableProps {
    /** Error message to display */
    message?: string;
}

export const StoreUnavailable: React.FC<StoreUnavailableProps> = ({
    message = 'This store is currently unavailable.'
}) => {
    return (
        <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-slate-50 to-slate-100 p-4">
            <div className="max-w-md w-full text-center p-8 bg-white rounded-2xl shadow-lg">
                {/* Icon */}
                <div className="w-20 h-20 mx-auto mb-6 bg-red-100 rounded-full flex items-center justify-center">
                    <svg
                        className="w-10 h-10 text-red-600"
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

                {/* Title */}
                <h1 className="text-2xl font-bold text-slate-900 mb-3">
                    Store Unavailable
                </h1>

                {/* Description */}
                <p className="text-slate-600 mb-6">
                    {message}
                </p>

                {/* Possible reasons */}
                <div className="text-left bg-slate-50 rounded-lg p-4 mb-6">
                    <p className="text-sm font-medium text-slate-700 mb-2">
                        This might happen because:
                    </p>
                    <ul className="text-sm text-slate-600 space-y-1.5">
                        <li className="flex items-start gap-2">
                            <span className="text-slate-400 mt-0.5">•</span>
                            The store doesn't exist
                        </li>
                        <li className="flex items-start gap-2">
                            <span className="text-slate-400 mt-0.5">•</span>
                            The store has been temporarily disabled
                        </li>
                        <li className="flex items-start gap-2">
                            <span className="text-slate-400 mt-0.5">•</span>
                            The URL might be incorrect
                        </li>
                    </ul>
                </div>

                {/* Actions */}
                <div className="flex flex-col sm:flex-row gap-3 justify-center">
                    <button
                        onClick={() => window.location.reload()}
                        className="px-6 py-2.5 bg-slate-900 text-white rounded-lg hover:bg-slate-800 transition-colors font-medium"
                    >
                        Try Again
                    </button>
                </div>

                {/* Contact */}
                <p className="mt-8 text-xs text-slate-400">
                    If this problem persists, please contact the store owner.
                </p>
            </div>
        </div>
    );
};
