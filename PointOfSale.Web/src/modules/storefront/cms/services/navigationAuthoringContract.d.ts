import type { HeaderAnnouncementConfig } from '../components/HeaderAnnouncementSettings';
export class NavigationAuthoringContractError extends Error { constructor(code: string, message: string, details?: unknown); code: string; details: unknown }
export interface NavigationLink { label: string; labelAr?: string | null; type: string; targetId?: string | null; collectionKey?: string | null; path?: string | null; slug?: string | null; openInNewTab?: boolean; order?: number; enabled?: boolean; validationStatus?: string | null; validationCode?: string | null; validationMessage?: string | null }
export interface NavigationConfig { header: NavigationLink[]; footer: NavigationLink[]; announcement?: HeaderAnnouncementConfig | null }
export interface NavigationWarning { code: string; message: string; snapshot: string; menu: string; index: number | null; field?: string | null; linkType: string; targetId?: string | null; status: string }
export interface NavigationSnapshot { rowVersion: string; draft: NavigationConfig; live: NavigationConfig | null; hasPendingChanges: boolean; validationWarnings?: NavigationWarning[]; brokenLinks?: NavigationWarning[] }
export interface NavigationTarget { type: string; targetId: string | null; collectionKey: string | null; label: string; resolvedPath: string | null; state: string }
export function admitNavigationSnapshot(candidate: unknown): NavigationSnapshot & { validationWarnings: NavigationWarning[]; brokenLinks: NavigationWarning[] };
export function mapNavigationDraftToRequest(candidate: unknown): NavigationConfig & { rowVersion: string };
export function admitNavigationTargets(candidate: unknown): { categories: NavigationTarget[]; products: NavigationTarget[]; cmsPages: NavigationTarget[]; collections: NavigationTarget[] };
export function extractNavigationAdmissionError(error: unknown): { code: string; message: string; details: unknown } | null;
export function extractNavigationConflict(error: unknown): { code: string; message: string; latestVersion: ReturnType<typeof admitNavigationSnapshot> | null } | null;
