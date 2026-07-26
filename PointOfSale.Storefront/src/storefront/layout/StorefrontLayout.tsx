import React, { useEffect, useState } from 'react';
import type { ComponentType } from 'react';
import { useTheme } from '../themes/ThemeContext';
import {
    getDefaultFooterComponent,
    getDefaultHeaderComponent,
    resolveFooterComponent,
    resolveHeaderComponent,
} from '../themes/ThemeLayoutResolver';

interface StorefrontLayoutProps {
    children: React.ReactNode;
}

export const StorefrontLayout: React.FC<StorefrontLayoutProps> = ({ children }) => {
    const { themeId } = useTheme();
    const [HeaderComponent, setHeaderComponent] = useState<ComponentType | null>(() => getDefaultHeaderComponent());
    const [FooterComponent, setFooterComponent] = useState<ComponentType | null>(() => getDefaultFooterComponent());

    useEffect(() => {
        let cancelled = false;
        const fallbackHeader = getDefaultHeaderComponent();
        const fallbackFooter = getDefaultFooterComponent();

        setHeaderComponent(() => fallbackHeader);
        setFooterComponent(() => fallbackFooter);

        resolveHeaderComponent(themeId)
            .then((component) => {
                if (!cancelled) {
                    setHeaderComponent(() => component ?? fallbackHeader);
                }
            })
            .catch((error) => {
                console.error(`[StorefrontLayout] Failed to resolve header for theme "${themeId}".`, error);
                if (!cancelled) {
                    setHeaderComponent(() => fallbackHeader);
                }
            });

        resolveFooterComponent(themeId)
            .then((component) => {
                if (!cancelled) {
                    setFooterComponent(() => component ?? fallbackFooter);
                }
            })
            .catch((error) => {
                console.error(`[StorefrontLayout] Failed to resolve footer for theme "${themeId}".`, error);
                if (!cancelled) {
                    setFooterComponent(() => fallbackFooter);
                }
            });

        return () => {
            cancelled = true;
        };
    }, [themeId]);

    return (
        <div className="flex min-h-full flex-col bg-background text-text">
            {HeaderComponent ? <HeaderComponent /> : null}
            {children}
            {FooterComponent ? <FooterComponent /> : null}
        </div>
    );
};

export default StorefrontLayout;
