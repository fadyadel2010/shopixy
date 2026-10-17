import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { removeTrackingTokenFromCurrentUrl } from './activeCheckoutOrderContext';

describe('tracking URL mitigation ownership', () => {
  beforeEach(() => {
    const location = { href: 'http://localhost/', search: '', hash: '' };
    vi.stubGlobal('document', { title: 'Synthetic fixture' });
    vi.stubGlobal('window', { location, history: { state: null, replaceState: (_state: unknown, _title: string, path: string) => {
      const url = new URL(path, location.href);
      Object.assign(location, { href: url.href, search: url.search, hash: url.hash });
    } } });
  });
  afterEach(() => vi.unstubAllGlobals());

  it.each(['/verify-email', '/reset-password'])('does not consume the %s authentication token', path => {
    window.history.replaceState({}, '', `${path}?token=synthetic-auth-token&tenant=synthetic-tenant`);
    expect(removeTrackingTokenFromCurrentUrl().removed).toBe(false);
    expect(new URL(window.location.href).searchParams.get('token')).toBe('synthetic-auth-token');
  });

  it.each(['?token=synthetic-tracking-token', '#tracking_token=synthetic-tracking-token'])('still strips tracking capabilities: %s', suffix => {
    window.history.replaceState({}, '', `/orders/synthetic-order/track${suffix}`);
    expect(removeTrackingTokenFromCurrentUrl().removed).toBe(true);
    expect(window.location.search).toBe('');
    expect(window.location.hash).toBe('');
  });
});
