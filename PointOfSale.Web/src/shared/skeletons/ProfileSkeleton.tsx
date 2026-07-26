import './skeletons.css';

/**
 * ProfileSkeleton - Loading placeholder for profile sections.
 * RULES:
 * - Pure presentation
 * - No data fetching
 */
export function ProfileSkeleton() {
  return (
    <div className="profile-section skeleton-section">
      <div className="skeleton-line skeleton-title" />
      <div className="skeleton-line skeleton-text" />
      <div className="skeleton-line skeleton-text short" />
    </div>
  );
}
