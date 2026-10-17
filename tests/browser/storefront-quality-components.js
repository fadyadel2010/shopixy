// Actual React components in an isolated browser document. Every API call is intercepted.
// This is not a hosted-backend or external-delivery certification.
async page => {
    const context = await page.context().browser().newContext();
    const tab = await context.newPage();
    const cases = [], failures = [], unexpectedErrors = [];
    let activeScenario = 'setup';
    let otpVerifications = 0, orderReads = 0, verificationPosts = 0, resendFails = true;
    const json = (route, body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });
    await tab.route('**/api/**', route => {
        const path = route.request().url().replace(/^https?:\/\/[^/]+/, '').split('?')[0];
        if (!path.startsWith('/api/')) return route.continue();
        if (path.endsWith('/auth/register')) return json(route, { customer: { id: '20000000-0000-4000-8000-000000000001', email: 'quality@example.test', displayName: 'Quality', emailVerified: false }, expiresAt: new Date().toISOString(), verificationEmailSent: false });
        if (path.endsWith('phone-verification-status')) return json(route, { isRequired: true, isVerified: false, formattedPhone: '+12025550123' });
        if (path.endsWith('request-phone-otp')) return json(route, { success: true, expiresInSeconds: 300, retryAfterSeconds: 60 });
        if (path.endsWith('verify-phone-otp')) { otpVerifications++; return json(route, { success: true }); }
        if (path.endsWith('/account/orders')) {
            orderReads++;
            return orderReads === 1 ? json(route, { code: 'CONTROLLED_OUTAGE' }, 503) : json(route, { orders: [], totalPages: 1, totalCount: 0 });
        }
        if (path.endsWith('/resend-verification')) return resendFails ? json(route, { code: 'REQUEST_FAILED' }, 503) : json(route, { success: true });
        if (path.endsWith('/verify-email')) {
            verificationPosts++;
            const token = route.request().postDataJSON().token;
            if (token === 'expired-token') return json(route, { code: 'TOKEN_EXPIRED' }, 400);
            if (token === 'used-token') return json(route, { code: 'TOKEN_USED' }, 400);
            return json(route, { success: true });
        }
        return json(route, { items: [], categories: [], google: false, facebook: false });
    });
    await tab.route('**/__storefront_quality_components__', route => route.fulfill({ contentType: 'text/html', body:
        '<!doctype html><html><head><meta charset="utf-8"><link rel="stylesheet" href="/src/styles/index.css"></head><body><main id="fixture"></main></body></html>' }));
    tab.on('pageerror', error => unexpectedErrors.push(error.stack ?? error.message));
    try {
        await tab.goto('http://localhost:5174/__storefront_quality_components__');
        await tab.evaluate(async () => {
            const refresh = await import('/@react-refresh'); refresh.injectIntoGlobalHook(window);
            window.$RefreshReg$ = () => {}; window.$RefreshSig$ = () => type => type;
            const React = (await import('/node_modules/.vite/deps/react.js')).default;
            const { createRoot } = (await import('/node_modules/.vite/deps/react-dom_client.js')).default;
            // Match Vite's versioned module URL so app hooks share this Router context.
            const runtimeSource = await (await fetch('/src/storefront/runtime/layout/LayoutRuntime.tsx')).text();
            const dependency = (source, path) => source.match(new RegExp('"(' + path.replaceAll('.', '\\.') + '[^" ]*)"'))[1];
            const routerUrl = runtimeSource.match(/from "([^"]*react-router-dom[^"]*)"/)[1];
            const { MemoryRouter, useLocation } = await import(routerUrl);
            const { HeaderRuntime } = await import('/src/storefront/runtime/layout/LayoutRuntime.tsx');
            const { resolveHeaderComponent } = await import('/src/storefront/themes/ThemeLayoutResolver.ts');
            const layoutSource = await (await fetch('/src/storefront/layout/StorefrontLayout.tsx')).text();
            const { ThemeProvider } = await import(dependency(layoutSource, '/src/storefront/themes/ThemeContext.tsx'));
            const { SearchRuntimeProvider } = await import('/src/components/search/SearchRuntime.tsx');
            const { VerifyEmailBanner } = await import('/src/auth/components/VerifyEmailBanner.tsx');
            const { PhoneOtpVerification } = await import('/src/components/checkout/PhoneOtpVerification.tsx');
            const { OrderHistorySection } = await import('/src/auth/components/OrderHistorySection.tsx');
            const { VerifyEmailPage } = await import('/src/auth/pages/VerifyEmailPage.tsx');
            const { RegisterPage } = await import('/src/auth/pages/RegisterPage.tsx');
            const shippingSource = await (await fetch('/src/components/checkout/ShippingStep.tsx')).text();
            const { ShippingStep } = await import('/src/components/checkout/ShippingStep.tsx');
            const { useCheckoutStore } = await import(dependency(shippingSource, '/src/store/checkoutStore.ts'));
            const { useStoreConfigStore } = await import(dependency(runtimeSource, '/src/store/index.ts'));
            const { useNavigationStore } = await import(dependency(runtimeSource, '/src/stores/navigationStore.ts'));
            const { useFooterStore } = await import(dependency(runtimeSource, '/src/stores/footerStore.ts'));
            const authHookSource = await (await fetch('/src/auth/hooks/useAuth.ts')).text();
            const { useAuthStore } = await import(dependency(authHookSource, '/src/auth/store/authStore.ts'));
            const { useLanguageStore, t } = await import(dependency(runtimeSource, '/src/i18n/index.ts'));
            const { setResolvedTenant } = await import('/src/bootstrap/tenantResolver.ts');
            const { setSessionId } = await import('/src/bootstrap/sessionManager.ts');
            setResolvedTenant('10000000-0000-4000-8000-000000000001'); setSessionId('quality-component-session');
            useNavigationStore.setState({ navigation: { header: { links: [{ label: 'Shop', path: '/products', type: 'custom' }] }, footer: { links: [] } }, loadNavigation: async () => {} });
            useFooterStore.setState({ loadFooter: async () => {} });
            useAuthStore.setState({ status: 'unauthenticated', customer: null, availableProviders: { google: false, facebook: false } });
            useStoreConfigStore.setState(s => ({ config: { ...s.config, storeName: 'Quality Store', currency: 'EGP', defaultCountryCode: 'EG', behavior: { ...s.config.behavior, enableSearch: true } }, isLoaded: true }));
            const root = createRoot(document.getElementById('fixture'));
            let key = 0;
            function Probe() { const location = useLocation(); return React.createElement('output', { id: 'route' }, location.pathname); }
            const render = (node, path = '/') => root.render(React.createElement(MemoryRouter, { key: ++key, initialEntries: [path] },
                React.createElement(ThemeProvider, null, React.createElement(SearchRuntimeProvider, null, node)), React.createElement(Probe)));
            window.quality = {
                labels: () => ({ login: t('header.login'), register: t('header.register'), account: t('header.account'), logout: t('header.logout'), menu: t('header.menu'), retry: t('common.retry') }),
                header: async (theme, authenticated, language) => {
                    useLanguageStore.getState().setLanguage(language);
                    useAuthStore.setState({ status: authenticated ? 'authenticated' : 'unauthenticated', customer: authenticated ? { id: '20000000-0000-4000-8000-000000000001', displayName: 'Quality', emailVerified: true } : null });
                    const Component = await resolveHeaderComponent(theme);
                    render(React.createElement(HeaderRuntime, { component: Component }));
                },
                banner: (language, canResend, emailSent) => {
                    useLanguageStore.getState().setLanguage(language);
                    render(React.createElement('div', { style: { maxWidth: 320, margin: 'auto', padding: 16 } }, React.createElement(VerifyEmailBanner, { email: 'quality@example.test', canResend, emailSent })));
                },
                otp: () => { useLanguageStore.getState().setLanguage('en'); window.otpVerified = false; render(React.createElement(PhoneOtpVerification, { phone: '+12025550123', onVerified: () => { window.otpVerified = true; } })); },
                orders: () => { useAuthStore.setState({ status: 'authenticated' }); render(React.createElement(OrderHistorySection)); },
                verifyPage: (token = 'synthetic-token') => render(React.createElement(React.StrictMode, null, React.createElement(VerifyEmailPage)), `/verify-email?token=${token}`),
                register: language => { useLanguageStore.getState().setLanguage(language); useAuthStore.setState({ status: 'unauthenticated', customer: null, isLoading: false }); render(React.createElement(RegisterPage), '/register?return=%2Fproducts'); },
                shipping: country => {
                    useLanguageStore.getState().setLanguage('en');
                    useCheckoutStore.setState(s => ({ address: { ...s.address, country, phone: '' }, checkoutFields: [], checkoutFieldsTotalCount: 0, checkoutFieldsStatus: 'EMPTY', checkoutFieldsLoading: false, shippingMethods: [], shippingMethodsStatus: 'EMPTY', shippingMethodsLoading: false }));
                    render(React.createElement(ShippingStep, { onContinue: () => {} }));
                },
                submittedPhone: () => useCheckoutStore.getState().address.phone,
            };
        });
        for (const theme of ['modern', 'minimal', 'fashion', 'pearl', 'phoenix', 'hype', 'alizon', 'sharp', 'cosmetics', 'seven']) {
            for (const language of ['en', 'ar']) for (const authenticated of [false, true]) for (const width of [1280, 820, 390]) {
                const name = `${theme}/${language}/${authenticated ? 'account' : 'guest'}/${width}`;
                activeScenario = name;
                await tab.setViewportSize({ width, height: 900 });
                await tab.evaluate(async x => window.quality.header(...x), [theme, authenticated, language]);
                const labels = await tab.evaluate(() => window.quality.labels());
                const header = tab.locator('header'); await header.waitFor();
                let control = header.locator(authenticated ? 'a[href="/account"]' : 'a[href^="/login"]');
                if (!await control.first().isVisible()) {
                    const menu = header.getByRole('button', { name: new RegExp(`^(Open menu|${labels.menu})$`) });
                    await menu.click();
                    control = tab.locator(authenticated ? 'a[href="/account"]:visible' : 'a[href^="/login"]:visible');
                }
                await control.first().waitFor({ state: 'visible' });
                if (authenticated && !await control.first().getAttribute('aria-label') && width !== 390 && !(theme === 'cosmetics' && width === 820)) failures.push(`${name}: unnamed account icon`);
                if (!authenticated) {
                    const register = tab.locator('a[href^="/register"]:visible');
                    if (!await register.count()) failures.push(`${name}: register unavailable`);
                    if (width === 1280) {
                        await control.first().click();
                        await tab.waitForFunction(() => document.getElementById('route').textContent === '/login', { timeout: 3000 });
                        await register.first().click();
                        await tab.waitForFunction(() => document.getElementById('route').textContent === '/register', { timeout: 3000 });
                    }
                }
                const layout = await tab.evaluate(() => ({ width: innerWidth, scroll: document.documentElement.scrollWidth }));
                if (layout.scroll > width + 1) failures.push(`${name}: overflow ${layout.scroll}`);
                cases.push(name);
            }
        }
        for (const language of ['en', 'ar']) for (const width of [1280, 820, 390]) {
            await tab.setViewportSize({ width, height: 900 });
            await tab.evaluate(l => window.quality.banner(l, false, false), language);
            const banner = tab.locator('div[role="status"]'); await banner.waitFor();
            const textWidth = await banner.locator('p').first().evaluate(e => e.getBoundingClientRect().width);
            if (textWidth < 150) failures.push(`banner/${language}/${width}: constrained text ${textWidth}`);
            if (await banner.locator('button').count()) failures.push('unauthenticated resend button');
            await tab.screenshot({ path: `.ai/evidence/storefront-quality-20261015/verification-${language}-${width}.png` });
            cases.push(`banner/${language}/${width}`);
        }
        await tab.evaluate(() => window.quality.banner('en', true, undefined));
        await tab.getByRole('button', { name: 'Resend verification email', exact: true }).click();
        await tab.getByText('Could not send verification email.', { exact: false }).waitFor();
        resendFails = false;
        await tab.getByRole('button', { name: 'Resend verification email', exact: true }).click();
        await tab.getByText('Verification email sent!', { exact: false }).waitFor();
        cases.push('resend/failure-then-success');
        await tab.evaluate(() => window.quality.otp());
        await tab.getByRole('button', { name: 'Send Verification Code' }).click();
        await tab.getByLabel('Enter 6-digit code').fill('123456');
        await tab.getByRole('button', { name: 'Verify and Continue' }).click();
        await tab.waitForFunction(() => window.otpVerified === true);
        await tab.waitForTimeout(400); // Exceeds the auto-submit timer, to detect a second request.
        if (otpVerifications !== 1) failures.push(`OTP duplicate verification: ${otpVerifications}`);
        cases.push('otp/auto-and-manual-single-submit');
        await tab.evaluate(() => window.quality.orders());
        await tab.getByRole('button', { name: 'Retry', exact: true }).click();
        await tab.getByText('No orders yet', { exact: false }).waitFor();
        if (orderReads !== 2) failures.push(`order retry count: ${orderReads}`);
        cases.push('orders/retry-recovers');
        await tab.evaluate(() => window.quality.verifyPage());
        await tab.getByRole('heading', { name: 'Email Verified!', exact: true }).waitFor();
        if (verificationPosts !== 1) failures.push(`StrictMode verification posts: ${verificationPosts}`);
        cases.push('email/single-use-link-StrictMode');
        for (const [token, text] of [['expired-token', 'This verification link has expired. Please request a new one.'], ['used-token', 'This verification link has already been used.']]) {
            await tab.evaluate(token => window.quality.verifyPage(token), token);
            await tab.getByText(text, { exact: true }).waitFor();
            cases.push(`email/${token}-error-code`);
        }
        for (const language of ['en', 'ar']) for (const width of [1280, 820, 390]) {
            activeScenario = `registration/${language}/${width}`;
            await tab.setViewportSize({ width, height: 900 });
            await tab.evaluate(l => window.quality.register(l), language);
            await tab.locator('#register-name').fill('Quality');
            await tab.locator('#register-email').fill('quality@example.test');
            await tab.locator('#register-password').fill('SyntheticPassword1');
            await tab.locator('#register-confirm-password').fill('SyntheticPassword1');
            await tab.locator('button[type="submit"]').click();
            await tab.getByRole('heading', { name: language === 'en' ? 'Account created!' : 'تم إنشاء الحساب!', exact: true }).waitFor();
            const confirmation = tab.locator('div[role="status"]');
            if (await confirmation.locator('button').count()) failures.push(`${activeScenario}: guest resend`);
            if ((await confirmation.locator('p').first().boundingBox()).width < 150) failures.push(`${activeScenario}: compressed text`);
            await tab.screenshot({ path: `.ai/evidence/storefront-quality-20261015/registration-${language}-${width}.png` });
            cases.push(activeScenario);
        }
        await tab.waitForTimeout(3300); // The old forced redirect fired after three seconds.
        if (await tab.locator('#route').textContent() !== '/register') failures.push('registration auto-redirect returned');
        for (const [country, input, expected] of [['EG', '01022174803', '+201022174803'], ['SA', '0551234567', '+966551234567'], ['AE', '0501234567', '+971501234567'], ['US', '(202) 555-1234', '+12025551234'], ['EG', '+447911123456', '+447911123456']]) {
            activeScenario = `shipping/phone/${country}/${input.startsWith('+') ? 'international' : 'national'}`;
            await tab.evaluate(country => window.quality.shipping(country), country);
            await tab.locator('#checkout-phone').fill(input);
            await tab.locator('#checkout-phone').blur();
            const submitted = await tab.evaluate(() => window.quality.submittedPhone());
            if (submitted !== expected) failures.push(`${activeScenario}: normalization mismatch`);
            cases.push(activeScenario);
        }
        return { mode: 'isolated real components, intercepted HTTP, no external delivery or business mutations', passed: cases.length, cases, failures, unexpectedErrors, otpVerifications, orderReads, verificationPosts };
    } catch (error) {
        return { activeScenario, completed: cases.length, failures, unexpectedErrors, otpVerifications, orderReads, verificationPosts, headings: await tab.locator('h1,h2').allTextContents(), body: (await tab.locator('body').innerText()).slice(-1500), error: String(error) };
    } finally { await context.close(); }
}
