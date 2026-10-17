import { afterEach, expect, it, vi } from 'vitest';
import { resolveAnalyticsContext } from './AnalyticsContextResolver';

const queue = vi.hoisted(() => ({ enqueue: vi.fn() }));
vi.mock('./AnalyticsQueue', () => ({ enqueueAnalyticsEvent: queue.enqueue, flushAnalyticsQueue: vi.fn() }));
vi.mock('@/bootstrap', () => ({ isTenantResolved: () => true, getTenantId: () => 'p6-tenant' }));
vi.mock('@/bootstrap/sessionManager', () => ({ getSessionId: () => 'p6-session' }));
vi.mock('@/bootstrap/tenantScopedStorage', () => ({ tryGetTenantScopedStorageKey: (key: string) => 'p6:' + key }));
afterEach(() => { vi.clearAllMocks(); vi.unstubAllGlobals(); });

it('preserves only bounded UTM evidence and removes arbitrary secrets and fragments', () => {
    vi.stubGlobal('window', { location: { origin: 'https://qa.example', href: 'https://qa.example/?utm_source=QA&utm_medium=cpc&utm_campaign=p6qa&utm_content=creative&utm_term=term&token=secret&email=private#secret' }, innerWidth: 1000, innerHeight: 800 });
    vi.stubGlobal('document', { documentElement: { lang: 'en' }, referrer: '' });
    const context = resolveAnalyticsContext();
    expect(context.pageUrl).toContain('utm_source=QA');
    expect(context.pageUrl).toContain('utm_term=term');
    expect(context.pageUrl).not.toContain('secret');
    expect(context.pageUrl).not.toContain('email');
});

it('bounds campaign parameters and strips URL credentials even for custom URLs', () => {
    vi.stubGlobal('window', { location: { origin: 'https://qa.example', href: `https://user:secret@qa.example/?utm_campaign=${'a'.repeat(300)}` }, innerWidth: 1000, innerHeight: 800 });
    vi.stubGlobal('document', { documentElement: { lang: 'ar' }, referrer: 'https://referrer.example/?token=private' });
    const context = resolveAnalyticsContext();
    expect(new URL(context.pageUrl).searchParams.get('utm_campaign')).toHaveLength(200);
    expect(context.pageUrl).not.toContain('secret');
    expect(context.referrer).toBe('https://referrer.example/');
});
