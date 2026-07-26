import React, { createContext, useContext, useEffect } from 'react';
import { selectConfig, useStoreConfigStore } from '../../store';
import { resolveActiveTheme } from './ThemeResolver';
import type { ThemeDefinition } from './ThemeRegistry';

export interface ThemeContextValue {
    themeId: string;
    themeDefinition: ThemeDefinition;
}

const ThemeContext = createContext<ThemeContextValue | undefined>(undefined);

export const ThemeProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
    const config = useStoreConfigStore(selectConfig);
    const themeDefinition: ThemeDefinition = resolveActiveTheme(config.storefrontTheme);

    useEffect(() => {
        if (import.meta.env.DEV) {
            console.log('[ThemeProvider] Theme resolved:', {
                storefrontTheme: config.storefrontTheme,
                resolvedTheme: themeDefinition.id,
            });
        }
    }, [config.storefrontTheme, themeDefinition.id]);

    return (
        <ThemeContext.Provider
            value={{
                themeId: themeDefinition.id,
                themeDefinition,
            }}
        >
            {children}
        </ThemeContext.Provider>
    );
};

export function useTheme(): ThemeContextValue {
    const context = useContext(ThemeContext);

    if (!context) {
        throw new Error('useTheme must be used within a ThemeProvider.');
    }

    return context;
}
