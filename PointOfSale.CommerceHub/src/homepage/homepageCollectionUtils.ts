export type HomepageCollectionSource = "all" | "random" | "curated";

function normalizeCollectionSourceValue(value: unknown): HomepageCollectionSource | null {
  if (typeof value !== "string") {
    return null;
  }

  const normalized = value.trim().toLowerCase();
  if (normalized === "all" || normalized === "random" || normalized === "curated") {
    return normalized;
  }

  return null;
}

export function normalizeHomepageGuidList(value: unknown): string[] | undefined {
  if (!Array.isArray(value)) {
    return undefined;
  }

  return Array.from(
    new Set(
      value
        .map((entry) => String(entry ?? "").trim())
        .filter(Boolean)
    )
  );
}

export function resolveHomepageCollectionSource(
  value: unknown,
  curatedIds: unknown
): HomepageCollectionSource {
  const explicitSource = normalizeCollectionSourceValue(value);
  if (explicitSource) {
    return explicitSource;
  }

  const normalizedIds = normalizeHomepageGuidList(curatedIds);
  return normalizedIds && normalizedIds.length > 0 ? "curated" : "all";
}

export function normalizeHomepageCollectionLimit(
  value: unknown,
  fallback: number,
  maxValue = 24
): number {
  let parsedValue: number | null = null;

  if (typeof value === "number" && Number.isFinite(value)) {
    parsedValue = value;
  } else if (typeof value === "string" && value.trim()) {
    const numericValue = Number(value.trim());
    if (Number.isFinite(numericValue)) {
      parsedValue = numericValue;
    }
  }

  if (parsedValue === null) {
    return fallback;
  }

  const roundedValue = Math.round(parsedValue);
  if (roundedValue < 1) {
    return fallback;
  }

  return Math.min(roundedValue, maxValue);
}

function createDeterministicRank(seed: string): number {
  let hash = 2166136261;

  for (let index = 0; index < seed.length; index += 1) {
    hash ^= seed.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }

  return hash >>> 0;
}

export function selectHomepageCollectionItems<T>(
  items: readonly T[],
  options: {
    limit: number;
    source: HomepageCollectionSource;
    seedKey: string;
    getKey: (item: T, index: number) => string;
  }
): T[] {
  const { limit, source, seedKey, getKey } = options;

  if (source !== "random") {
    return items.slice(0, limit);
  }

  return items
    .map((item, index) => ({
      item,
      rank: createDeterministicRank(`${seedKey}:${getKey(item, index)}`)
    }))
    .sort((left, right) => left.rank - right.rank)
    .slice(0, limit)
    .map((entry) => entry.item);
}
