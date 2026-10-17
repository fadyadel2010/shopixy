import { describe, expect, it } from 'vitest';
import * as fs from 'fs';
import * as path from 'path';

const STOREFRONT_SRC = path.resolve(__dirname, '../../../PointOfSale.Storefront/src');

function readStorefront(relativePath: string): string {
    return fs.readFileSync(path.join(STOREFRONT_SRC, relativePath), 'utf-8');
}

function getSourceFiles(dir: string, fileList: string[] = []): string[] {
    for (const entry of fs.readdirSync(dir)) {
        const filePath = path.join(dir, entry);
        const stat = fs.statSync(filePath);

        if (stat.isDirectory()) {
            if (!entry.includes('node_modules') && !entry.includes('dist')) {
                getSourceFiles(filePath, fileList);
            }
            continue;
        }

        if (/\.(ts|tsx)$/.test(entry)) {
            fileList.push(filePath);
        }
    }

    return fileList;
}

function normalize(filePath: string): string {
    return filePath.replace(/\\/g, '/');
}

describe('Affiliate checkout frontend certification', () => {
    const storefrontFiles = getSourceFiles(STOREFRONT_SRC);
    const affiliateFrontendFiles = storefrontFiles.filter((file) => {
        const normalized = normalize(file);
        const basename = path.basename(file);

        return (
            basename.includes('Affiliate') ||
            normalized.includes('/pages/landing/') ||
            normalized.includes('/hooks/useAffiliate') ||
            normalized.includes('/contexts/AffiliateContext') ||
            normalized.includes('/bootstrap/affiliateRefTracker')
        );
    });

    it('routes affiliate entry through the certified checkout route without a duplicate checkout page', () => {
        const router = readStorefront('app/router.tsx');
        const funnelHook = readStorefront('hooks/useAffiliateFunnel.ts');
        const endpoints = readStorefront('services/api/endpoints.ts');

        expect(router).toContain("path: '/p/:referralCode/:slug'");
        expect(router).toContain('<AffiliateLandingPage />');
        expect(router).toContain("path: '/checkout'");
        expect(router).toContain('<CheckoutPage />');
        expect(router).not.toContain('ThemePageRoute');
        expect(funnelHook).toContain('startAffiliateFunnel(request');
        expect(funnelHook).toContain('navigate(response.checkoutUrl)');
        expect(endpoints).toContain("FUNNEL_START: '/api/storefront/funnel/start'");
    });

    it('reuses certified checkout components and does not define affiliate replacement components', () => {
        const checkoutPage = readStorefront('pages/CheckoutPage.tsx');
        const checkoutIndex = readStorefront('components/checkout/index.ts');
        const storefrontFileNames = storefrontFiles.map((file) => path.basename(file));

        [
            'CheckoutStepper',
            'ShippingStep',
            'PaymentStep',
            'ReviewStep',
            'OrderSummary',
            'CheckoutSkeleton',
            'CheckoutEmptyState',
        ].forEach((componentName) => {
            expect(checkoutPage).toContain(componentName);
            expect(checkoutIndex).toContain(componentName);
        });

        expect(storefrontFileNames).not.toContain('AffiliateCheckoutPage.tsx');
        expect(storefrontFileNames).not.toContain('AffiliateShippingStep.tsx');
        expect(storefrontFileNames).not.toContain('AffiliatePaymentStep.tsx');
        expect(storefrontFileNames).not.toContain('AffiliateOrderSummary.tsx');
    });

    it('preserves referral candidates across landing, navigation, checkout, refresh, pending, and tracking', () => {
        const tracker = readStorefront('bootstrap/affiliateRefTracker.ts');
        const httpClient = readStorefront('services/api/httpClient.ts');
        const affiliateLink = readStorefront('components/common/AffiliateLink.tsx');
        const affiliateNavigation = readStorefront('hooks/useAffiliateNavigation.ts');
        const checkoutPage = readStorefront('pages/CheckoutPage.tsx');
        const pendingPage = readStorefront('pages/CheckoutPendingPage.tsx');

        expect(tracker).toContain("urlParams.get('ref')");
        expect(tracker).toContain("path.match(/^\\/p\\/([^\\/]+)\\//)");
        expect(tracker).toContain('sessionStorage.setItem');
        expect(tracker).toContain('sessionStorage.getItem');
        expect(httpClient).toContain("config.headers['X-Affiliate-Ref'] = affiliateRef");
        expect(affiliateNavigation).toContain("params.set('ref', affiliateRef)");
        expect(affiliateLink).toContain('useBuildAffiliateUrl');
        expect(checkoutPage).toContain('buildAffiliateUrl(buildPendingCheckoutUrl(orderResult))');
        expect(pendingPage).toContain('useAffiliateNavigate');
        expect(pendingPage).toContain('<AffiliateLink');
        expect(pendingPage).toContain('`/orders/${orderId}/track`');
    });

    it('renders shipping dynamically from certified checkout shipping APIs with no affiliate carrier authority', () => {
        const checkoutStore = readStorefront('store/checkoutStore.ts');
        const shippingStep = readStorefront('components/checkout/ShippingStep.tsx');
        const affiliateCombined = affiliateFrontendFiles
            .map((file) => fs.readFileSync(file, 'utf-8'))
            .join('\n');

        expect(checkoutStore).toContain("SHIPPING_METHODS: '/api/storefront/shipping-methods'");
        expect(checkoutStore).toContain('admitStorefrontShippingMethodsPreview(response.data)');
        expect(shippingStep).toContain('shippingMethods.map((method)');
        expect(shippingStep).toContain("name={language === 'ar' && method.nameAr?.trim() ? method.nameAr : method.name}");
        expect(shippingStep).toContain('price={method.price}');
        expect(affiliateCombined).not.toMatch(/fedex|dhl|bosta|aramex|mylerz/i);
        expect(affiliateCombined).not.toMatch(/shippingCalculation|calculateShipping|carrierSelection/i);
    });

    it('renders payment dynamically from certified checkout payment APIs with no affiliate payment authority', () => {
        const checkoutStore = readStorefront('store/checkoutStore.ts');
        const paymentStep = readStorefront('components/checkout/PaymentStep.tsx');
        const affiliateCombined = affiliateFrontendFiles
            .map((file) => fs.readFileSync(file, 'utf-8'))
            .join('\n');

        expect(checkoutStore).toContain("PAYMENT_METHODS: '/api/storefront/payment-methods'");
        expect(checkoutStore).toContain('admitStorefrontPaymentMethods(response.data)');
        expect(paymentStep).toContain('paymentMethods.map((method)');
        expect(paymentStep).toContain('method.displayName');
        expect(paymentStep).toContain('method.isAvailable');
        expect(affiliateCombined).not.toMatch(/confirmPayment|paymentIntent|clientSecret|paymobIframe|stripe|paymob|fawry/i);
    });
});
