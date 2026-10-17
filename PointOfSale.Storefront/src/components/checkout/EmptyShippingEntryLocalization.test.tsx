import { renderToStaticMarkup } from 'react-dom/server';
import type { ReactNode } from 'react';
import { afterEach, expect, it, vi } from 'vitest';
import { t } from '@/i18n';
import { useLanguageStore } from '@/i18n/languageStore';
import { CartEmptyState } from '../cart/CartEmptyState';
import { CheckoutEmptyState } from './CheckoutEmptyState';

// Styling-only Button substitute avoids the unrelated common barrel's browser
// analytics startup in node SSR. The actual copy and translation hooks are tested.
vi.mock('@/components/common', () => ({ Button: ({ children }: { children: ReactNode }) => <button>{children}</button> }));

vi.mock('@/i18n', async importOriginal => {
    const actual = await importOriginal<typeof import('@/i18n')>();
    return { ...actual, useTranslation: () => {
        const language = useLanguageStore.getState().language;
        return { language, t: (key: string) => actual.t(key, {}, language) };
    } };
});
afterEach(() => useLanguageStore.setState({ language: 'en' }));

it.each(['en', 'ar'] as const)('empty cart and checkout localize the shipping entry boundary in %s', language => {
    useLanguageStore.setState({ language });
    const cart = renderToStaticMarkup(<CartEmptyState />);
    const checkout = renderToStaticMarkup(<CheckoutEmptyState />);
    for (const markup of [cart, checkout]) {
        expect(markup).toContain(t('cart.empty', {}, language));
        const escapedDescription = renderToStaticMarkup(<span>{t('cart.emptyDescription', {}, language)}</span>).slice(6, -7);
        expect(markup).toContain(escapedDescription);
        expect(markup).toContain(t('cart.continueShopping', {}, language));
        expect(markup).toContain('href="/products"');
        expect(markup).not.toContain('href="/checkout"');
        expect(markup).toContain(`dir="${language === 'ar' ? 'rtl' : 'ltr'}"`);
    }
    if (language === 'ar') {
        expect(cart).not.toContain('Your cart is empty');
        expect(checkout).not.toContain('Browse Products');
    }
});
