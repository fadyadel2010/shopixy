import { EmptyState } from './EmptyState';

interface NoRequestsYetProps {
  onBrowseServices?: () => void;
}

/**
 * NoRequestsYet - Empty state for buyer's requests.
 * RULES:
 * - Pure presentation
 * - No data fetching
 */
export function NoRequestsYet({ onBrowseServices }: NoRequestsYetProps) {
  return (
    <EmptyState
      title="No service requests yet"
      description="You haven't requested any services. Browse the marketplace to find services you need."
      actionLabel={onBrowseServices ? 'Browse Services' : undefined}
      onAction={onBrowseServices}
    />
  );
}
