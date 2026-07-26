import { EmptyState } from './EmptyState';

/**
 * NoMessagesYet - Empty state for chat.
 * RULES:
 * - Pure presentation
 * - No data fetching
 */
export function NoMessagesYet() {
  return (
    <EmptyState
      title="No messages yet"
      description="Start the conversation by sending a message below."
    />
  );
}
