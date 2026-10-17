import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import { CartItemRow } from './CartItemRow';

vi.mock('@/utils', () => ({formatMoney: (amount: number) => String(amount), resolveProductImage: () => '/synthetic.png'}));
vi.mock('@/components/media', () => ({OptimizedImage: () => null}));

describe('cart product navigation', () => {
  it('uses the backend product identity, never the selected variant SKU as a slug', () => {
    const html = renderToStaticMarkup(<CartItemRow item={{productId:'11111111-1111-4111-8111-111111111111',
      variantId:'22222222-2222-4222-8222-222222222222',productName:'Synthetic',sku:'VARIANT-SKU-NOT-A-SLUG',
      quantity:1,unitPrice:30,lineTotal:30}} isUpdating={false} currency="EGP"
      onQuantityChange={async () => {}} onRemove={async () => {}} />);
    expect(html.match(/href="\/products\/11111111-1111-4111-8111-111111111111"/g)).toHaveLength(2);
    expect(html).not.toContain('href="/products/VARIANT-SKU-NOT-A-SLUG"');
  });
});
