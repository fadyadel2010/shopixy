export interface PosCart { cartId: string; warehouseId: string; customerId?: string | null; version: number; status: string | number; expiresAtUtc: string; items: { itemId: string; productId: string; productVariantId?: string | null; unitId?: string | null; quantity: number; discountAmount: number }[] }
export function isPosCartId(value: unknown): boolean;
export function getPosCart(id: string, options?: { signal?: AbortSignal }): Promise<PosCart | null>;
export function getOpenPosCart(options?: { signal?: AbortSignal }): Promise<PosCart | null>;
export function createPosCart(options?: { warehouseId?: string; customerId?: string | null; signal?: AbortSignal }): Promise<PosCart>;
export function transitionPosCart(cart: PosCart, transition: string, reason?: string, options?: { signal?: AbortSignal }): Promise<PosCart | null>;
export function cancelPosCart(id: string, reason?: string, options?: { signal?: AbortSignal }): Promise<PosCart | null>;
export function synchronizePosCart(options?: { cartId?: string; warehouseId?: string; customerId?: string | null; items?: unknown[]; signal?: AbortSignal }): Promise<PosCart>;
