import type { ReactElement } from 'react';
export interface StorefrontBreadcrumbsProps {
    appendCurrentLabel?: boolean;
    className?: string;
    currentLabel?: string;
    onNavigate?: (path: string) => void | boolean;
}
export function StorefrontBreadcrumbs(props: StorefrontBreadcrumbsProps): ReactElement | null;
export default StorefrontBreadcrumbs;
