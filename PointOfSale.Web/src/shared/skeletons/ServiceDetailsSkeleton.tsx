import './skeletons.css';

/**
 * ServiceDetailsSkeleton - Loading placeholder for ServiceDetailsPage.
 * RULES:
 * - Pure presentation
 * - No data fetching
 */
export function ServiceDetailsSkeleton() {
  return (
    <div className="service-details-page skeleton-page">
      <div className="skeleton-header">
        <div className="skeleton-line skeleton-title large" />
        <div className="skeleton-badge" />
      </div>

      <div className="skeleton-section">
        <div className="skeleton-line skeleton-subtitle" />
        <div className="skeleton-line skeleton-text" />
      </div>

      <div className="skeleton-section">
        <div className="skeleton-line skeleton-subtitle" />
        <div className="skeleton-line skeleton-description" />
        <div className="skeleton-line skeleton-description" />
        <div className="skeleton-line skeleton-description short" />
      </div>

      <div className="skeleton-section">
        <div className="skeleton-line skeleton-subtitle" />
        <div className="skeleton-stats">
          <div className="skeleton-stat" />
          <div className="skeleton-stat" />
          <div className="skeleton-stat" />
        </div>
      </div>

      <div className="skeleton-button large" />
    </div>
  );
}
