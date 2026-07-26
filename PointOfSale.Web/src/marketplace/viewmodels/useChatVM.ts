import { useChat } from '../hooks/useChat';
import { formatDistanceToNow } from 'date-fns';
import type { ChatMessage } from '../types';
import type { ChatIdentityAuthority } from '../chatAuthority';

/**
 * useChatVM - ViewModel for chat.
 * RULES:
 * - Consumes hooks
 * - Maps to UI props
 * - Adds formatted text
 * - Groups messages
 * - NO domain logic
 */
export function useChatVM(identity: ChatIdentityAuthority | null) {
  const { messages, isLoading, isConnected, canSend, sendMessage } = useChat(identity);

  const isEmpty = !isLoading && messages.length === 0;

  // Group consecutive messages by sender
  const groupedMessages = groupMessagesBySender(messages);

  // Format timestamps
  const messagesWithFormatted = groupedMessages.map((group) => ({
    ...group,
    formattedTime: formatDistanceToNow(new Date(group.createdAt), { addSuffix: true }),
  }));

  return {
    messages: messagesWithFormatted,
    isLoading,
    isEmpty,
    isConnected,
    canSend,
    sendMessage,
  };
}

/**
 * Group consecutive messages by same sender.
 * Pure function - no side effects.
 */
function groupMessagesBySender(messages: readonly ChatMessage[]) {
  if (messages.length === 0) return [];

  const groups: Array<ChatMessage & { isGroupStart: boolean }> = [];
  let lastSenderId: string | null = null;

  messages.forEach((message) => {
    const isGroupStart = message.senderUserId !== lastSenderId;
    groups.push({ ...message, isGroupStart });
    lastSenderId = message.senderUserId;
  });

  return groups;
}
