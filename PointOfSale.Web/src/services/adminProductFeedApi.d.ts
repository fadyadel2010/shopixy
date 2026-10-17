export type ProductFeedChannel = 'meta' | 'google' | 'tiktok' | 'snapchat';
export const PRODUCT_FEED_CHANNELS: readonly { id: ProductFeedChannel; name: string }[];
export function assertProductFeedChannel(channel: unknown): asserts channel is ProductFeedChannel;
export interface ProductFeedToken { tokenId: string; channel: ProductFeedChannel; createdAtUtc: string; revokedAtUtc?: string | null; feedUrl?: string }
declare const adminProductFeedApi: {
    listTokens(channel: string): Promise<ProductFeedToken[]>;
    createToken(channel: string): Promise<ProductFeedToken>;
    rotateToken(channel: string, tokenId: string): Promise<ProductFeedToken>;
    revokeToken(channel: string, tokenId: string): Promise<void>;
};
export default adminProductFeedApi;
