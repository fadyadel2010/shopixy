import './empty-states.css';

interface EmptyStateProps {
  title: string;
  description: string;
  actionLabel?: string;
  onAction?: () => void;
  icon?: React.ReactNode;
}

/**
 * EmptyState - Generic empty state component.
 * RULES:
 * - Pure presentation
 * - No data fetching
 * - No business logic
 */
export function EmptyState({ title, description, actionLabel, onAction, icon }: EmptyStateProps) {
  return (
    <div className="empty-state">
      {icon && <div className="empty-state-icon">{icon}</div>}
      <h3 className="empty-state-title">{title}</h3>
      <p className="empty-state-description">{description}</p>
      {actionLabel && onAction && (
        <button onClick={onAction} className="btn btn-primary empty-state-action">
          {actionLabel}
        </button>
      )}
    </div>
  );
}
