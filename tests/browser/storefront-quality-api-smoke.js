// Real hosted negative contracts only. No valid registration, resend, OTP or order request.
async page => {
    const context = await page.context().browser().newContext();
    const tab = await context.newPage();
    const blocked = [], cases = [], errors = [];
    let identity = null;
    tab.on('pageerror', error => errors.push(error.message));
    tab.on('request', request => {
        const h = request.headers();
        if (/^[a-f0-9-]{36}$/i.test(h['x-tenant-id'] ?? '') && h['x-session-id']) {
            identity = { 'X-Tenant-Id': h['x-tenant-id'], 'X-Session-Id': h['x-session-id'] };
        }
    });
    await tab.route('**/api/**', route => {
        const request = route.request();
        const path = request.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0];
        if (!path.startsWith('/api/') || ['GET', 'HEAD', 'OPTIONS'].includes(request.method())) return route.continue();
        if (request.method() === 'POST') {
            const body = request.postDataJSON() ?? {};
            const harmless = (path === '/api/storefront/auth/register' && body.email === 'quality-validation@example.test' && body.password === 'short')
                || (path === '/api/storefront/auth/verify-email' && body.token?.startsWith('quality-invalid-'))
                || (path === '/api/storefront/auth/resend-verification' && Object.keys(body).length === 0)
                || (path === '/api/storefront/checkout/request-phone-otp' && body.phone === '')
                || (path === '/api/storefront/checkout/verify-phone-otp' && body.phone === '' && body.otpCode === '');
            if (harmless) return route.continue();
        }
        blocked.push({ method: request.method(), path });
        return route.fulfill({ status: 409, contentType: 'application/json', body: '{"code":"QUALITY_UNSAFE_REQUEST_BLOCKED"}' });
    });
    try {
        await tab.goto('http://ahramstore.localhost:5174/');
        await tab.locator('[data-product-card-theme]').first().waitFor();
        if (!identity) throw new Error('Bootstrap did not provide a resolved tenant/session header pair');
        const result = await tab.evaluate(async headers => {
            const cookie = document.cookie.split(';').map(s => s.trim()).find(s => s.startsWith('csrf_token='));
            if (cookie) headers['X-CSRF-Token'] = decodeURIComponent(cookie.slice('csrf_token='.length));
            const calls = [
                ['weak-registration', '/api/storefront/auth/register', { email: 'quality-validation@example.test', password: 'short', displayName: 'Quality validation' }, 400, 'WEAK_PASSWORD'],
                ['invalid-email-token', '/api/storefront/auth/verify-email', { token: `quality-invalid-${crypto.randomUUID()}` }, 400, 'INVALID_TOKEN'],
                ['guest-resend-rejected', '/api/storefront/auth/resend-verification', {}, 401, null],
                ['missing-phone-request', '/api/storefront/checkout/request-phone-otp', { phone: '' }, 400, ['PHONE_REQUIRED', 'VALIDATION_ERROR']],
                ['missing-phone-verification', '/api/storefront/checkout/verify-phone-otp', { phone: '', otpCode: '' }, 400, ['INVALID_REQUEST', 'VALIDATION_ERROR']],
            ];
            const results = [];
            for (const [name, path, body, expectedStatus, expectedCode] of calls) {
                const response = await fetch(path, { method: 'POST', credentials: 'include', headers: { ...headers, 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
                const data = await response.json().catch(() => ({}));
                const codeMatches = !expectedCode || (Array.isArray(expectedCode) ? expectedCode.includes(data.code) : data.code === expectedCode);
                results.push({ name, status: response.status, code: data.code ?? null, passed: response.status === expectedStatus && codeMatches });
            }
            const statusResponse = await fetch('/api/storefront/checkout/phone-verification-status?phone=invalid', { headers, credentials: 'include' });
            const status = await statusResponse.json();
            results.push({ name: 'phone-policy-status', status: statusResponse.status, isRequired: status.isRequired, isVerified: status.isVerified, passed: statusResponse.status === 200 && typeof status.isRequired === 'boolean' && typeof status.isVerified === 'boolean' && (!status.isRequired || !status.isVerified) });
            return results;
        }, identity);
        cases.push(...result);
        return { mode: 'real restarted API, negative/safe read-only contracts, fresh guest browser context', cases, blocked, errors, failures: cases.filter(c => !c.passed).length, realBusinessMutations: 0, realMessagesRequested: 0 };
    } catch (error) { return { cases, blocked, errors, error: String(error) }; }
    finally { await context.close(); }
}
