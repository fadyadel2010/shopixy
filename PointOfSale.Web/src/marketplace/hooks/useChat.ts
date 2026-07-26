import { useInfiniteQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { marketplaceApi } from '../api/marketplaceApi';
import { marketplaceKeys } from '../queryKeys';
import { marketplaceAuthorityQueryOptions, useMarketplaceAuthorityScope } from '../authority';
import {
  deriveChatHistoryPageAuthority,
  mergeChatHistoryPagesAuthority,
  isCurrentChatRealtimeEvent,
  type ChatIdentityAuthority,
} from '../chatAuthority';

/**
 * Hook for chat functionality.
 * PHASE CR-1 HARDENING:
 * - Cookie-based authentication (withCredentials: true)
 * - NO query string tokens or tenantId
 * - Server-authoritative tenant from JWT
 * - Connects to SignalR only if canChat = true
 * - Loads messages via REST
 * - Real-time only invalidates current backend authority
 * - Never optimistic updates
 */
export function useChat(identity: ChatIdentityAuthority | null) {
  const queryClient = useQueryClient();
  const authorityScope = useMarketplaceAuthorityScope();
  const [isConnected, setIsConnected] = useState(false);
  const connectionAuthorityRef = useRef<{
    connection: signalR.HubConnection;
    requestId: string;
    authorityScope: string;
  } | null>(null);
  const currentIdentityRef = useRef(identity);
  currentIdentityRef.current = identity;
  const requestId = identity?.requestId ?? '';

  // Load messages via REST
  const chatQuery = useInfiniteQuery({
    queryKey: marketplaceKeys.chat(requestId, authorityScope),
    queryFn: async ({ pageParam }) => {
      if (!identity) {
        throw new Error('Current chat identity authority is unavailable');
      }

      const payload = await marketplaceApi.getChatMessages(requestId, pageParam);
      const page = deriveChatHistoryPageAuthority(payload, identity, pageParam);
      if (!page) {
        throw new Error('Invalid chat history page authority response');
      }

      return page;
    },
    initialPageParam: null as string | null,
    getNextPageParam: (lastPage) => lastPage.hasMore
      ? lastPage.nextBeforeMessageId ?? undefined
      : undefined,
    enabled: !!identity?.canViewChatHistory,
    ...marketplaceAuthorityQueryOptions,
  });
  const historyAuthorityFailed = chatQuery.isError || chatQuery.isFetchNextPageError;
  const messages = identity
    && !historyAuthorityFailed
    && !(chatQuery.isFetching && !chatQuery.isFetchingNextPage)
    ? mergeChatHistoryPagesAuthority(chatQuery.data?.pages ?? [], identity) ?? []
    : [];

  // SignalR connection
  useEffect(() => {
    if (!identity?.canSendChat || historyAuthorityFailed) return;
    let acceptsRealtimeEvents = true;

    // PHASE CR-1: Cookie-based authentication, NO query string tokens
    const newConnection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/chat', {
        withCredentials: true  // Send HttpOnly Secure cookie
      })
      .withAutomaticReconnect()
      .build();

    // Realtime is an invalidation trigger only; REST reload preserves backend ordering and audit truth.
    newConnection.on('ReceiveMessage', (message: unknown) => {
      if (!acceptsRealtimeEvents || !isCurrentChatRealtimeEvent(message, identity)) return;
      void Promise.allSettled([
        queryClient.invalidateQueries({ queryKey: marketplaceKeys.scope(authorityScope) }),
        queryClient.resetQueries({ queryKey: marketplaceKeys.chat(requestId, authorityScope), exact: true }),
      ]);
    });

    // Reset connected state on reconnection events
    newConnection.onreconnecting(() => {
      if (acceptsRealtimeEvents) {
        connectionAuthorityRef.current = null;
        setIsConnected(false);
      }
    });
    newConnection.onreconnected(async () => {
      if (!acceptsRealtimeEvents) return;

      // A recovered transport is not authority. Retire it and require a fresh
      // request/chat validation before a new connection may join the room.
      acceptsRealtimeEvents = false;
      if (connectionAuthorityRef.current?.connection === newConnection) {
        connectionAuthorityRef.current = null;
      }
      setIsConnected(false);
      await Promise.allSettled([
        newConnection.stop(),
        queryClient.invalidateQueries({ queryKey: marketplaceKeys.request(requestId, authorityScope) }),
        queryClient.resetQueries({ queryKey: marketplaceKeys.chat(requestId, authorityScope), exact: true }),
      ]);
    });
    newConnection.onclose(() => {
      if (acceptsRealtimeEvents) {
        connectionAuthorityRef.current = null;
        setIsConnected(false);
      }
    });

    void (async () => {
      try {
        await newConnection.start();
        if (!acceptsRealtimeEvents) {
          await newConnection.stop();
          return;
        }

        await newConnection.invoke('JoinConversation', requestId);
        if (!acceptsRealtimeEvents) {
          await newConnection.stop();
          return;
        }

        connectionAuthorityRef.current = { connection: newConnection, requestId, authorityScope };
        await queryClient.resetQueries({
          queryKey: marketplaceKeys.chat(requestId, authorityScope),
          exact: true,
        });
        if (!acceptsRealtimeEvents) {
          await newConnection.stop();
          return;
        }
        setIsConnected(true);
      } catch (err) {
        if (acceptsRealtimeEvents) {
          console.error('SignalR connection error:', err);
          connectionAuthorityRef.current = null;
          setIsConnected(false);
        }
      }
    })();

    return () => {
      acceptsRealtimeEvents = false;
      if (connectionAuthorityRef.current?.connection === newConnection) {
        connectionAuthorityRef.current = null;
      }
      setIsConnected(false);
      void newConnection.stop();
    };
  }, [requestId, identity?.canSendChat, historyAuthorityFailed, queryClient, authorityScope]);

  const sendMessage = async (body: string, attachmentObjectKey?: string) => {
    const currentIdentity = currentIdentityRef.current;
    const connectionAuthority = connectionAuthorityRef.current;
    if (
      !currentIdentity?.canSendChat
      || currentIdentity.requestId !== requestId
      || !connectionAuthority
      || connectionAuthority.requestId !== currentIdentity.requestId
      || connectionAuthority.authorityScope !== authorityScope
      || connectionAuthority.connection.state !== signalR.HubConnectionState.Connected
    ) {
      throw new Error('Current chat send authority is unavailable');
    }

    try {
      await connectionAuthority.connection.invoke('SendMessage', requestId, body, attachmentObjectKey || null);
    } catch (err) {
      console.error('Failed to send message:', err);
      throw err;
    } finally {
      await queryClient.resetQueries({
        queryKey: marketplaceKeys.chat(requestId, authorityScope),
        exact: true,
      });
    }
  };

  const loadOlderMessages = async () => {
    if (
      !identity?.canViewChatHistory
      || historyAuthorityFailed
      || !chatQuery.hasNextPage
      || chatQuery.isFetchingNextPage
    ) {
      return;
    }

    await chatQuery.fetchNextPage({ cancelRefetch: false });
  };

  return {
    messages,
    isLoading: !!identity?.canViewChatHistory
      && (chatQuery.isPending || (chatQuery.isFetching && !chatQuery.isFetchingNextPage)),
    isLoadingOlder: chatQuery.isFetchingNextPage,
    hasOlderMessages: !!chatQuery.hasNextPage,
    authorityFailed: historyAuthorityFailed,
    isConnected,
    canSend: !!identity?.canSendChat && !historyAuthorityFailed && isConnected,
    loadOlderMessages,
    sendMessage,
  };
}
