import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
vi.mock('@/i18n', () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock('@/services/api/httpClient', () => ({ httpClient: { post: vi.fn() } }));
vi.mock('react-router-dom', () => ({ Link: ({ to, children, ...props }: any) => <a href={to} {...props}>{children}</a> }));
import { VerifyEmailBanner } from './VerifyEmailBanner';
describe('verification confirmation', () => {
    it('does not claim an email was dispatched after transport failure', () => {
        const html = renderToStaticMarkup(<VerifyEmailBanner email="quality@example.test" emailSent={false} canResend={false} />);
        expect(html).toContain('auth.verifyEmail.dispatchFailed');
        expect(html).not.toContain('<button');
        expect(html).toContain('href="/login?return=%2Faccount"');
    });
    it('claims dispatch only with affirmative dispatch evidence', () => {
        expect(renderToStaticMarkup(<VerifyEmailBanner email="quality@example.test" emailSent />)).toContain('auth.verifyEmail.description');
        expect(renderToStaticMarkup(<VerifyEmailBanner email="quality@example.test" />)).toContain('auth.verifyEmail.pending');
    });
});
