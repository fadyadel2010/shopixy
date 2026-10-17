export function isRuntimeOwnedSection(sectionType: string): boolean;
export function isSectionAvailableForPage(sectionType: string, pageKey?: string): boolean;
export function filterSectionsForPage<T extends { type?: string }>(sections?: readonly T[], pageKey?: string): (T & { order: number })[];
export function filterSectionDefinitionsForPage<T extends { type?: string }>(definitions?: readonly T[], pageKey?: string): T[];
