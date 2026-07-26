import { EmptyState } from './EmptyState';

interface NoServicesFoundProps {
  hasFilters?: boolean;
  onClearFilters?: () => void;
}

/**
 * NoServicesFound - Empty state for search results.
 * RULES:
 * - Pure presentation
 * - No data fetching
 */
export function NoServicesFound({ hasFilters, onClearFilters }: NoServicesFoundProps) {
  if (hasFilters) {
    return (
      <EmptyState
        title="No services match your filters"
        description="Try adjusting your search criteria or clearing filters to see more results."
        actionLabel="Clear Filters"
        onAction={onClearFilters}
      />
    );
  }

  return (
    <EmptyState
      title="No services available"
      description="There are currently no services in the marketplace. Check back later or be the first to create one!"
    />
  );
}
