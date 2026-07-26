/**
 * Tenant Not Found Component
 * 
 * Shown when tenant cannot be resolved from URL.
 * System-level error screen - no header/footer.
 */

import React from 'react';

interface TenantNotFoundProps {
    /** The attempted hostname/subdomain */
    hostname?: string;
}

export const TenantNotFound: React.FC<TenantNotFoundProps> = ({ hostname }) => {
    return (
        <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-slate-50 to-slate-100 p-4">
            <div className="max-w-md w-full text-center p-8 bg-white rounded-2xl shadow-lg">
                {/* Icon */}
                <div className="w-20 h-20 mx-auto mb-6 bg-amber-100 rounded-full flex items-center justify-center">
                    <svg
                        className="w-10 h-10 text-amber-600"
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24"
                    >
                        <path
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            strokeWidth={2}
                            d="M9.172 16.172a4 4 0 015.656 0M9 10h.01M15 10h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
                        />
                    </svg>
                </div>

                {/* Title */}
                <h1 className="text-2xl font-bold text-slate-900 mb-3">
                    Store Not Found
                </h1>

                {/* Description */}
                <p className="text-slate-600 mb-6">
                    We couldn't identify which store you're trying to access.
                    Please check the URL and try again.
                </p>

                {/* Attempted URL */}
                {hostname && (
                    <div className="bg-slate-100 rounded-lg px-4 py-3 mb-6">
                        <p className="text-sm text-slate-500 mb-1">Attempted URL:</p>
                        <code className="text-sm text-slate-700 break-all">
                            {hostname}
                        </code>
                    </div>
                )}

                {/* Help text */}
                <div className="text-sm text-slate-500 space-y-2">
                    <p>Expected format:</p>
                    <code className="block bg-slate-100 rounded px-3 py-2 text-slate-700">
                        yourstore.example.com
                    </code>
                </div>

                {/* Contact */}
                <p className="mt-8 text-xs text-slate-400">
                    If you believe this is an error, please contact support.
                </p>
            </div>
        </div>
    );
};
