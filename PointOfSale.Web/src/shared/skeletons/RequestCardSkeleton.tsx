import './skeletons.css';

/**
 * RequestCardSkeleton - Loading placeholder for RequestCard.
 * RULES:
 * - Pure presentation
 * - No data fetching
 */
export function RequestCardSkeleton() {
  return (
    <div className="request-card skeleton-card">
      <div className="skeleton-header">
        <div className="skeleton-line skeleton-title" />
        <div className="skeleton-badge" />
      </div>

      <div className="skeleton-provider">
        <div className="skeleton-line skeleton-text" />
      </div>

      <div className="skeleton-message">
        <div className="skeleton-line skeleton-description" />
      </div>

      <div className="skeleton-meta">
        <div className="skeleton-line skeleton-text short" />
      </div>

      <div className="skeleton-actions">
        <div className="skeleton-button" />
        <div className="skeleton-button" />
      </div>
    </div>
  );
}
