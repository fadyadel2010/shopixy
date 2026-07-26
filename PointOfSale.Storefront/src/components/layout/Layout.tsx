/**
 * Layout Component
 * 
 * Main page layout wrapper with Header and Footer.
 * Uses CSS variables for theming.
 * Includes global ToastContainer for notifications.
 * Includes NavigationProgress for route transition feedback.
 * 
 * Phase 7.6.2: Added navigation progress bar.
 */

import React from 'react';
import { ToastContainer, NavigationProgress } from '@/components/feedback';
import { AffiliateBanner } from './AffiliateBanner';
import { StorefrontLayout } from '@/storefront/layout/StorefrontLayout';

interface LayoutProps {
    children: React.ReactNode;
    /** @deprecated Categories are now accessed via filters sidebar on products page */
    categories?: unknown[];
    /** Show navigation loading bar */
    isLoading?: boolean;
}

export const Layout: React.FC<LayoutProps> = ({
    children,
    isLoading = false,
}) => {
    return (
        <div className="min-h-screen flex flex-col bg-background text-text">
            {/* AF-12: Affiliate Mode Banner */}
            <AffiliateBanner />

            {/* Navigation Progress Bar */}
            <NavigationProgress isLoading={isLoading} />

            <StorefrontLayout>
                <main className="flex-1" id="main-content">
                    {children}
                </main>
            </StorefrontLayout>

            {/* Global Toast Notifications */}
            <ToastContainer />
        </div>
    );
};
