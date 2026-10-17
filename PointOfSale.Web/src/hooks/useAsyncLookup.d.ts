import type { Dispatch, SetStateAction } from 'react';
export function useAsyncLookup<T>(options: {
    loadOptions(request: { search: string; page: number; pageSize: number }): Promise<{ items: T[]; hasMore?: boolean }>;
    enabled?: boolean; loadOnEmptyQuery?: boolean; pageSize?: number; initialQuery?: string;
}): {
    query: string; setQuery: Dispatch<SetStateAction<string>>; options: T[]; loading: boolean; error: string; hasMore: boolean;
    loadMore(): Promise<void>; refresh(): Promise<void>; reset(): void;
};
