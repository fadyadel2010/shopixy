let resolvedTenant: string | null = null

export function isTenantResolved(): boolean {
  return resolvedTenant !== null
}

export function setResolvedTenant(tenantId: string, options?: { overwrite?: boolean }): void {
  if (resolvedTenant !== null && resolvedTenant !== tenantId && !options?.overwrite) {
    return
  }

  resolvedTenant = tenantId
}
