import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
vi.mock('@/hooks', () => ({ useCurrency: () => ({ formatPrice: String }) }));
vi.mock('@/i18n', () => ({ useLocalizedContent: () => ({ t: (key: string) => key, localize: () => '', language: 'en' }) }));
const marketing = vi.hoisted(() => ({ addItem: vi.fn(), addToCart: vi.fn() }));
vi.mock('@/store', () => ({ useCartStore: (select: (s: { addItem: typeof marketing.addItem }) => unknown) => select({ addItem: marketing.addItem }), useToast: () => ({ success: vi.fn(), error: vi.fn() }) }));
vi.mock('@/analytics/hooks/useAnalytics', () => ({ useAnalytics: () => ({ addToCart: marketing.addToCart }) }));
vi.mock('@/utils', () => ({ resolveProductImage: () => '' }));
vi.mock('@/components/media', () => ({ OptimizedImage: () => null }));
vi.mock('./WishlistButton', () => ({ WishlistButton: () => null }));
vi.mock('@/components/common/AffiliateLink', () => ({ AffiliateLink: ({ href, children, ...props }: React.AnchorHTMLAttributes<HTMLAnchorElement>) => <a href={href} {...props}>{children}</a> }));
import { ProductCardQuickAddButton, useProductCardRuntime, type ProductCardRuntime } from './ProductCardRuntime';

function runtime(reason: string | null, canAdd: boolean): ProductCardRuntime {
    return { product: { availability: { unavailableReason: reason } }, href: '/products/configurable?ref=quality',
        canQuickAdd: canAdd, isAddingToCart: false, themeId: 'modern', labels: { addToCart: 'Add' }, handleQuickAdd: vi.fn() } as unknown as ProductCardRuntime;
}
describe('product card action semantics', () => {
    it('emits exactly one add event only after a successful catalog quick-add', async () => {
        let captured: ProductCardRuntime | null = null;
        function Capture() {
            captured = useProductCardRuntime({ product: { id: 'qa-product', name: 'QA', pricing: { currentPrice: 25 }, availability: { canAddToCart: true } } as ProductCardRuntime['product'], layout: {} as ProductCardRuntime['layout'], themeId: 'modern' });
            return null;
        }
        renderToStaticMarkup(<Capture />);
        const handler = (captured as ProductCardRuntime | null)!.handleQuickAdd;
        const event = { preventDefault: vi.fn(), stopPropagation: vi.fn() } as unknown as React.MouseEvent<HTMLButtonElement>;
        marketing.addItem.mockResolvedValue(false);
        await handler(event);
        expect(marketing.addToCart).not.toHaveBeenCalled();
        marketing.addItem.mockResolvedValue(true);
        await Promise.all([handler(event), handler(event)]);
        expect(marketing.addToCart).toHaveBeenCalledExactlyOnceWith('qa-product', undefined, 25, 1, 'QA');
    });
    it('navigates to option selection instead of disabling the configurable product action', () => {
        const html = renderToStaticMarkup(<ProductCardQuickAddButton runtime={runtime('VARIANT_SELECTION_REQUIRED', false)} className="action" />);
        expect(html).toContain('href="/products/configurable?ref=quality"');
        expect(html).toContain('product.selectOptions');
        expect(html).not.toContain('disabled');
    });
    it('retains disabled stock controls and working simple-product buttons', () => {
        expect(renderToStaticMarkup(<ProductCardQuickAddButton runtime={runtime('OUT_OF_STOCK', false)} className="" />)).toContain('disabled');
        expect(renderToStaticMarkup(<ProductCardQuickAddButton runtime={runtime(null, true)} className="" />)).not.toContain('disabled=""');
    });
});
