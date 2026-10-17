import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
const auth = vi.hoisted(() => ({ isAuthenticated: false, isLoading: false, customer: null as any, status: 'unauthenticated', logout: vi.fn() }));
vi.mock('../hooks/useAuth', () => ({ useAuth: () => auth }));
vi.mock('@/i18n', () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock('react-router-dom', () => ({ useLocation: () => ({ pathname: '/', search: '' }), Link: ({ to, children, ...props }: any) => <a href={to} {...props}>{children}</a> }));
import { AuthNavItems } from './AuthNavItems';
describe('account controls on light theme surfaces', () => {
    it('inherits the theme foreground instead of forcing white guest controls', () => {
        auth.isAuthenticated = false; auth.customer = null;
        const html = renderToStaticMarkup(<AuthNavItems />);
        expect(html).toContain('href="/login"'); expect(html).toContain('href="/register"');
        expect(html).not.toMatch(/text-white/);
    });
    it('gives icon-only account and logout controls accessible names at tablet widths', () => {
        auth.isAuthenticated = true; auth.customer = { displayName: 'Quality' };
        const html = renderToStaticMarkup(<AuthNavItems />);
        expect(html).toContain('aria-label="header.account"');
        expect(html).toContain('aria-label="header.logout"');
    });
});
