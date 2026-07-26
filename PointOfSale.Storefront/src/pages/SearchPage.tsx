/**
 * SearchPage Component
 * 
 * Full search results page at /search?q=...
 * 
 * Features:
 * - Full results from Elasticsearch
 * - Entity type filtering (tabs)
 * - Pagination
 * - Grouped results by type
 * - Loading/empty/error states
 * 
 * CRITICAL:
 * - Reads from Elasticsearch ONLY via /api/storefront/search
 * - NO MongoDB direct access
 * - NO client-side filtering as substitute
 * 
 * Phase 10.1: Multi-language support
 * Phase 5A: Analytics tracking (SearchPerformed)
 */

import React, { useEffect, useState, useCallback, useRef } from 'react';
import { useSearchParams } from 'react-router-dom';
import { Layout } from '@/components/layout';
import { SearchInput } from '@/components/search/SearchInput';
import { SearchResultItem } from '@/components/search/SearchResultItem';
import { useTranslation } from '@/i18n';
import { useAnalytics } from '@/analytics';
import {
    useSearchStore,
    selectSearchResults,
    selectSearchStatus,
    selectError
} from '@/stores/searchStore';
import { MIN_SEARCH_LENGTH } from '@/services/api/searchApi';

// ============================================================================
// Types
// ============================================================================

type EntityTab = 'all' | 'product' | 'category' | 'cms';

// ============================================================================
// Component
// ============================================================================

