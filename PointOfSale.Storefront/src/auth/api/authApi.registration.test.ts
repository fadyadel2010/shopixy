import { beforeEach, describe, expect, it, vi } from 'vitest';

const { post } = vi.hoisted(() => ({ post: vi.fn() }));
vi.mock('@/services/api/httpClient', () => ({ httpClient: { post }, isHttpClientError: () => false }));
import { register } from './authApi';

describe('registration optional phone transport', () => {
    beforeEach(() => { post.mockReset().mockResolvedValue({ data: {} }); });
    it.each([undefined, '', '  \t '])('omits optional blank %j from JSON', async phone => {
        await register({ displayName: 'QA', email: 'qa@example.test', password: 'Synthetic1!', phone });
        const payload = JSON.parse(JSON.stringify(post.mock.calls[0][1]));
        expect(payload).not.toHaveProperty('phone');
        expect(payload).toMatchObject({ displayName: 'QA', email: 'qa@example.test', password: 'Synthetic1!' });
    });
    it('preserves supported formatting for authoritative tenant-aware API normalization', async () => {
        await register({ displayName: 'QA', email: 'qa@example.test', password: 'Synthetic1!', phone: '  +1 (202) 555-0101  ' });
        expect(post.mock.calls[0][1].phone).toBe('+1 (202) 555-0101');
        expect(post).toHaveBeenCalledTimes(1);
    });
    it('does not retry a potentially committed POST after a transport failure', async () => {
        post.mockRejectedValueOnce(new Error('Synthetic response loss'));
        await expect(register({ displayName: 'QA', email: 'qa@example.test', password: 'Synthetic1!' })).rejects.toThrow('Synthetic response loss');
        expect(post).toHaveBeenCalledTimes(1);
    });
});
