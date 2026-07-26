import React, { createContext, useContext, useEffect } from 'react';
import { StoreConfig, DEFAULT_STORE_CONFIG } from '../../types/storeConfig';
import { useStoreConfig } from '../../hooks/useStoreConfig';
import { applyTheme } from '../../theme/applyTheme';

const ThemeContext = createContext<StoreConfig>(DEFAULT_STORE_CONFIG);

export const useTheme = () => useContext(ThemeContext);

export const ThemeProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const { config, loading } = useStoreConfig();

    useEffect(() => {
        if (config && config.theme) {
            applyTheme(config.theme);
        }
    }, [config]);

    // Don't block hydration/render entirely if loading?
    // Usually we want to block until theme is applied to avoid flash of unstyled content.
    if (loading) {
        return (
            <div className="min-h-screen flex items-center justify-center bg-gray-50">
                <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
            </div>
        );
    }

    return (
        <ThemeContext.Provider value={config || DEFAULT_STORE_CONFIG}>
            {children}
        </ThemeContext.Provider>
    );
};
