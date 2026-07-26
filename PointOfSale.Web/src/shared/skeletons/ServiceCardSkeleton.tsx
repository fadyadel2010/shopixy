import './skeletons.css';

/**
 * ServiceCardSkeleton - Loading placeholder for ServiceCard.
 * RULES:
 * - Pure presentation
 * - No data fetching
 * - CSS-only animation
 */
export function ServiceCardSkeleton() {
  return (
    <div className="service-card skeleton-card">
      <div className="skeleton-header">
        <div className="skeleton-line skeleton-title" />
        <div className="skeleton-badge" />
      </div>

      <div className="skeleton-line skeleton-description" />
      <div className="skeleton-line skeleton-description short" />

      <div className="skeleton-provider">
        <div className="skeleton-line skeleton-text" />
      </div>

      <div className="skeleton-stats">
        <div className="skeleton-stat" />
        <div className="skeleton-stat" />
        <div className="skeleton-stat" />
      </div>

      <div className="skeleton-button" />
    </div>
  );
}
