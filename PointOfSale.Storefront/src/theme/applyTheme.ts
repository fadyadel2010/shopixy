/**
 * Apply Theme
 * 
 * Applies theme configuration to the document by setting CSS variables on :root.
 * Called during app bootstrap after StoreConfig is loaded.
 */

import type { ThemeConfig, LayoutConfig } from '@/types';
import { themeToCssVariables } from './cssVariables';

/**
 * Apply theme CSS variables to document root
 */
export const applyTheme = (theme: ThemeConfig): void => {
    const root = document.documentElement;
    const cssVariables = themeToCssVariables(theme);

    // Apply each CSS variable
    Object.entries(cssVariables).forEach(([property, value]) => {
        root.style.setProperty(property, value);
    });

    // Apply background and text color to body
    document.body.style.backgroundColor = `var(--color-background)`;
    document.body.style.color = `var(--color-text)`;

    if (import.meta.env.DEV) {
        console.log('[Theme] Applied CSS variables:', cssVariables);
    }
};

/**
 * Log layout variants (for Phase 7.3)
 * Currently just logs - actual UI differences implemented in Phase 7.3
 */
export const logLayoutVariants = (layout: LayoutConfig): void => {
    if (import.meta.env.DEV) {
        console.log('[Theme] Layout variants:', {
            headerVariant: layout.headerVariant,
            productCardVariant: layout.productCardVariant,
        });
    }
};
