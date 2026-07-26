import './skeletons.css';

interface MessageSkeletonProps {
  isOwnMessage?: boolean;
}

/**
 * MessageSkeleton - Loading placeholder for ChatMessage.
 * RULES:
 * - Pure presentation
 * - No data fetching
 */
export function MessageSkeleton({ isOwnMessage = false }: MessageSkeletonProps) {
  return (
    <div className={`chat-message skeleton-message ${isOwnMessage ? 'own-message' : 'other-message'}`}>
      <div className="skeleton-message-header">
        <div className="skeleton-line skeleton-text short" />
      </div>
      <div className="skeleton-message-body">
        <div className="skeleton-line skeleton-description" />
        <div className="skeleton-line skeleton-description short" />
      </div>
    </div>
  );
}
