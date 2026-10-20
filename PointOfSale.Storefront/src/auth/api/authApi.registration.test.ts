import { beforeEach, describe, expect, it, vi } from 'vitest';

const { post } = vi.hoisted(() => ({ post: vi.fn() }));
vi.mock('@/services/api/httpClient', () => ({ httpClient: { post }, isHttpClientError: () => false }));
import { register } from './authApi';

describe('registration without phone collection', () => {
    beforeEach(() => { post.mockReset().mockResolvedValue({ data: {} }); });
    it('sends only the remaining registration fields with credentials', async () => {
        const request = { displayName: 'QA', email: 'qa@example.test', password: 'Synthetic1!' };
        await register(request);
        expect(post).toHaveBeenCalledExactlyOnceWith('/api/storefront/auth/register', request, { withCredentials: true });
        expect(post.mock.calls[0][1]).not.toHaveProperty('phone');
    });
    it('does not forward stale phone or confirmation values from runtime callers', async () => {
        const staleRequest = { displayName: 'QA', email: 'qa@example.test', password: 'Synthetic1!', phone: '+12025550101', confirmPassword: 'Synthetic1!' };
        await register(staleRequest);
        expect(post.mock.calls[0][1]).toEqual({ displayName: 'QA', email: 'qa@example.test', password: 'Synthetic1!' });
        expect(post.mock.calls[0][1]).not.toHaveProperty('phone');
    });
    it('does not retry a potentially committed POST after a transport failure', async () => {
        post.mockRejectedValueOnce(new Error('Synthetic response loss'));
        await expect(register({ displayName: 'QA', email: 'qa@example.test', password: 'Synthetic1!' })).rejects.toThrow('Synthetic response loss');
        expect(post).toHaveBeenCalledTimes(1);
    });
});
