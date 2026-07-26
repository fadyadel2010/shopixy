# Services Marketplace - Phase 2 Implementation Summary

## Overview
Phase 2 adds real-time chat and interaction logging to the Services Marketplace foundation. Chat is enabled ONLY after a ServiceRequest is accepted, with all messages persisted before broadcast and full participant validation.

## Core Invariants Enforced

### 1. No ServiceRequest → No Chat
- Cannot create or send messages without valid ServiceRequest
- Database trigger validates ServiceRequest exists and is in valid state
- Service layer validates before persistence

### 2. Only Participants Can Chat
- Only BuyerUserId and ProviderUserId from ServiceRequest can send messages
- Database trigger validates sender is participant
- SignalR hub validates on connection and message send
- No admin bypass

### 3. Persist Before Broadcast
- Every message validated by domain service
- Stored in PostgreSQL with all constraints
- THEN broadcast via SignalR
- No transient messages

### 4. Chat Does NOT Change Business State
- Messages are informational only
- Cannot complete requests
- Cannot create reviews
- Cannot modify ServiceRequest.Status
- Only domain services change state

### 5. One Conversation Per ServiceRequest
- ConversationId == ServiceRequestId
- No separate conversation table
- 1:1 mapping guaranteed

### 6. Messages Are Immutable
- No public setters
- Database trigger prevents modification
- Only soft delete allowed (for future moderation)

### 7. Interaction State Validation
- ServiceRequest.AllowsInteraction() is single source of truth
- Returns true for: Accepted, CompletedByProvider, CompletedByBuyer
- All chat validation uses this method
- No duplicated status checks

## Deliverables

### 1. Database Schema
**File:** `migrations/20260212_ServicesMarketplacePhase2_Chat.sql`

Enums created:
- `SenderRole` (Buyer, Provider)
- `MessageType` (Text, System)

Table created:
- `ServiceRequestMessages` with full constraints

Triggers:
- `validate_message_requires_active_request()` - Validates ServiceRequest state and participant
- `prevent_message_modification()` - Enforces message immutability

Constraints:
- FK to ServiceRequests with cascade delete
- FK to Users
- Check constraint: Body length > 0
- Indexes on (ServiceRequestId, CreatedAt) for efficient retrieval

### 2. Domain Entities
**File:** `PointOfSale.Core/Entities/ServiceRequestMessage.cs`

Features:
- Private constructor
- Static factory methods: CreateText(), CreateSystem()
- Immutable properties (no public setters)
- Inherits from TenantEntity
- Body trimmed and validated

### 3. Enums
**Files:**
- `PointOfSale.Core/Enums/SenderRole.cs` (Buyer, Provider)
- `PointOfSale.Core/Enums/MessageType.cs` (Text, System)

### 4. Domain Service
**Interface:** `PointOfSale.Core/Interfaces/IChatService.cs`
**Implementation:** `PointOfSale.Infrastructure/Services/ChatService.cs`

Methods:
- `SendMessageAsync()` - Validates, persists, returns message
- `SendSystemMessageAsync()` - Creates system messages
- `GetMessagesAsync()` - Retrieves conversation history
- `CanUserParticipateAsync()` - Validates user access

Responsibilities:
- Load ServiceRequest
- Verify participant
- Verify state using AllowsInteraction()
- Determine SenderRole
- Create message entity
- Save to database
- NO SignalR logic

### 5. SignalR Hub
**File:** `PointOfSale.API/Hubs/ChatHub.cs`

Methods:
- `JoinConversation()` - Validates participant and joins group
- `LeaveConversation()` - Removes from group
- `SendMessage()` - Calls IChatService, then broadcasts
- `GetMessages()` - Returns conversation history

Features:
- Authenticates user from claims
- Group name: "sr:{ServiceRequestId}"
- Validates participation before join
- Disconnects unauthorized users
- NEVER touches DbContext directly
- Only depends on IChatService

### 6. System Messages Integration
**Updated:** `PointOfSale.Infrastructure/Services/ServiceRequestService.cs`

System messages created on:
- Accept: "Request accepted by provider"
- MarkCompletedByProvider: "Provider marked service as completed"
- ConfirmCompletedByBuyer: "Buyer confirmed service completion"
- Cancel: "Request cancelled" (with optional reason)

### 7. ServiceRequest Enhancement
**Updated:** `PointOfSale.Core/Entities/ServiceRequest.cs`

Added method:
```csharp
public bool AllowsInteraction()
{
    return Status == ServiceRequestStatus.Accepted ||
           Status == ServiceRequestStatus.CompletedByProvider ||
           Status == ServiceRequestStatus.CompletedByBuyer;
}
```

Updated:
- `CanEnableChat()` now calls `AllowsInteraction()`
- Single source of truth for interaction validation

### 8. EF Core Configuration
**File:** `PointOfSale.Infrastructure/Data/Configurations/ServiceRequestMessageConfiguration.cs`

Features:
- Enum to string conversion
- Cascade delete on ServiceRequest
- Indexes for performance
- Text column type for Body

### 9. Architecture Tests
**File:** `PointOfSale.Tests/Architecture/ChatArchitectureTests.cs`

