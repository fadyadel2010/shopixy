import React, { useEffect, useState } from 'react';
import type { ComponentType } from 'react';
import { useTheme } from '../themes/ThemeContext';
import { getDefaultPageComponent, resolvePageComponent } from '../themes/ThemePageResolver';

interface ThemePageRouteProps {
    pageName: string;
}

export const ThemePageRoute: React.FC<ThemePageRouteProps> = ({ pageName }) => {
    const { themeId } = useTheme();
    const [PageComponent, setPageComponent] = useState<ComponentType | null>(() => (
        getDefaultPageComponent(pageName)
    ));

    useEffect(() => {
        let cancelled = false;
        const fallbackPage = getDefaultPageComponent(pageName);

        setPageComponent(() => fallbackPage);

        resolvePageComponent(themeId, pageName)
            .then((component) => {
                if (!cancelled) {
                    setPageComponent(() => component ?? fallbackPage);
                }
            })
            .catch((error) => {
                console.error(
                    `[ThemePageRoute] Failed to resolve page "${pageName}" for theme "${themeId}".`,
                    error
                );

                if (!cancelled) {
                    setPageComponent(() => fallbackPage);
                }
            });

        return () => {
            cancelled = true;
        };
    }, [pageName, themeId]);

    return PageComponent ? <PageComponent /> : null;
};

export default ThemePageRoute;
