let resolvedTenant: string | null = null

export function isTenantResolved(): boolean {
  return resolvedTenant !== null
}

export function getTenantId(): string {
  if (!resolvedTenant) {
    throw new Error('Tenant has not been resolved.')
  }

  return resolvedTenant
}

export function setResolvedTenant(tenantId: string, options?: { overwrite?: boolean }): void {
  if (resolvedTenant !== null && resolvedTenant !== tenantId && !options?.overwrite) {
    return
  }

  resolvedTenant = tenantId
}

export function clearResolvedTenant(): void {
  resolvedTenant = null
}