Tests that fail build if:
- ServiceRequestMessage has public setters
- ServiceRequestMessage has public constructor
- ChatHub accesses DbContext directly
- ChatHub depends on anything except IChatService
- DTOs accept SenderRole from client
- DTOs accept MessageType from client
- ServiceRequest missing AllowsInteraction() method
- IChatService.SendMessageAsync returns void

### 10. Integration Tests
**File:** `PointOfSale.Tests/Integration/ChatIntegrationTests.cs`

Tests:
- Cannot send message without ServiceRequest
- Cannot send when status is Pending
- Can send after Accepted
- Non-participant rejected
- Both buyer and provider can send
- System messages created on status changes
- CanUserParticipate validates correctly
- Messages ordered by CreatedAt
- Cannot send after Cancelled

## Invariant Enforcement Layers

### Layer 1: Database
- Trigger validates ServiceRequest state
- Trigger validates participant
- Trigger validates SenderRole matches user
- Trigger prevents message modification
- Check constraint on Body length
- Foreign key constraints

### Layer 2: Entity
- Private constructor
- Static factory methods
- Immutable properties
- Body validation and trimming

### Layer 3: Service Layer (IChatService)
- Loads ServiceRequest
- Validates AllowsInteraction()
- Validates participant
- Determines SenderRole server-side
- Persists before returning

### Layer 4: SignalR Hub
- Authenticates user
- Validates participation on join
- Calls IChatService (no direct DB access)
- Broadcasts only after persistence

### Layer 5: Architecture Tests
- Compile-time enforcement
- Build fails if invariants violated
- Prevents accidental violations

## Authorization Flow

### Connection:
1. Client sends ServiceRequestId
2. Hub extracts userId from claims
3. Hub calls IChatService.CanUserParticipateAsync()
4. If authorized: join group "sr:{ServiceRequestId}"
5. If unauthorized: disconnect

### Message Send:
1. Client sends ServiceRequestId + messageBody
2. Hub extracts userId from claims
3. Hub calls IChatService.SendMessageAsync()
4. Service validates and persists
5. Hub broadcasts to group
6. If error: throw HubException

## System Messages

System messages automatically created when:
- ServiceRequest accepted
- Provider marks completed
- Buyer confirms completion
- Request cancelled

System messages:
- Use MessageType.System
- Use provider's UserId by convention
- Go through IChatService (not hub)
- Persisted like regular messages

## Multi-Tenancy
- ServiceRequestMessage inherits from TenantEntity
- TenantId included in every message
- Automatic tenant isolation via global query filters
- No cross-tenant visibility

## Security Model
- Only participants can join conversation
- Only participants can send messages
- SenderRole determined server-side (never from client)
- MessageType determined server-side (never from client)
- No admin bypass
- User identity from authenticated claims only

## What Phase 2 Does NOT Include
- No UI/frontend
- No notifications (email, push, etc.)
- No file attachments
- No message search
- No moderation tools
- No trust scores
- No Redis backplane (optional for future)
- No MongoDB projections

## Files Created

### Database (1 file)
- `migrations/20260212_ServicesMarketplacePhase2_Chat.sql`

### Core Layer (4 files)
- `PointOfSale.Core/Enums/SenderRole.cs`
- `PointOfSale.Core/Enums/MessageType.cs`
- `PointOfSale.Core/Entities/ServiceRequestMessage.cs`
- `PointOfSale.Core/Interfaces/IChatService.cs`

### Infrastructure Layer (2 files)
- `PointOfSale.Infrastructure/Services/ChatService.cs`
- `PointOfSale.Infrastructure/Data/Configurations/ServiceRequestMessageConfiguration.cs`

### API Layer (1 file)
- `PointOfSale.API/Hubs/ChatHub.cs`

### Tests (2 files)
- `PointOfSale.Tests/Architecture/ChatArchitectureTests.cs`
- `PointOfSale.Tests/Integration/ChatIntegrationTests.cs`

### Updated Files (2 files)
- `PointOfSale.Core/Entities/ServiceRequest.cs` (added AllowsInteraction())
- `PointOfSale.Infrastructure/Services/ServiceRequestService.cs` (added system messages)

**Total: 10 new files + 2 updated files**

## Verification Checklist

✅ Impossible to chat without accepted request
✅ Impossible to chat by non-participants
✅ Every message persisted before broadcast
✅ One conversation per ServiceRequest
✅ Messages are immutable
✅ SenderRole determined server-side
✅ MessageType determined server-side
✅ AllowsInteraction() is single source of truth
✅ ChatHub never touches DbContext
✅ System messages on status changes
✅ Build fails if invariants violated
✅ Multi-tenancy support
✅ Tenant isolation enforced

## Integration with Phase 1

Phase 2 builds on Phase 1 foundation:
- Uses existing ServiceRequest state machine
- Respects existing status transitions
- Adds chat capability without changing business logic
- System messages track state changes
- Chat enabled only in valid states

## Next Steps for Phase 3

Phase 3 will add:
- Rating aggregation and display
- Provider reputation scores
- Trust metrics
- Review statistics
- Provider ranking algorithms

## Phase 2 Status: COMPLETE ✅

Real-time chat with full invariant enforcement is ready for Phase 3 (Ratings & Trust).
