export const MIN_AUTOCOMPLETE_QUERY_LENGTH = 3;

export const normalizeSearchInput = (value?: string | null) => (value ?? "").replace(/\s+/g, " ").trimStart();

export const normalizeSearchQuery = (value?: string | null) => normalizeSearchInput(value).trim();
