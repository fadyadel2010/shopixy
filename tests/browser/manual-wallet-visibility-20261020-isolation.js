async page => {
    const admin = page.context().pages().find(p => p.url().startsWith('http://localhost:5173/'));
    const storefront = page.context().pages().find(p => p.url().startsWith('http://ahramstore.localhost:5174/'));
    const result = await admin.evaluate(async () => {
        const api = (await import('/src/services/api.js')).default;
        const foreign = '11111111-1111-4111-8111-111111111111';
        const original = (await api.get('/admin/payment-methods/MANUALWALLET')).data;
        const probes = [];
        for (const [method, url, data] of [
            ['get', '/admin/payment-methods/MANUALWALLET'],
            ['put', '/admin/payment-methods/MANUALWALLET/configure', {configuration: {walletNumber: '01000000088'}}],
            ['get', '/admin/orders/f17895bc-b5b0-43fb-87c9-cdb9da69494d'],
            ['post', '/admin/payments/manual-wallet/82d45236-e358-4415-a129-f9a804e7c2d1/confirm', {}],
            ['get', '/admin/payments/manual-wallet/proof-image/82d45236-e358-4415-a129-f9a804e7c2d1']
        ]) {
            try { const r = await api.request({method, url, data, headers: {'X-Tenant-Id': foreign}}); probes.push({method, url, status: r.status}); }
            catch (e) { probes.push({method, url, status: e.response?.status, code: e.response?.data?.code}); }
        }
        const invalid = [];
        for (const value of ['', 'not-a-wallet']) {
            try { const r = await api.put('/admin/payment-methods/MANUALWALLET/configure', {configuration: {walletNumber: value}}); invalid.push({status: r.status}); }
            catch (e) { invalid.push({status: e.response?.status, body: e.response?.data}); }
        }
        const unchanged = JSON.stringify(original) === JSON.stringify((await api.get('/admin/payment-methods/MANUALWALLET')).data);
        const proof = await api.get('/admin/payments/manual-wallet/proof-image/82d45236-e358-4415-a129-f9a804e7c2d1', {responseType: 'blob'});
        const dataUrl = await new Promise(resolve => { const reader = new FileReader(); reader.onload = () => resolve(reader.result); reader.readAsDataURL(proof.data); });
        const proofTab = window.open('', '_blank');
        proofTab.document.title = 'Actual retained synthetic proof — Admin API readback';
        const image = proofTab.document.createElement('img'); image.src = dataUrl; image.alt = 'Actual TEST ONLY uploaded proof'; proofTab.document.body.append(image);
        return {probes, invalid, unchanged, proof: {status: proof.status, type: proof.data.type, size: proof.data.size}};
    });
    const proofPage = page.context().pages().find(p => p.url() === 'about:blank');
    await proofPage.locator('img').waitFor();
    await proofPage.waitForFunction(() => document.querySelector('img')?.complete && document.querySelector('img').naturalWidth > 0);
    await proofPage.screenshot({path: '.ai/evidence/manual-wallet-visibility-20261020/actual-retained-proof-readback.png', fullPage: true});
    await proofPage.close();
    const publicProbes = await storefront.evaluate(async () => {
        const loaded = performance.getEntriesByType('resource').find(e => e.name.includes('/src/bootstrap/index.ts'))?.name || '/src/bootstrap/index.ts';
        const {getRequestHeaders} = await import(loaded);
        const probes = [];
        for (const path of ['/payment-methods', '/payments/manual-wallet/config', '/payments/82d45236-e358-4415-a129-f9a804e7c2d1']) {
            const r = await fetch('/api/storefront' + path, {headers: {...getRequestHeaders(), 'X-Tenant-Id': '11111111-1111-4111-8111-111111111111'}, credentials: 'include'});
            probes.push({path, status: r.status});
        }
        return probes;
    });
    return {...result, publicProbes, passed: result.unchanged && result.probes.every(p => p.status === 403) && result.invalid.every(p => p.status === 400) && publicProbes.every(p => p.status === 403)};
}
