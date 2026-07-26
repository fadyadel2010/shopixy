import React from 'react';
import ReactDOM from 'react-dom/client';
import { App } from './app';
import { removeTrackingTokenFromCurrentUrl } from './features/checkout/activeCheckoutOrderContext';
import { initializeLanguage } from './i18n';
import './styles/index.css';

// Strip inbound tracking tokens before any analytics, bootstrap, or React effects can observe the URL.
// Wrapped in try/catch to ensure app startup continues even if URL mitigation fails.
try {
    removeTrackingTokenFromCurrentUrl();
} catch (error) {
    console.error('[Startup] Failed to remove tracking token from URL:', error);
    // Swallow error - URL mitigation failure should not prevent app from loading
}

// Initialize language (applies lang/dir attributes to document)
initializeLanguage();

ReactDOM.createRoot(document.getElementById('root')!).render(
    <React.StrictMode>
        <App />
    </React.StrictMode>
);
