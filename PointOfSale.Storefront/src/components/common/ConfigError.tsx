/**
 * Config Load Error Fallback
 * 
 * Shown when StoreConfig fails to load.
 */

import React from 'react';

interface ConfigErrorProps {
    error?: string;
    onRetry?: () => void;
}

export const ConfigError: React.FC<ConfigErrorProps> = ({ error, onRetry }) => {
    return (
        <div className="min-h-screen flex items-center justify-center bg-gray-100">
            <div className="text-center p-8 max-w-md">
                <div className="text-6xl mb-4">🔧</div>
                <h1 className="text-2xl font-bold text-gray-900 mb-2">
                    Store Configuration Error
                </h1>
                <p className="text-gray-600 mb-4">
                    We couldn't load the store configuration. This might be a temporary issue.
                </p>
                {error && (
                    <p className="text-sm text-red-600 mb-6 font-mono bg-red-50 p-3 rounded">
                        {error}
                    </p>
                )}
                {onRetry && (
                    <button
                        onClick={onRetry}
                        className="px-6 py-2 bg-blue-600 text-white rounded hover:bg-blue-700 transition-colors"
                    >
                        Try Again
                    </button>
                )}
            </div>
        </div>
    );
};