export const SearchPage: React.FC = () => {
    const { t } = useTranslation();
    const [searchParams, setSearchParams] = useSearchParams();

    // Get query from URL
    const urlQuery = searchParams.get('q') || '';
    const urlPage = parseInt(searchParams.get('page') || '1', 10);
    const urlType = (searchParams.get('type') || 'all') as EntityTab;

    // Local state
    const [query, setQuery] = useState(urlQuery);
    const [activeTab, setActiveTab] = useState<EntityTab>(urlType);

    // PHASE 5A: Analytics for search tracking
    const analytics = useAnalytics();
    const lastTrackedSearchRef = useRef<string>('');

    // Search store
    const searchResults = useSearchStore(selectSearchResults);
    const searchStatus = useSearchStore(selectSearchStatus);
    const error = useSearchStore(selectError);
    const fetchSearch = useSearchStore(state => state.fetchSearch);

    // Fetch search on mount and when URL params change
    useEffect(() => {
        if (urlQuery.trim().length >= MIN_SEARCH_LENGTH) {
            const type = activeTab === 'all' ? undefined : activeTab;
            fetchSearch(urlQuery, urlPage, type);
        }
    }, [urlQuery, urlPage, activeTab, fetchSearch]);

    // PHASE 5A: Track SearchPerformed when search completes (CRITICAL for funnel)
    useEffect(() => {
        if (
            searchStatus === 'success' &&
            searchResults &&
            urlQuery.trim().length >= MIN_SEARCH_LENGTH &&
            lastTrackedSearchRef.current !== urlQuery
        ) {
            analytics.search(urlQuery, searchResults.totalCount);
            lastTrackedSearchRef.current = urlQuery;
            console.log('[Analytics] SearchPerformed tracked:', urlQuery, 'results:', searchResults.totalCount);
        }
    }, [searchStatus, searchResults, urlQuery, analytics]);

    // Sync local query state with URL
    useEffect(() => {
        setQuery(urlQuery);
    }, [urlQuery]);

    // Handle search submit
    const handleSearchSubmit = useCallback(() => {
        if (query.trim().length >= MIN_SEARCH_LENGTH) {
            const params = new URLSearchParams();
            params.set('q', query.trim());
            if (activeTab !== 'all') {
                params.set('type', activeTab);
            }
            setSearchParams(params);
        }
    }, [query, activeTab, setSearchParams]);

    // Handle tab change
    const handleTabChange = useCallback((tab: EntityTab) => {
        setActiveTab(tab);
        const params = new URLSearchParams(searchParams);
        if (tab === 'all') {
            params.delete('type');
        } else {
            params.set('type', tab);
        }
        params.delete('page'); // Reset to page 1
        setSearchParams(params);
    }, [searchParams, setSearchParams]);

    // Handle pagination
    const handlePageChange = useCallback((newPage: number) => {
        const params = new URLSearchParams(searchParams);
        params.set('page', String(newPage));
        setSearchParams(params);
        window.scrollTo({ top: 0, behavior: 'smooth' });
    }, [searchParams, setSearchParams]);

    // Tab configuration
    const tabs: { key: EntityTab; labelKey: string }[] = [
        { key: 'all', labelKey: 'common.viewAll' },
        { key: 'product', labelKey: 'search.products' },
        { key: 'category', labelKey: 'search.categories' },
        { key: 'cms', labelKey: 'search.pages' },
    ];

    return (
        <Layout>
            <div className="flex-1 container mx-auto px-4 py-8">
                {/* Search Header */}
                <div className="max-w-3xl mx-auto mb-8">
                    <h1 className="text-2xl md:text-3xl font-bold text-text mb-4">
                        {t('common.search')}
                    </h1>

                    {/* Search Input */}
                    <div className="relative">
                        <SearchInput
                            value={query}
                            onChange={setQuery}
                            onSearch={handleSearchSubmit}
                            isLoading={searchStatus === 'loading'}
                            size="lg"
                        />
                    </div>
                </div>

                {/* Results Section */}
                {urlQuery.trim().length >= MIN_SEARCH_LENGTH && (
                    <div className="max-w-4xl mx-auto">
                        {/* Tabs */}
                        <div className="flex gap-1 mb-6 overflow-x-auto pb-2">
                            {tabs.map(tab => (
                                <button
                                    key={tab.key}
                                    onClick={() => handleTabChange(tab.key)}
                                    className={`
                                        px-4 py-2 rounded-lg text-sm font-medium whitespace-nowrap transition-colors
                                        ${activeTab === tab.key
                                            ? 'bg-primary text-white'
                                            : 'bg-muted/10 text-muted hover:bg-muted/20 hover:text-text'
                                        }
                                    `}
                                >
                                    {t(tab.labelKey)}
                                </button>
                            ))}
                        </div>

                        {/* Search Meta */}
                        {searchStatus === 'success' && searchResults && (
                            <div className="text-sm text-muted mb-4">
                                {t('search.results', { count: searchResults.totalCount })} {t('search.searching')} "{urlQuery}" ({searchResults.searchTimeMs}ms)
                            </div>
                        )}

                        {/* Loading State */}
                        {searchStatus === 'loading' && (
                            <div className="flex items-center justify-center gap-3 py-16">
                                <svg className="w-6 h-6 animate-spin text-primary" fill="none" viewBox="0 0 24 24">
                                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                                </svg>
                                <span className="text-muted">
                                    {t('search.searching')}
                                </span>
                            </div>
                        )}

                        {/* Error State */}
                        {searchStatus === 'error' && (
                            <div className="text-center py-16">
                                <svg className="w-12 h-12 mx-auto mb-4 text-error/50" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
                                </svg>
                                <h3 className="text-lg font-semibold text-text mb-2">
                                    {t('search.unavailable')}
                                </h3>
                                <p className="text-muted mb-4">
                                    {error || t('errors.general')}
                                </p>
                                <button
                                    onClick={() => fetchSearch(urlQuery, urlPage, activeTab === 'all' ? undefined : activeTab)}
                                    className="px-4 py-2 bg-primary text-white rounded-lg hover:bg-primary/90 transition-colors"
                                >
                                    {t('common.retry')}
                                </button>
                            </div>
                        )}

                        {/* Empty State */}
                        {searchStatus === 'empty' && (
                            <div className="text-center py-16">
                                <svg className="w-12 h-12 mx-auto mb-4 text-muted/50" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                                </svg>
                                <h3 className="text-lg font-semibold text-text mb-2">
                                    {t('common.noResults')}
                                </h3>
                                <p className="text-muted">
                                    {t('search.noResultsFor', { query: urlQuery })}
                                </p>
                            </div>
                        )}

                        {/* Results List */}
                        {searchStatus === 'success' && searchResults && searchResults.items.length > 0 && (
                            <>
                                <div className="bg-background border border-muted/20 rounded-xl overflow-hidden divide-y divide-muted/10">
                                    {searchResults.items.map(item => (
                                        <SearchResultItem
                                            key={`${item.entityType}-${item.entityId}`}
                                            item={item}
                                            query={urlQuery}
                                        />
                                    ))}
                                </div>

                                {/* Pagination */}
                                {searchResults.totalPages > 1 && (
                                    <div className="flex items-center justify-center gap-2 mt-8">
                                        <button
                                            onClick={() => handlePageChange(urlPage - 1)}
                                            disabled={!searchResults.hasPreviousPage}
                                            className={`
                                                px-4 py-2 rounded-lg text-sm font-medium transition-colors
                                                ${searchResults.hasPreviousPage
                                                    ? 'bg-muted/10 text-text hover:bg-muted/20'
                                                    : 'bg-muted/5 text-muted cursor-not-allowed'
                                                }
                                            `}
                                        >
                                            {t('common.previous')}
                                        </button>

                                        <span className="px-4 py-2 text-sm text-muted">
                                            {t('common.page')} {urlPage} / {searchResults.totalPages}
                                        </span>

                                        <button
                                            onClick={() => handlePageChange(urlPage + 1)}
                                            disabled={!searchResults.hasNextPage}
                                            className={`
                                                px-4 py-2 rounded-lg text-sm font-medium transition-colors
                                                ${searchResults.hasNextPage
                                                    ? 'bg-muted/10 text-text hover:bg-muted/20'
                                                    : 'bg-muted/5 text-muted cursor-not-allowed'
                                                }
                                            `}
                                        >
                                            {t('common.next')}
                                        </button>
                                    </div>
                                )}
                            </>
                        )}
                    </div>
                )}

                {/* Initial State (no query) */}
                {urlQuery.trim().length < MIN_SEARCH_LENGTH && (
                    <div className="max-w-3xl mx-auto text-center py-16">
                        <svg className="w-16 h-16 mx-auto mb-4 text-muted/30" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                        </svg>
                        <h2 className="text-xl font-semibold text-text mb-2">
                            {t('search.placeholder')}
                        </h2>
                        <p className="text-muted">
                            {t('search.minChars')}
                        </p>
                    </div>
                )}
            </div>
        </Layout>
    );
};

export default SearchPage;
