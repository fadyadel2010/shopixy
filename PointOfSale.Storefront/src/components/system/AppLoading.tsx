/**
 * App Loading Component
 * 
 * Shown during bootstrap/initialization.
 * Simple, clean loading state - no header/footer.
 */

import React from 'react';

interface AppLoadingProps {
    message?: string;
}

export const AppLoading: React.FC<AppLoadingProps> = ({
    message = 'Loading store...'
}) => {
    return (
        <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-slate-50 to-slate-100">
            <div className="text-center p-8">
                {/* Animated Logo/Spinner */}
                <div className="relative mb-8">
                    <div className="w-16 h-16 mx-auto">
                        {/* Outer ring */}
                        <div className="absolute inset-0 border-4 border-slate-200 rounded-full" />
                        {/* Spinning ring */}
                        <div className="absolute inset-0 border-4 border-transparent border-t-blue-600 rounded-full animate-spin" />
                    </div>
                </div>

                {/* Message */}
                <p className="text-slate-600 text-lg font-medium animate-pulse">
                    {message}
                </p>
            </div>
        </div>
    );
};
