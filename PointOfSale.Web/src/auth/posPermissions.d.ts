import type { PosUser } from '../store/authStore'
export const POS_PERMISSIONS: Set<string>
export function deriveEffectivePosPermissions(value: unknown): string[] | null
export function hasPosPermission(user: PosUser | null | undefined, permission: string): boolean
