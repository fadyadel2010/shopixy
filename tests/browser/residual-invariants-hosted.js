// Read-only product/variant observations against the existing tenant. Only the
// variant-resolution POST (a read contract) and rejected input probes are used.
async page => {
    const tab = await page.context().newPage();
    try {
        await tab.goto('http://ahramstore.localhost:5174/products');
        const observation = await tab.evaluate(async () => {
            const calls = [];
            const csrf = decodeURIComponent(document.cookie.split(';').map(value => value.trim()).find(value => value.startsWith('csrf_token='))?.slice(11) || '');
            const headers = response => Object.fromEntries(['cache-control', 'pragma', 'expires', 'surrogate-control', 'etag', 'last-modified'].map(name => [name, response.headers.get(name)]));
            const request = async (path, body) => {
                const response = await fetch('/api/storefront/products/' + path, {
                    credentials: 'include',
                    ...(body === undefined ? {} : { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf }, body: JSON.stringify(body) }),
                });
                let data; try { data = await response.json(); } catch { data = null; }
                calls.push({ path, method: body === undefined ? 'GET' : 'POST', http: response.status, headers: headers(response), code: data?.code ?? null });
                return { http: response.status, data };
            };
            const discovery = await request('discover?pageSize=12');
            const items = discovery.data?.items ?? [];
            const products = [];
            for (const item of items.slice(0, 3)) {
                const { data: product, http } = await request('by-id/' + item.id);
                if (http !== 200) continue;
                if (product.slug) await request(encodeURIComponent(product.slug));
                const selectedOptions = Object.fromEntries((product.variants?.find(v => v.isDefault)?.attributes ?? product.variants?.[0]?.attributes ?? []).map(a => [a.name, a.value]));
                const resolution = await request(`by-id/${product.id}/variant-resolution`, { selectedOptions });
                const forged = await request(`by-id/${product.id}/variant-resolution`, { selectedOptions, variantId: '11111111-1111-4111-8111-111111111111', price: 0.01, availability: { canPurchase: true } });
                await request(`by-id/${product.id}/variant-resolution`, { selectedOptions: { '__unknown_option__': 'invalid' } });
                await request(`by-id/${product.id}/variant-resolution`, { selectedOptions: 'invalid-shape' });
                products.push({ id: product.id, slug: product.slug, name: product.name, descriptionPresent: !!product.description, categoryName: product.categoryName,
                    price: product.price, effectivePrice: product.effectivePrice, pricing: product.pricing, availability: product.availability,
                    hasVariants: product.hasVariants, variants: product.variants?.map(v => ({ id: v.id, attributes: v.attributes, pricing: v.pricing, availability: v.availability, isActive: v.isActive })),
                    media: product.media?.map(m => ({ id: m.id, mediaType: m.mediaType, hasUrl: !!m.url, hasPresentationUrl: !!m.presentationUrl })),
                    resolution: { http: resolution.http, productId: resolution.data?.productId, resolvedVariantId: resolution.data?.resolvedVariantId, variantId: resolution.data?.variant?.id, pricing: resolution.data?.variant?.pricing, availability: resolution.data?.variant?.availability },
                    forgedCommercialFieldsIgnored: forged.http === 200 && forged.data?.resolvedVariantId === resolution.data?.resolvedVariantId && JSON.stringify(forged.data?.variant?.pricing) === JSON.stringify(resolution.data?.variant?.pricing) && JSON.stringify(forged.data?.variant?.availability) === JSON.stringify(resolution.data?.variant?.availability), selectedOptions });
            }
            await request('__phase4_missing_product__');
            await request('by-id/11111111-1111-4111-8111-111111111111');
            await request('by-id/11111111-1111-4111-8111-111111111111/variant-resolution', { selectedOptions: {} });
            if (products[0]) {
                const spoof = await fetch(`/api/storefront/products/by-id/${products[0].id}/variant-resolution`, { method: 'POST', credentials: 'include',
                    headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf, 'X-Tenant-Id': '11111111-1111-4111-8111-111111111111' }, body: JSON.stringify({ selectedOptions: {} }) });
                const data = await spoof.json();
                calls.push({ path: 'foreign-tenant-header/variant-resolution', method: 'POST', http: spoof.status, code: data.code, headers: headers(spoof) });
                const conditional = await fetch(`/api/storefront/products/by-id/${products[0].id}`, { headers: { 'If-None-Match': '"stale"' }, credentials: 'include' });
                calls.push({ path: 'conditional/by-id', method: 'GET', http: conditional.status, headers: headers(conditional) });
            }
            return { observedUtc: new Date().toISOString(), layer: 'actual-hosted-API', discoveryHttp: discovery.http, products, calls, businessMutations: 0 };
        });
        const chosen = observation.products.find(p => p.hasVariants) ?? observation.products[0];
        if (chosen) {
            await tab.goto('http://ahramstore.localhost:5174/products/' + encodeURIComponent(chosen.slug || chosen.id));
            await tab.locator('[data-product-template]').waitFor();
            observation.browser = await tab.evaluate(expected => ({
                url: location.pathname,
                nameVisible: document.body.textContent.includes(expected.name),
                productContextId: document.querySelector('#ai-product-context')?.getAttribute('data-ai-product-id'),
                mediaCount: document.querySelector('[data-product-template]')?.getAttribute('data-product-media-count'),
                template: document.querySelector('[data-product-template]')?.getAttribute('data-product-template'),
                buttons: [...document.querySelectorAll('button')].filter(b => /cart|option|stock/i.test(b.textContent)).map(b => ({ label: b.textContent.trim(), disabled: b.disabled })),
            }), chosen);
        }
        return observation;
    } finally { await tab.close(); }
}
