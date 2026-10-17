export interface PosCartSnapshot { cartId: string; items: { id: string; quantity: number; [key: string]: unknown }[]; customer: { id: string; name: string; [key: string]: unknown } | null; discountType: string; discountValue: number; warehouseId: string; editingSaleId: string | null; editingInvoiceNumber: string | null; editingOriginalItems: unknown[]; editingBaselineFingerprint: string | null; editingMutationVersion: number | null; savedAt?: string }
export function buildPosCartSnapshotFingerprint(snapshot: unknown): string;
export function shouldApplyExternalPosCartSnapshot(input: { localHasActiveCart: boolean; localFingerprint: string; externalFingerprint: string }): boolean;
export function savePosCartSnapshot(snapshot: unknown, options?: { sourceId?: string }): void;
export function getPersistedPosCart(): PosCartSnapshot | null;
export function subscribeToPosCartChanges(callback?: (event: { sourceId: string | null; savedAt: string | null; snapshot: PosCartSnapshot | null }) => void): () => void;
export function clearPersistedPosCart(): void;
