import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useFormatting } from '../../hooks/useFormatting';

interface SearchBarProps {
  onSearch: (query: string) => void;
}

/**
 * SearchBar - Simple search input.
 * NO client-side filtering.
 */
export function SearchBar({ onSearch }: SearchBarProps) {
  const { t } = useTranslation();
  const { dir, isRTL } = useFormatting();
  const [query, setQuery] = useState('');

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onSearch(query);
  };

  return (
    <form onSubmit={handleSubmit} className="search-bar" dir={dir}>
      <input
        type="text"
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        aria-label={t('servicesMarketplace.searchBar.placeholder')}
        placeholder={t('servicesMarketplace.searchBar.placeholder')}
        className={`search-input ${isRTL ? 'text-right' : 'text-left'}`}
      />
      <button type="submit" className="btn btn-primary">
        {t('servicesMarketplace.searchBar.search')}
      </button>
    </form>
  );
}
