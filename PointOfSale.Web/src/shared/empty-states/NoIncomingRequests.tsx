import { EmptyState } from './EmptyState';

/**
 * NoIncomingRequests - Empty state for provider's incoming requests.
 * RULES:
 * - Pure presentation
 * - No data fetching
 */
export function NoIncomingRequests() {
  return (
    <EmptyState
      title="No incoming requests"
      description="You don't have any service requests yet. Make sure your services are active and visible in the marketplace."
    />
  );
}
