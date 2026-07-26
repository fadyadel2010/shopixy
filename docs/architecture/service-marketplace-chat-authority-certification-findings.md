# PASS S1-C Service Marketplace Chat Authority

Service Marketplace Chat Authority is production-certifiable for inspected runtime paths after chat history membership, closed-lifecycle send, attachment view-authority, and system-message boundary hardening.

## Membership findings
- Chat membership is owned by SQL `ServiceRequests.BuyerUserId` and `ProviderUserId` under trusted tenant scope.
- Third parties cannot join, send, view history, upload attachments, or download attachments on inspected paths because membership is reloaded from SQL request authority.

## Conversation findings
- Conversation identity is the accepted SQL `ServiceRequest.Id`; no separate conversation row, SignalR group, DTO, route value, or frontend state owns conversation identity.
- Conversations cannot be forged without the accepted request authority certified in S1-B.

## Message findings
- Message identity is minted server-side by `ServiceRequestMessage.CreateText/CreateSystem` with `Guid.NewGuid()` and persisted in PostgreSQL.
- Sender ownership and `SenderRole` are derived from SQL request buyer/provider ownership, not DTOs, SignalR connection state, or frontend state.

## Attachment findings
- Chat attachment storage keys must be server-issued tenant/request/user-scoped object keys under `tenants/{tenantId}/marketplace-chat-attachments/{requestId}/{userId}`.
- Absolute URLs, traversal references, foreign tenant/request/user keys, missing object-storage references, and legacy local upload references fail closed.

## Visibility findings
- History reads now require caller user ID, trusted tenant ID, and SQL request participant proof inside `ChatService.GetMessagesAsync`.
- Participants may view SQL message history after closure, but non-participants and foreign tenants cannot view history.

## Realtime findings
- SignalR is transport only. `ChatHub` delegates join, send, and history checks to `IChatService` and does not access `DbContext` or use socket/group membership as authority.
- Broadcast occurs only after SQL message persistence succeeds.

## Lifecycle findings
- User message sending is allowed only while the SQL request is `Accepted` or `CompletedByProvider`; buyer-confirmed completion, cancellation, rejection, pending state, and deletion block new user messages.
- System lifecycle messages are tenant-checked and require a non-pending SQL lifecycle state.

## Projection findings
- No Redis, MongoDB, Elasticsearch, cache, projection, or frontend path owns chat membership, message history, sender identity, or attachment visibility on inspected paths.

## Isolation findings
- Message, history, realtime, upload, and attachment download paths require trusted tenant scope and SQL request tenant equality.
- Public attachment routes require authenticated user and tenant claims before resolving signed object-storage URLs.

## Concurrency findings
- Message sends acquire SQL request row locks on relational providers and recheck lifecycle, membership, tenant scope, and rate limits under lock before persisting.
- Replay and duplicate send attempts create separate SQL messages only when the sender remains an authoritative participant in an open chat lifecycle; stale lifecycle changes fail closed.

## Historical findings
- Historical messages remain immutable SQL rows and are visible only to SQL request participants.
- Closed conversations preserve history visibility without preserving send/upload authority.

## Adversarial findings
- Membership bypass, message forgery, sender-role DTO tampering, attachment key tampering, foreign attachment download, route-tampered history reads, tenant guessing, socket spoofing, stale SignalR group membership, and frontend replay are blocked or fail closed on inspected runtime paths.

## Runtime Hardening Performed
- Changed `IChatService.GetMessagesAsync` and `ChatService.GetMessagesAsync` to require caller user and trusted tenant authority and verify SQL participant history visibility internally.
- Added `CanUserViewConversationAsync` for history and attachment visibility separate from send/upload participation.
- Changed attachment download authorization to use SQL history visibility so participants can access historical attachments after closure without reopening send authority.
- Changed `ServiceRequest.AllowsInteraction` to close user-message authority after buyer-confirmed completion.
- Added `ServiceRequest.CanViewChatHistory` for participant-only historical visibility after non-pending lifecycle states.
- Hardened `SendSystemMessageAsync` with tenant equality and non-pending SQL lifecycle checks.

## Exact Runtime Files Modified
- `PointOfSale.Core/Entities/ServiceRequest.cs`
- `PointOfSale.Core/Interfaces/IChatService.cs`
- `PointOfSale.Infrastructure/Services/ChatService.cs`
- `PointOfSale.API/Hubs/ChatHub.cs`
- `PointOfSale.API/Controllers/MarketplaceController.cs`

## Regression Freezes Added
- `PointOfSale.Tests/Architecture/ServiceMarketplaceChatAuthorityCertificationTests.cs`
- Updated `PointOfSale.Tests/Integration/ChatIntegrationTests.cs`
- Updated `PointOfSale.Tests/MarketplaceAttachmentSecurityTests.cs`

## Architecture Updates
- Updated `.ai-context/PROJECT_CONTEXT.md`.
- Updated `.ai-context/ARCHITECTURE_TRUTH.md`.
- Updated `.ai-context/KNOWN_RISKS.md`.
- Updated `.ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md`.
- Updated `.ai-context/CODEX_RULES.md`.

## Explicit Answers
- Who owns chat authority? PostgreSQL `ServiceRequests` and `ServiceRequestMessages`, deriving membership and conversation identity from accepted request authority.
- Can chats be forged? No on inspected paths; conversation identity is the SQL request ID and interaction requires SQL accepted/active lifecycle authority.
- Can memberships be bypassed? No; buyer/provider membership is reloaded from SQL request truth.
- Can messages be forged? No on inspected paths; message identity and sender role are server-created after SQL membership/lifecycle checks.
- Can attachments bypass ownership? No; attachment keys must match tenant/request/user ownership and object-storage existence, and downloads require SQL participant visibility.
- Can tenant leakage occur? No inspected chat, realtime, history, upload, or attachment path exposes foreign-tenant data after hardening.
- Can sockets bypass SQL authority? No; SignalR group state is transport only and every join/send/history path delegates to SQL-backed chat service authority.
- Were runtime code changes required? Yes.
- Is Service Marketplace Chat Authority production-certifiable? Yes, for inspected runtime paths after the hardening above.

## Residual Operational Risk
- Production still needs chat-specific SQL/attachment audit dashboards, WORM/SIEM message and attachment evidence retention, object-storage lifecycle/delete reconciliation, and PostgreSQL-backed high-concurrency chaos runs for send/reconnect/history/attachment races.
