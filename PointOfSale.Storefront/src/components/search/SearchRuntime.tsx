import React, { createContext, useCallback, useContext, useMemo, useState } from 'react';
import { useStoreConfigStore, selectBehavior } from '@/store';
import { SearchModal, useSearchShortcut } from './SearchModal';

interface SearchRuntimeContextValue {
    enabled: boolean;
    openSearch: () => void;
}

const SearchRuntimeContext = createContext<SearchRuntimeContextValue>({
    enabled: false,
    openSearch: () => undefined,
});

export function SearchRuntimeProvider({ children }: { children: React.ReactNode }) {
    const behavior = useStoreConfigStore(selectBehavior);
    const [isOpen, setIsOpen] = useState(false);
    const enabled = behavior.enableSearch;
    const openSearch = useCallback(() => {
        if (enabled) {
            setIsOpen(true);
        }
    }, [enabled]);
    const closeSearch = useCallback(() => setIsOpen(false), []);

    useSearchShortcut(openSearch);

    const value = useMemo(() => ({ enabled, openSearch }), [enabled, openSearch]);

    return (
        <SearchRuntimeContext.Provider value={value}>
            {children}
            {enabled ? <SearchModal isOpen={isOpen} onClose={closeSearch} /> : null}
        </SearchRuntimeContext.Provider>
    );
}

export interface SearchRuntimeTriggerProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
    children?: React.ReactNode;
}

export const SearchRuntimeTrigger = React.forwardRef<HTMLButtonElement, SearchRuntimeTriggerProps>(
    ({ children, onClick, type = 'button', 'aria-label': ariaLabel = 'Search products', ...props }, ref) => {
        const { enabled, openSearch } = useContext(SearchRuntimeContext);

        if (!enabled) {
            return null;
        }

        return (
            <button
                {...props}
                ref={ref}
                type={type}
                aria-label={ariaLabel}
                onClick={(event) => {
                    onClick?.(event);
                    if (!event.defaultPrevented) {
                        openSearch();
                    }
                }}
            >
                {children ?? (
                    <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                    </svg>
                )}
            </button>
        );
    }
);

SearchRuntimeTrigger.displayName = 'SearchRuntimeTrigger';
