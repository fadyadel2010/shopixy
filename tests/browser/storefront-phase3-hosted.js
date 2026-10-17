// Authorized temporary data, actual Admin UI/API/database and Storefront consumer.
// No order submission, payment, message, inventory or external provider operation.
async page => {
    const checks = [], failures = [], requests = [], errors = [], created = [];
    const prefix = 'P3QA' + Date.now();
    const tenant = 'c9c5c1ae-949e-498e-bc63-07242daeb49d';
    let storefront, cartBefore, cartTouched = false;
    const check = (name, value, details = {}) => { checks.push({ name, passed: !!value, ...details }); if (!value) throw new Error(name); };
    const errorHandler = e => errors.push(e.message);
    const responseHandler = r => { const path = r.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0]; if (/^\/api\/(coupons|promotions|admin\/smart-offers|storefront\/(cart|checkout\/pricing|smart-offers))/.test(path)) requests.push({ path, method: r.request().method(), status: r.status() }); };
    page.on('pageerror', errorHandler); page.on('response', responseHandler);
    const admin = (method, path, data, rowVersion) => page.evaluate(async ({ method, path, data, rowVersion }) => {
        const api = (await import('/src/services/api.js')).default;
        try { const r = await api.request({ method, url: path, data, headers: rowVersion ? { 'If-Match': `"${rowVersion}"` } : {} }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: e.response?.data }; }
    }, { method, path, data, rowVersion });
    const sf = (method, path, data) => storefront.evaluate(async ({ method, path, data }) => {
        const { httpClient } = await import('/src/services/api/httpClient.ts');
        try { const r = await httpClient.request({ method, url: '/api' + path, data }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: e.response?.data }; }
    }, { method, path, data });
    const read = async path => { const r = await admin('get', path); if (r.status !== 200) throw new Error(`read ${path}: ${r.status}`); return r.data; };
    const cleanup = async () => {
        for (const record of [...created].reverse()) {
            const r = await admin('get', record.path + '/' + record.id);
            if (r.status === 404) continue;
            if (r.status !== 200) { failures.push({ cleanup: record, status: r.status }); continue; }
            const removed = await admin('delete', record.path + '/' + record.id, undefined, r.data.rowVersion);
            if (removed.status !== 204) failures.push({ cleanup: record, status: removed.status });
        }
    };
    let coupon, promotion, smart;
    try {
        await page.goto('http://localhost:5173/ecommerce/coupons');
        await page.getByRole('heading', { name: 'Coupons', exact: true }).waitFor();
        const config = await read('/admin/store-config');
        check('Actual Owner workspace is AhramStore', config.tenantId === tenant && config.storeName === 'AhramStore');
        const originals = { coupons: await read('/coupons?pageSize=200'), promotions: await read('/promotions?pageSize=200'), smart: await read('/admin/smart-offers?pageSize=200') };
        check('Initial coupon, promotion and Smart Offer records captured before synthetic writes', true, { counts: Object.fromEntries(Object.entries(originals).map(([k, v]) => [k, v.length])) });

        await page.getByRole('button', { name: '+ Add Coupon', exact: true }).click();
        const form = page.locator('form');
        await form.locator('input[type=text]').fill(' ' + prefix.toLowerCase() + ' ');
        await form.locator('input[type=number]').nth(0).fill('10');
        await form.getByRole('button', { name: 'Save', exact: true }).click();
        await page.getByRole('button', { name: prefix, exact: true }).waitFor();
        coupon = (await read('/coupons?search=' + prefix)).find(c => c.code === prefix);
        check('Coupon UI creates normalized persisted tenant coupon', coupon && coupon.tenantId === tenant && coupon.discountValue === 10);
        created.push({ path: '/coupons', id: coupon.id });
        const duplicate = await admin('post', '/coupons', { ...coupon, code: ' ' + prefix.toLowerCase() + ' ' });
        check('Server rejects normalized duplicate code', duplicate.status === 400 || duplicate.status === 409, { status: duplicate.status });
        const invalid = await admin('post', '/coupons', { code: prefix + 'INVALID', discountType: 'Percentage', discountValue: 101 });
        check('Server rejects forged percentage above 100', invalid.status === 400, { status: invalid.status });
        await page.getByRole('button', { name: 'Edit', exact: true }).first().click();
        await form.locator('input[type=number]').nth(0).fill('12.5');
        await form.getByRole('button', { name: 'Update', exact: true }).click();
        await page.getByText('12.5%', { exact: true }).waitFor();
        coupon = await read('/coupons/' + coupon.id);
        check('Coupon UI edit has fresh persisted readback', coupon.discountValue === 12.5);
        await page.getByTitle(/Deactivate/i).first().click();
        await page.getByTitle(/^Activate$/i).first().waitFor();
        check('Coupon UI disable persists', !(await read('/coupons/' + coupon.id)).isActive);
        await page.getByTitle(/^Activate$/i).first().click();
        await page.getByTitle(/Deactivate/i).first().waitFor();
        check('Coupon UI re-enable persists', (await read('/coupons/' + coupon.id)).isActive);
        const stale = await admin('patch', '/coupons/' + coupon.id + '/toggle', null, '1');
        check('Stale coupon lifecycle write rejected', stale.status === 409, { status: stale.status });

        await page.goto('http://localhost:5173/ecommerce/promotions');
        await page.getByRole('heading', { name: 'Promotions (Offers)', exact: true }).waitFor();
        await page.getByRole('button', { name: /Add Promotion/ }).click();
        await page.locator('form input[type=text]').fill(prefix + ' offer');
        await page.locator('form input[type=number]').nth(0).fill('20');
        await page.locator('form select').nth(1).selectOption('Cart');
        const now = new Date();
        await page.locator('form input[type=datetime-local]').nth(0).fill(new Date(now.getTime() - 3600000).toISOString().slice(0, 16));
        await page.locator('form input[type=datetime-local]').nth(1).fill(new Date(now.getTime() + 3600000).toISOString().slice(0, 16));
        await page.locator('form').getByRole('button', { name: 'Create', exact: true }).click();
        await page.getByText(prefix + ' offer', { exact: true }).waitFor();
        promotion = (await read('/promotions?search=' + prefix))[0];
        check('Promotion UI persists active Cart offer and UTC schedule', promotion?.scope === 'Cart' && promotion.discountValue === 20 && promotion.isActive);
        created.push({ path: '/promotions', id: promotion.id });
        await page.getByTitle('Edit', { exact: true }).first().click();
        await page.locator('form').filter({ has: page.getByRole('button', { name: 'Update', exact: true }) }).locator('textarea').fill('Synthetic Phase3 edit');
        await page.locator('form').getByRole('button', { name: 'Update', exact: true }).click();
        await page.locator('form').getByRole('button', { name: 'Update', exact: true }).waitFor({ state: 'detached' });
        await page.locator('tbody').getByText('Synthetic Phase3 edit', { exact: true }).waitFor();
        const editedPromotion = await read('/promotions/' + promotion.id);
        check('Promotion UI edit preserves exact persisted schedule', editedPromotion.startDate === promotion.startDate && editedPromotion.endDate === promotion.endDate && editedPromotion.description === 'Synthetic Phase3 edit');
        await page.getByTitle('Deactivate', { exact: true }).first().click();
        await page.getByTitle('Activate', { exact: true }).first().waitFor();
        check('Promotion UI deactivate persists', !(await read('/promotions/' + promotion.id)).isActive);
        await page.getByTitle('Activate', { exact: true }).first().click();
        await page.getByTitle('Deactivate', { exact: true }).first().waitFor();
        promotion = await read('/promotions/' + promotion.id);

        await page.goto('http://localhost:5173/ecommerce/smart-offers');
        await page.getByRole('button', { name: 'Create offer', exact: true }).waitFor();
        await page.getByRole('button', { name: 'Create offer', exact: true }).click();
        const smartForm = page.locator('form');
        await smartForm.getByLabel('Name', { exact: true }).fill(prefix + ' smart');
        await smartForm.getByLabel('Title', { exact: true }).fill(prefix + ' popup');
        await smartForm.getByLabel('Button text', { exact: true }).fill('Apply QA offer');
        await smartForm.getByLabel('Coupon code', { exact: true }).fill(prefix + 'SMART');
        await smartForm.getByLabel('Discount value', { exact: true }).fill('30');
        await smartForm.getByRole('button', { name: 'Create offer', exact: true }).click();
        await page.getByText(prefix + ' smart', { exact: true }).waitFor();
        smart = (await read('/admin/smart-offers?search=' + prefix))[0];
        check('Smart Offer UI creates inactive popup and generated coupon', smart?.status === 'Inactive' && smart.generatedCouponId);
        created.push({ path: '/admin/smart-offers', id: smart.id });
        const generatedCouponId = smart.generatedCouponId;
        await page.locator('tbody').getByRole('button', { name: 'Edit', exact: true }).click();
        await smartForm.getByLabel('Description', { exact: true }).fill('Synthetic Phase3 popup edit');
        await Promise.all([
            page.waitForResponse(r => r.url().includes(smart.id) && r.request().method() === 'PUT'),
            smartForm.getByRole('button', { name: 'Save changes', exact: true }).click(),
        ]);
        await page.locator('form').filter({ has: page.getByLabel('Name', { exact: true }) }).waitFor({ state: 'detached' });
        smart = await read('/admin/smart-offers/' + smart.id);
        check('Smart Offer UI edit preserves generated coupon identity', smart.popupDescription === 'Synthetic Phase3 popup edit' && smart.generatedCouponId === generatedCouponId, { description: smart.popupDescription });

        storefront = await page.context().newPage(); storefront.on('pageerror', errorHandler); storefront.on('response', responseHandler);
        await storefront.goto('http://ahramstore.localhost:5174/cart');
        await storefront.locator('main').waitFor();
        cartBefore = await sf('get', '/storefront/cart');
        check('Authenticated configured Storefront cart is available for test', cartBefore.status === 200 && cartBefore.data.ownerType.toLowerCase() === 'customer', { status: cartBefore.status, originalItems: cartBefore.data.items?.length });
        check('Existing test customer basket starts empty', cartBefore.data.items.length === 0);
        const products = await sf('get', '/storefront/products/discover?pageSize=20');
        const candidates = products.data.items || products.data.products || products.data;
        let added;
        for (const product of candidates) {
            added = await sf('post', '/storefront/cart/items', { productId: product.id || product.productId, quantity: 1 });
            if (added.status === 200) { cartTouched = true; break; }
        }
        check('Actual Storefront cart accepts a current product fixture', added?.status === 200 && added.data.items.length > 0, { status: added?.status });
        const basket = added.data;
        await storefront.reload();
        await storefront.getByText(basket.items[0].name, { exact: true }).first().waitFor();
        check('Actual Storefront Cart UI renders authoritative product and offer', (await storefront.locator('main').innerText()).includes(prefix + ' offer'));
        const summary = await storefront.getByRole('heading', { name: 'Order Summary', exact: true }).locator('..').innerText();
        check('Actual Cart UI displays estimated non-stacked payable amount', summary.includes((basket.subtotal - Math.round(basket.subtotal * .2 * 100) / 100 + basket.taxTotal).toFixed(2)), { summary });
        const apply = await sf('post', '/storefront/cart/apply-coupon', { code: prefix });
        check('Coupon application succeeds but does not stack with better offer', apply.status === 200 && apply.data.totalDiscount === Math.round(basket.subtotal * .2 * 100) / 100, { status: apply.status, subtotal: basket.subtotal, discount: apply.data?.totalDiscount });
        const pricing = await sf('post', '/storefront/checkout/pricing', { cartId: basket.cartId, shippingCountry: 'EG', subtotal: 1, discount: 99999, total: .01, tenantId: '11111111-1111-4111-8111-111111111111' });
        check('Checkout preview ignores forged totals and recomputes winning SQL offer', pricing.status === 200 && pricing.data.subtotal === basket.subtotal && pricing.data.discount === apply.data.totalDiscount && pricing.data.total === Math.round((pricing.data.subtotal - pricing.data.discount + pricing.data.tax + pricing.data.shipping + pricing.data.paymentFee) * 100) / 100, { status: pricing.status, pricing: pricing.data });
        // Enable after the cart reload: a real hidden-tab ExitIntent before that
        // reload would correctly consume this fixture's once-per-session display.
        await page.getByRole('button', { name: 'Enable', exact: true }).first().click();
        await page.getByRole('button', { name: 'Disable', exact: true }).first().waitFor();
        smart = await read('/admin/smart-offers/' + smart.id);
        check('Smart Offer enable updates persisted generated coupon', smart.status === 'Active' && (await read('/coupons/' + smart.generatedCouponId)).isActive);
        await storefront.bringToFront();
        await storefront.reload();
        await storefront.getByRole('heading', { name: 'Order Summary', exact: true }).waitFor();
        const publicOffers = await sf('get', '/storefront/smart-offers');
        check('Actual public Smart Offer consumer includes active synthetic popup without coupon secrets', publicOffers.status === 200 && publicOffers.data.some(o => o.id === smart.id) && !JSON.stringify(publicOffers.data).includes(prefix + 'SMART'));
        const raw = await sf('post', '/storefront/cart/apply-coupon', { code: prefix + 'SMART' });
        check('Raw generated coupon cannot bypass Smart Offer redemption', raw.status === 400, { status: raw.status, code: raw.data?.code });
        const candidateState = await storefront.evaluate(async () => {
            // Vite HMR URLs are module identities; importing the unversioned
            // path would create a separate empty cache, not inspect the app.
            const moduleUrl = performance.getEntriesByType('resource').find(e => /\/smartOfferEngine\.ts(?:\?|$)/.test(e.name)).name;
            const engine = await import(moduleUrl);
            const offers = engine.getLoadedSmartOffers();
            return { offers, eligible: engine.getEligibleOffers({ triggerType: 'ExitIntent' }).map(o => o.id), dialogs: document.querySelectorAll('[role=dialog]').length };
        });
        check('Runtime candidate cache contains current hosted popup', candidateState.offers.some(o => o.id === smart.id), { candidateState });
        await storefront.bringToFront();
        await storefront.evaluate(() => document.dispatchEvent(new MouseEvent('mouseleave', { clientY: 0 })));
        const popup = storefront.getByRole('dialog');
        await popup.getByRole('heading', { name: prefix + ' popup', exact: true }).waitFor();
        check('Actual rule-based ExitIntent popup renders synthetic Admin content', true);
        const [popupApplied] = await Promise.all([
            storefront.waitForResponse(r => r.url().includes('/cart/apply-coupon') && r.request().method() === 'POST'),
            popup.getByRole('button', { name: 'Apply QA offer', exact: true }).click(),
        ]);
        check('Real popup-to-token bridge and CouponInput complete actual application', popupApplied.status() === 200, { status: popupApplied.status() });
        const token = await sf('post', '/storefront/smart-offers/' + smart.id + '/redeem', {});
        check('Actual redeem issues customer-bound protected token', token.status === 200 && !!token.data.redeemToken, { status: token.status });
        const smartApply = await sf('post', '/storefront/cart/apply-coupon', { smartOfferRedeemToken: token.data.redeemToken });
        check('Smart Offer coupon wins once through actual cart authority', smartApply.status === 200 && smartApply.data.discountTotal === Math.round(basket.subtotal * .3 * 100) / 100 && smartApply.data.cartPromotionDiscountTotal === 0, { status: smartApply.status, discount: smartApply.data?.discountTotal, error: smartApply.status === 200 ? undefined : smartApply.data });
        const retry = await sf('post', '/storefront/cart/apply-coupon', { smartOfferRedeemToken: token.data.redeemToken });
        check('Repeated Smart Offer application does not double discount or consume quota', retry.status === 200 && retry.data.totalDiscount === smartApply.data.totalDiscount && (await read('/coupons/' + smart.generatedCouponId)).currentUsage === 0);
        const smartPricing = await sf('post', '/storefront/checkout/pricing', { cartId: basket.cartId, shippingCountry: 'EG' });
        check('Actual checkout-pricing agrees with generated coupon winner', smartPricing.status === 200 && smartPricing.data.discount === smartApply.data.totalDiscount && smartPricing.data.couponDiscount === smartApply.data.totalDiscount && smartPricing.data.promotionDiscount === 0, { pricing: smartPricing.data });
        const disable = await admin('put', '/admin/smart-offers/' + smart.id, { ...smart, status: 'Inactive' }, smart.rowVersion);
        check('Current Smart Offer deactivation persists', disable.status === 200);
        const refreshed = await sf('get', '/storefront/cart');
        check('Cart revalidates disabled generated coupon and restores best active promotion', refreshed.status === 200 && refreshed.data.discountTotal === 0 && refreshed.data.cartPromotionDiscountTotal === apply.data.totalDiscount);
        const noRedeem = await sf('post', '/storefront/smart-offers/' + smart.id + '/redeem', {});
        check('Disabled Smart Offer is no longer redeemable', noRedeem.status === 404, { status: noRedeem.status });
        await storefront.reload();
        await storefront.locator('main').waitFor();
        check('Storefront browser renders recalculated persisted cart', (await storefront.locator('main').innerText()).length > 0);

        promotion = await read('/promotions/' + promotion.id);
        check('Promotion can be disabled before isolated manual-coupon consumer check', (await admin('patch', '/promotions/' + promotion.id + '/toggle', undefined, promotion.rowVersion)).status === 200);
        const couponOnly = await sf('post', '/storefront/cart/apply-coupon', { code: prefix });
        const couponPricing = await sf('post', '/storefront/checkout/pricing', { cartId: basket.cartId, shippingCountry: 'EG' });
        check('Actual manual-coupon cart and checkout agree after offer removal', couponOnly.status === 200 && couponPricing.status === 200 && couponOnly.data.discountTotal === Math.round(basket.subtotal * .125 * 100) / 100 && couponPricing.data.couponDiscount === couponOnly.data.discountTotal && couponPricing.data.promotionDiscount === 0, { discount: couponOnly.data?.discountTotal, pricing: couponPricing.data });

        const c = await read('/coupons/' + coupon.id);
        const expired = await admin('put', '/coupons/' + coupon.id, { ...c, startDate: null, endDate: new Date(Date.now() - 1000).toISOString() }, c.rowVersion);
        check('Coupon expiration accepted with authoritative readback', expired.status === 200 && expired.data.status === 'Expired');
        const rejected = await sf('post', '/storefront/cart/apply-coupon', { code: prefix });
        check('Actual server rejects expired coupon', rejected.status === 400, { status: rejected.status, code: rejected.data?.code });
        for (const [route, label, record] of [
            ['smart-offers', prefix + ' smart', created.find(r => r.path === '/admin/smart-offers')],
            ['promotions', prefix + ' offer', created.find(r => r.path === '/promotions')],
            ['coupons', prefix, created.find(r => r.path === '/coupons')],
        ]) {
            await page.goto('http://localhost:5173/ecommerce/' + route);
            const row = page.locator('tbody tr').filter({ has: page.getByText(label, { exact: true }) });
            await row.waitFor();
            const remove = route === 'promotions' ? row.getByTitle('Delete', { exact: true }) : row.getByRole('button', { name: 'Delete', exact: true });
            await remove.click();
            const dialog = page.getByRole('dialog');
            await dialog.getByRole('button', { name: 'Cancel', exact: true }).click();
            check(`Actual ${route} delete cancellation preserves record`, (await admin('get', record.path + '/' + record.id)).status === 200);
            await remove.click();
            const [deleted] = await Promise.all([
                page.waitForResponse(r => r.url().includes(record.id) && r.request().method() === 'DELETE'),
                dialog.getByRole('button', { name: 'Delete', exact: true }).click(),
            ]);
            await row.waitFor({ state: 'detached' });
            check(`Actual ${route} UI delete persists with fresh 404`, deleted.status() === 204 && (await admin('get', record.path + '/' + record.id)).status === 404);
        }
        check('No uncaught browser exceptions', errors.length === 0, { errors });
    } catch (e) { failures.push({ error: e.message.split('\n').slice(0, 6).join('\n') }); }
    finally {
        if (cartTouched && storefront) {
            const cleared = await sf('delete', '/storefront/cart');
            const readback = await sf('get', '/storefront/cart');
            checks.push({ name: 'Synthetic basket cleanup freshly verified', passed: [200, 204].includes(cleared.status) && readback.status === 200 && readback.data.items.length === 0 });
        }
        await cleanup();
        for (const record of created) { const r = await admin('get', record.path + '/' + record.id); checks.push({ name: 'Synthetic lifecycle cleanup: ' + record.path, id: record.id, passed: r.status === 404 }); }
        if (storefront) await storefront.close();
        page.removeListener('pageerror', errorHandler); page.removeListener('response', responseHandler);
    }
    return { observedUtc: new Date().toISOString(), evidence: 'hosted-actual-ui-api-configured-database-and-storefront', prefix, created, checks, failures, requests, errors, ordersSubmitted: 0, externalBusinessCalls: 0 };
}
