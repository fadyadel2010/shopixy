/**
 * Main App Component
 * 
 * Entry point for the storefront application.
 * Uses centralized bootstrap flow with proper error handling.
 * 
 * Flow:
 * App.tsx → bootstrap() → Auth Init → Error/Success → Check Status → Render appropriate screen
 * 
 * PHASE 7.10: Added storefront status handling for maintenance/disabled states
 * PHASE F1.1: Added auth initialization for 401 handling
 * AF-12: Added AffiliateProvider for affiliate mode context
 */

import React, { useEffect, useState } from 'react';
import { RouterProvider } from 'react-router-dom';
import { router } from './router';
import {
    bootstrap,
    resetBootstrap,
    BootstrapErrorCode,
    type BootstrapError
} from '@/bootstrap';
import { useStoreConfigStore, selectIsConfigLoaded, selectConfig } from '@/store';
import { ErrorBoundary } from '@/components/common';
import {
    AppLoading,
    TenantNotFound,
    StoreUnavailable,
    NetworkError,
    StoreMaintenancePage,
    StoreDisabledPage
} from '@/components/system';
import { initializeAuth } from '@/auth/bootstrap';
import { startEditorModeIfRequested } from '@/editor-mode';
import { AffiliateProvider } from '@/contexts/AffiliateContext';
import { OfferProvider } from '@/storefront/providers/OfferProvider';
import { ThemeProvider } from '@/storefront/themes/ThemeContext';

/**
 * Bootstrap states
 */
type BootstrapState =
    | { status: 'loading' }
    | { status: 'initializing_auth' }
    | { status: 'success' }
    | { status: 'error'; error: BootstrapError };

export const App: React.FC = () => {
    const [bootstrapState, setBootstrapState] = useState<BootstrapState>({ status: 'loading' });
    const isConfigLoaded = useStoreConfigStore(selectIsConfigLoaded);
    const config = useStoreConfigStore(selectConfig);

    useEffect(() => {
        const init = async () => {
            // Step 1: Bootstrap (tenant resolution, store config)
            const result = await bootstrap();

            if (!result.success) {
                setBootstrapState({ status: 'error', error: result.error });
                return;
            }

            // Step 2: Initialize auth system (silent, non-blocking for guests)
            setBootstrapState({ status: 'initializing_auth' });
            try {
                await initializeAuth();
            } catch (error) {
                // Auth init failures are non-fatal for guests
                console.warn('[App] Auth initialization failed (non-fatal):', error);
            }

            // Step 3: Editor mode (ARCH-1.3) — no-op unless embedded by the
            // builder host with explicit editorMode + editorHostOrigin params.
            startEditorModeIfRequested();

            // Step 4: Ready
            setBootstrapState({ status: 'success' });
        };

        init();
    }, []);

    // Handle retry for retryable errors
    const handleRetry = async () => {
        setBootstrapState({ status: 'loading' });
        resetBootstrap();

        const result = await bootstrap();

        if (!result.success) {
            setBootstrapState({ status: 'error', error: result.error });
            return;
        }

        setBootstrapState({ status: 'initializing_auth' });
        try {
            await initializeAuth();
        } catch {
            // Non-fatal
        }

        setBootstrapState({ status: 'success' });
    };

    // Loading state
    let content: React.ReactNode;

    if (bootstrapState.status === 'loading') {
        content = <AppLoading message="Loading store..." />;
    } else if (bootstrapState.status === 'initializing_auth') {
        content = <AppLoading message="Preparing your experience..." />;
    } else if (bootstrapState.status === 'error') {
        const { error } = bootstrapState;

        switch (error.code) {
            case BootstrapErrorCode.TENANT_RESOLUTION_FAILED:
                content = <TenantNotFound hostname={window.location.hostname} />;
                break;

            case BootstrapErrorCode.STORE_NOT_FOUND:
            case BootstrapErrorCode.STORE_UNAVAILABLE:
                content = <StoreUnavailable message={error.message} />;
                break;

            case BootstrapErrorCode.NETWORK_ERROR:
                content = (
                    <NetworkError
                        message={error.message}
                        onRetry={handleRetry}
                    />
                );
                break;

            case BootstrapErrorCode.UNKNOWN_ERROR:
            default:
                content = (
                    <NetworkError
                        message={error.message || 'An unexpected error occurred.'}
                        onRetry={handleRetry}
                    />
                );
                break;
        }
    } else if (!isConfigLoaded) {
        content = <AppLoading message="Preparing store..." />;
    } else if (config?.storefrontStatus === 'maintenance') {
        content = <StoreMaintenancePage message={config.maintenanceMessage} />;
    } else if (config?.storefrontStatus === 'disabled') {
        content = <StoreDisabledPage />;
    } else {
        content = (
            <ErrorBoundary>
                <AffiliateProvider>
                    <OfferProvider>
                        <RouterProvider router={router} />
                    </OfferProvider>
                </AffiliateProvider>
            </ErrorBoundary>
        );
    }

    return <ThemeProvider>{content}</ThemeProvider>;
};

