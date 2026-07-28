import { getTenantId, isTenantResolved } from './tenantResolver';

function normalizeTenantId(value: string): string {
    return value.trim().toLowerCase();
}

export function getTenantScopedStorageKey(baseKey: string): string {
    if (!isTenantResolved()) {
        throw new Error(`[TenantScopedStorage] Cannot access ${baseKey} before tenant resolution.`);
    }

    const tenantId = normalizeTenantId(getTenantId());
    if (!tenantId) {
        throw new Error(`[TenantScopedStorage] Cannot access ${baseKey} without tenant identity.`);
    }

    return `${baseKey}:${tenantId}`;
}

export function tryGetTenantScopedStorageKey(baseKey: string): string | null {
    if (!isTenantResolved()) {
        return null;
    }

    return getTenantScopedStorageKey(baseKey);
}
