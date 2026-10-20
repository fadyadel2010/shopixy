import { renderToStaticMarkup } from 'react-dom/server';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
    guard: { status: 'unauthenticated', isLoading: true, showContent: false },
    auth: { error: null as null | { code: string; message: string }, isLoading: false },
}));
vi.mock('@/i18n', () => ({ useTranslation: () => ({ t: (key: string, options?: { defaultValue?: string }) => options?.defaultValue || key, direction: 'ltr' }) }));
vi.mock('react-router-dom', () => ({
    useNavigate: () => vi.fn(), useLocation: () => ({ search: '' }),
    Link: ({ to, children }: { to: string; children: React.ReactNode }) => <a href={to}>{children}</a>,
}));
vi.mock('../hooks/useAuthGuard', () => ({ useAuthGuard: () => mocks.guard }));
vi.mock('../hooks/useAuth', () => ({ useAuth: () => ({ ...mocks.auth, register: vi.fn(), clearError: vi.fn() }) }));
vi.mock('@/store', () => ({ useStoreConfigStore: () => 'QA Store', selectStoreName: vi.fn() }));
vi.mock('../components/SocialLoginButtons', () => ({ SocialLoginButtons: () => null }));
vi.mock('../components/VerifyEmailBanner', () => ({ VerifyEmailBanner: () => null }));
import { RegisterPage } from './RegisterPage';

describe('registration lifecycle presentation', () => {
    beforeEach(() => {
        mocks.guard.status = 'unauthenticated'; mocks.guard.isLoading = true; mocks.guard.showContent = false;
        mocks.auth.error = null; mocks.auth.isLoading = false;
    });
    it('keeps the existing form mounted during submission instead of replacing it with auth loading', () => {
        mocks.auth.isLoading = true;
        const html = renderToStaticMarkup(<RegisterPage />);
        expect(html).toContain('id="register-email"');
        expect(html).not.toContain('register-phone');
        expect(html).toContain('Creating account...');
    });
    it('shows initial unknown authentication loading, not an actionable register form', () => {
        mocks.guard.status = 'unknown';
        expect(renderToStaticMarkup(<RegisterPage />)).not.toContain('id="register-email"');
    });
    it('renders exactly the remaining controls without a phone label, hidden value or error', () => {
        const html = renderToStaticMarkup(<RegisterPage />);
        expect(html.match(/<input\b/g)).toHaveLength(4);
        for (const field of ['name', 'email', 'password', 'confirm-password']) {
            expect(html).toContain(`id="register-${field}"`);
        }
        expect(html).not.toMatch(/phone|type="tel"|autocomplete="tel"|\(Optional\)/i);
        expect(html).toContain('At least 8 characters with uppercase, lowercase, and a number');
    });
    it('keeps request failures visible without restoring a removed field', () => {
        mocks.auth.error = { code: 'EMAIL_EXISTS', message: 'An account with this email already exists' };
        const html = renderToStaticMarkup(<RegisterPage />);
        expect(html).toContain('role="alert"');
        expect(html).toContain('An account with this email already exists');
        expect(html).not.toContain('register-phone');
    });
});
