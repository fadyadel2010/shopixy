import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { ServiceSearchRequest } from '../types';
import type { PresentationFormatting } from '../presentationFormatting';

interface FiltersProps extends Pick<PresentationFormatting, 'dir' | 'formatNumber'> {
  onChange: (filters: Partial<ServiceSearchRequest>) => void;
}

/**
 * Filters - Search filters.
 * RULES:
 * - No client-side filtering
 * - All filters sent to backend
 */
export function Filters({ onChange, dir, formatNumber }: FiltersProps) {
  const { t } = useTranslation();
  const [minRating, setMinRating] = useState<number | undefined>();
  const [trustBadge, setTrustBadge] = useState<string | undefined>();

  const handleApply = () => {
    onChange({
      minRating,
      trustBadge: trustBadge || undefined,
    });
  };

  const handleReset = () => {
    setMinRating(undefined);
    setTrustBadge(undefined);
    onChange({
      minRating: undefined,
      trustBadge: undefined,
    });
  };

  return (
    <div className="filters" dir={dir}>
      <div className="filter-group">
        <label>{t('servicesMarketplace.filters.minimumRating')}</label>
        <select value={minRating || ''} onChange={(e) => setMinRating(e.target.value ? Number(e.target.value) : undefined)}>
          <option value="">{t('servicesMarketplace.filters.any')}</option>
          <option value="3">{t('servicesMarketplace.filters.ratingPlus', { rating: formatNumber(3), defaultValue: `{{rating}}+ ★` })}</option>
          <option value="4">{t('servicesMarketplace.filters.ratingPlus', { rating: formatNumber(4), defaultValue: `{{rating}}+ ★` })}</option>
          <option value="4.5">{t('servicesMarketplace.filters.ratingPlus', { rating: formatNumber(4.5), defaultValue: `{{rating}}+ ★` })}</option>
        </select>
      </div>

      <div className="filter-group">
        <label>{t('servicesMarketplace.filters.trustBadge')}</label>
        <select value={trustBadge || ''} onChange={(e) => setTrustBadge(e.target.value || undefined)}>
          <option value="">{t('servicesMarketplace.filters.any')}</option>
          <option value="New">{t('servicesMarketplace.badges.new')}</option>
          <option value="Rising">{t('servicesMarketplace.badges.rising')}</option>
          <option value="Trusted">{t('servicesMarketplace.badges.trusted')}</option>
          <option value="TopRated">{t('servicesMarketplace.badges.topRated')}</option>
        </select>
      </div>

      <div className="filter-actions">
        <button type="button" onClick={handleApply} className="btn btn-primary">
          {t('servicesMarketplace.filters.apply')}
        </button>
        <button type="button" onClick={handleReset} className="btn btn-secondary">
          {t('servicesMarketplace.filters.reset')}
        </button>
      </div>
    </div>
  );
}
