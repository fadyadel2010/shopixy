import { useEffect, useState } from 'react';
import ReactDOM from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { HomePage } from '@/pages/HomePage';
import { ThemeProvider } from '@/storefront/themes/ThemeContext';
import { useStoreConfigStore } from '@/store';
import { setResolvedTenant } from '@/bootstrap';
import { useLanguageStore } from '@/i18n';
import { applyTheme } from '@/theme';
import { useNavigationStore } from '@/stores/navigationStore';
import { useFooterStore } from '@/stores/footerStore';
import { initializeAuth } from '@/auth/bootstrap';
import { admitDraftPreviewMessage, DRAFT_PREVIEW_PROTOCOL } from './draftPreviewContract';
import '@/styles/index.css';

// No public config bootstrap, draft fetch, Admin credential, or persistent draft
// storage here. Only the authenticated Admin parent can supply the preview DTO.
const parentOrigin = import.meta.env.VITE_ADMIN_ORIGIN || (import.meta.env.DEV ? 'http://localhost:5173' : '');
const params = new URLSearchParams(window.location.search);
const tenantId = params.get('tenant') || '';
const nonce = window.location.hash.slice(1);

function DraftPreview() {
    const [ready, setReady] = useState(false);
    useEffect(() => {
        if (!parentOrigin || window.parent === window || !tenantId || !nonce) return;
        let authStarted = false;
        const receive = (event: MessageEvent) => {
            const candidate = admitDraftPreviewMessage(event, window.parent, parentOrigin, tenantId, nonce);
            if (!candidate) return;
            setResolvedTenant(tenantId, { overwrite: true });
            useStoreConfigStore.getState().setConfig(candidate.config);
            useLanguageStore.getState().setLanguage(candidate.language);
            useNavigationStore.getState().invalidateNavigation();
            useNavigationStore.setState({ navigation: candidate.config.previewChrome.navigation, loading: false, error: null });
            useFooterStore.getState().invalidateFooter();
            useFooterStore.setState({ footer: candidate.config.previewChrome.footerByLanguage[candidate.language], loading: false, error: null });
            applyTheme(candidate.config.theme);
            if (!authStarted) { authStarted = true; void initializeAuth(); }
            setReady(true);
            window.parent.postMessage({ protocol: DRAFT_PREVIEW_PROTOCOL, type: 'applied', nonce }, parentOrigin);
        };
        window.addEventListener('message', receive);
        window.parent.postMessage({ protocol: DRAFT_PREVIEW_PROTOCOL, type: 'ready', nonce }, parentOrigin);
        return () => window.removeEventListener('message', receive);
    }, []);

    if (!ready) return <p role="status">Waiting for authenticated Admin preview.</p>;
    return <MemoryRouter initialEntries={['/']}><ThemeProvider><div
        data-admin-draft-preview="true"
        onSubmitCapture={event => { event.preventDefault(); event.stopPropagation(); }}
        onClickCapture={event => {
            // Inspect content/mobile menu/language without following shopper navigation
            // or executing commerce actions from a read-only authoring viewport.
            const target = event.target as HTMLElement;
            const button = target.closest('button');
            if (target.closest('a') || (button && !/menu|القائمة|إغلاق|close|arabic|english|العربية|الإنجليزية/i.test(button.getAttribute('aria-label') || ''))) {
                event.preventDefault(); event.stopPropagation();
            }
        }}
    ><HomePage /></div></ThemeProvider></MemoryRouter>;
}

ReactDOM.createRoot(document.getElementById('root')!).render(<DraftPreview />);
