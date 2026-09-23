import { beforeEach, describe, expect, it, vi } from 'vitest';
import { fetchProductByIdentifier } from './storefrontApi';
import { httpClient } from './httpClient';
import type { ProductReadDto } from '@/types';

vi.mock('./httpClient', () => ({
    httpClient: {
        get: vi.fn(),
        post: vi.fn(),
    },
}));

describe('storefront product detail API', () => {
    beforeEach(() => {
        vi.mocked(httpClient.get).mockReset();
    });

    it('uses the canonical slug endpoint when a card provides a slug', async () => {
        const product = { id: 'product-id', slug: 'realme-gt-neo-3' } as unknown as ProductReadDto;
        vi.mocked(httpClient.get).mockResolvedValueOnce({ data: product });

        await expect(fetchProductByIdentifier('realme-gt-neo-3')).resolves.toBe(product);

        expect(httpClient.get).toHaveBeenCalledWith('/api/storefront/products/realme-gt-neo-3');
    });

    it('uses the tenant-scoped by-id endpoint for a card with no slug', async () => {
        const productId = 'b55ddc92-14b2-0160-78e2-a9ebfe867f1b';
        const product = { id: productId } as unknown as ProductReadDto;
        vi.mocked(httpClient.get).mockResolvedValueOnce({ data: product });

        await expect(fetchProductByIdentifier(productId)).resolves.toBe(product);

        expect(httpClient.get).toHaveBeenCalledWith(
            '/api/storefront/products/by-id/b55ddc92-14b2-0160-78e2-a9ebfe867f1b',
        );
    });
});
