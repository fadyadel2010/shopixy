// One explicit synthetic Manual Wallet order. The uploaded image says NOT A
// RECEIPT; it is used for rejection, never represented as a transfer or approval.
// Do not rerun after a committed order; continue with the recorded order identity.
async page => {
    const prefix = 'P5QA20261017-817';
    const checks = [], failures = [], requests = [];
    const check = (name, passed, data = {}) => { checks.push({ name, passed: !!passed, ...data }); if (!passed) throw new Error(name); };
    const storefront = await page.context().newPage();
    const admin = (method, path, data) => page.evaluate(async ({ method, path, data }) => {
        const api = (await import('/src/services/api.js')).default;
        try { const r = await api.request({ method, url: path, data }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message } }; }
    }, { method, path, data });
    const sf = (method, path, data) => storefront.evaluate(async ({ method, path, data }) => {
        const { httpClient } = await import('/src/services/api/httpClient.ts');
        try { const r = await httpClient.request({ method, url: '/api' + path, data }); return { status: r.status, data: r.data }; }
        catch (e) { return { status: e.response?.status || 0, data: { code: e.response?.data?.code, message: e.response?.data?.message } }; }
    }, { method, path, data });
    let orderId, paymentIntentId, cartId, methodId, submitted = false;
    storefront.on('response', r => { if (r.url().includes('/api/')) requests.push({ path: r.url().replace(/^https?:\/\/[^/]+/, '').split('?')[0], status: r.status(), method: r.request().method() }); });
    try {
        const old = await admin('get', '/admin/orders?search=' + prefix + '&pageSize=50');
        check('No prior synthetic checkout exists; refusing blind replay', old.status === 200 && old.data.items.length === 0);
        const accounts = await admin('get', '/admin/whatsapp/accounts');
        check('Existing QA-disabled WhatsApp gate preserved before new order', accounts.status === 200 && accounts.data.every(a => !a.enabled));
        await storefront.goto('http://ahramstore.localhost:5174/cart');
        await storefront.locator('main').waitFor();
        const before = await sf('get', '/storefront/cart');
        check('Actual customer-owned cart starts empty', before.status === 200 && before.data.ownerType.toLowerCase() === 'customer' && before.data.items.length === 0);
        const products = await sf('get', '/storefront/products/discover?pageSize=20');
        let added;
        for (const product of products.data.items) {
            added = await sf('post', '/storefront/cart/items', { productId: product.id || product.productId, quantity: 1 });
            if (added.status === 200) break;
        }
        check('New synthetic line accepted by actual cart authority', added.status === 200 && added.data.items.length === 1, { status: added.status, code: added.data.code, subtotal: added.data.subtotal, currency: added.data.currency });
        cartId = added.data.cartId;
        await storefront.goto('http://ahramstore.localhost:5174/checkout');
        await storefront.locator('main').waitFor();
        await storefront.getByText(added.data.items[0].name, { exact: true }).first().waitFor();
        const state = await storefront.evaluate(async prefix => {
            const url = performance.getEntriesByType('resource').find(e => /\/store\/checkoutStore\.ts(?:\?|$)/.test(e.name))?.name;
            const { useCheckoutStore } = await import(url || '/src/store/checkoutStore.ts');
            const s = useCheckoutStore.getState();
            s.setAddress({ fullName: prefix + ' REJECT TEST', phone: '+201000000099', city: 'QA City', area: 'QA Area', addressLine: 'Synthetic QA address - no delivery', country: 'EG', notes: prefix + ' NOT A TRANSFER' });
            await s.fetchCheckoutFields(); await s.initCheckoutSession(); await s.fetchShippingMethods();
            return { methods: useCheckoutStore.getState().shippingMethods.map(m => ({ id: m.id, name: m.name })), status: useCheckoutStore.getState().shippingMethodsStatus, error: useCheckoutStore.getState().shippingMethodsError };
        }, prefix);
        const method = state.methods.find(m => m.name === prefix + ' Delivery'); methodId = method?.id;
        check('Actual checkout consumer includes configured synthetic shipping method', !!method, { methodId, state });
        await storefront.locator('input[name=shippingMethod][value="' + methodId + '"]').check();
        await storefront.getByRole('button', { name: /Continue to Payment/i }).click();
        await storefront.getByText(prefix + ' TEST ONLY Wallet', { exact: true }).click();
        await storefront.locator('input[type=file]').waitFor({ state: 'attached' });
        await storefront.evaluate(async () => {
            const url = performance.getEntriesByType('resource').find(e => /\/store\/checkoutStore\.ts(?:\?|$)/.test(e.name))?.name;
            const { useCheckoutStore } = await import(url || '/src/store/checkoutStore.ts');
            useCheckoutStore.getState().setManualWalletType('VODAFONECASH');
            useCheckoutStore.getState().setManualWalletPhoneNumber('01000000000');
            const canvas = document.createElement('canvas'); canvas.width = 720; canvas.height = 200;
            const ctx = canvas.getContext('2d'); ctx.fillStyle = '#fff'; ctx.fillRect(0, 0, 720, 200);
            ctx.fillStyle = '#900'; ctx.font = 'bold 28px sans-serif'; ctx.fillText('SYNTHETIC QA - NOT A RECEIPT', 15, 70); ctx.fillText('NO MONEY TRANSFERRED - REJECT TEST', 15, 125);
            const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
            const transfer = new DataTransfer(); transfer.items.add(new File([blob], 'P5QA-NOT-A-RECEIPT.png', { type: 'image/png' }));
            const input = document.querySelector('input[type=file]'); input.files = transfer.files; input.dispatchEvent(new Event('change', { bubbles: true }));
        });
        await storefront.waitForFunction(() => !document.querySelector('[disabled]')?.textContent?.includes('Uploading'));
        await storefront.getByRole('button', { name: /Review Order|Continue to Review/i }).click();
        const place = storefront.getByRole('button', { name: /Place Order/i }); await place.waitFor();
        const response = storefront.waitForResponse(r => /\/api\/storefront\/checkout$/.test(r.url()) && r.request().method() === 'POST', { timeout: 60000 });
        submitted = true; await place.click();
        const created = await response;
        let body; try { body = await created.json(); } catch { body = {}; }
        orderId = body.orderId;
        check('Actual Place Order response accepted once', created.status() === 200 && !!orderId, { status: created.status(), code: body.code, orderId, total: body.totalAmount });
        const detail = await admin('get', '/admin/orders/' + orderId);
        check('Manual payment persists awaiting verification, not paid', detail.status === 200 && detail.data.paymentStatus === 'AwaitingVerification', { orderId, total: detail.data.totals.total, currency: detail.data.currency });
        const queue = await admin('get', '/admin/payments/manual-wallet/pending');
        const payment = queue.data.items.find(p => p.orderId === orderId); paymentIntentId = payment?.paymentIntentId;
        check('Authoritative queue links exact order, amount and stored QA image', !!payment && payment.hasProofFile && payment.amount === detail.data.totals.total, { paymentIntentId });
        await page.goto('http://localhost:5173/ecommerce/manual-payments');
        const row = page.getByRole('row').filter({ has: page.getByText(payment.orderNumber, { exact: true }) });
        await row.getByRole('button', { name: /proof of payment/i }).click();
        await page.locator('img[alt]').last().waitFor();
        await page.getByRole('button', { name: 'Close', exact: true }).last().click();
        await row.getByRole('button', { name: 'Reject', exact: true }).click();
        await page.locator('#manual-payment-rejection-reason').fill(prefix + ' Synthetic negative test: image explicitly NOT A RECEIPT; no money transferred.');
        await page.getByRole('button', { name: 'Reject Payment', exact: true }).click();
        await row.waitFor({ state: 'hidden' });
        const rejected = await admin('get', '/admin/orders/' + orderId);
        check('Actual Manual Payments rejection persisted without collection', rejected.data.paymentStatus === 'Rejected', { status: rejected.data.status, paymentStatus: rejected.data.paymentStatus });
        const repeat = await admin('post', '/admin/payments/manual-wallet/' + paymentIntentId + '/reject', { reason: prefix + ' repeated rejection' });
        check('Repeated review is safely refused or idempotent', [200, 409].includes(repeat.status), { status: repeat.status, code: repeat.data?.code });
        const cancel = await admin('post', '/admin/orders/' + orderId + '/cancel', { reason: prefix + ' rejected QA order cleanup' });
        check('Supported cancellation requested for rejected QA order', cancel.status === 200, { status: cancel.status });
    } catch (e) { failures.push({ message: e.message.split('\n')[0] }); }
    finally {
        if (cartId) {
            const current = await sf('get', '/storefront/cart');
            if (current.status === 200 && current.data.cartId === cartId) {
                await sf('delete', '/storefront/cart');
                const clean = await sf('get', '/storefront/cart');
                checks.push({ name: 'Exact synthetic customer cart cleaned with fresh readback', passed: clean.status === 200 && clean.data.items.length === 0 });
            }
        }
        await storefront.close();
    }
    return { prefix, orderId, paymentIntentId, cartId, methodId, submitted, checks, failures, requests };
}
