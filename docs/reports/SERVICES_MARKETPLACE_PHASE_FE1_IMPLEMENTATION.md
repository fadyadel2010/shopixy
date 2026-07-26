# Services Marketplace - Phase FE-1: Frontend Foundation

## Implementation Date
February 12, 2026

## Mission
Create a deterministic, invariant-driven React TypeScript frontend for the Services Marketplace that:
- Acts as a thin projection layer over backend APIs
- NEVER invents state or computes rankings
- Uses server-driven permissions via capability flags
- Implements soft failure patterns
- Uses React Query for server state management

## Architecture Principles

### Core Rules
1. **UI is a projection layer** - No business logic in frontend
2. **Server-driven permissions** - All actions based on backend capability flags
3. **No client-side computation** - No trust math, no ranking, no sorting (except backend-provided)
4. **Soft failure** - Search errors return empty results, not error stacks
5. **SignalR only when allowed** - Chat connects only if `canChat = true`
6. **No optimistic updates** - Wait for server confirmation

### Technology Stack
- React 18 with TypeScript
- React Query (TanStack Query) for server state
- SignalR for real-time chat
- React Router for navigation

## Implementation Summary

### 1. Core Infrastructure

#### API Client (`src/core/apiClient.ts`)
- Centralized HTTP client with auth and error handling
- Base URL configuration
- Automatic token injection
- Global error handling

#### TypeScript Types (`src/marketplace/types/index.ts`)
- Exact matches to backend API contracts
- No client-side enhancements
- All capability flags included

#### API Layer (`src/marketplace/api/marketplaceApi.ts`)
- Pure data fetching functions
- Soft failure on search (returns empty results)
- No business logic
- Direct mapping to backend endpoints

### 2. Data Fetching Hooks

#### Query Hooks (Read Operations)
- `useMarketplaceSearch` - Search services with filters
- `useServiceDetails` - Get service details
- `useMyRequests` - Get buyer's requests
- `useIncomingRequests` - Get provider's requests
- `useRequestDetails` - Get request details
- `useMyServices` - Get provider's services
- `useChat` - Real-time chat with SignalR

#### Mutation Hooks (Write Operations)
- `useRequestService` - Request a service
- `useAcceptRequest` - Accept request (provider)
- `useMarkCompleted` - Mark as completed (provider)
- `useConfirmCompletion` - Confirm completion (buyer)
- `useCancelRequest` - Cancel request (buyer)
- `useCreateReview` - Submit review
- `useCreateService` - Create service (provider)
- `useActivateService` - Activate service (provider)
- `useDeactivateService` - Deactivate service (provider)

### 3. UI Components

#### Dumb Components (No Data Fetching)
- `ServiceCard` - Display service in search results
- `SearchBar` - Search input
- `Filters` - Filter controls (minRating, trustBadge)
- `RequestCard` - Display service request
- `ChatMessage` - Display chat message
- `ReviewModal` - Review submission form

All components:
- Receive data via props
- No API calls
- No business logic
- Pure presentation

### 4. Page Components

#### MarketplaceHome (`pages/MarketplaceHome.tsx`)
- Search and discover services
- Client-side state: search params only
- Pagination
- Sort: relevance | rating (backend-provided only)
- Soft failure: shows "No services available" on error

#### ServiceDetailsPage (`pages/ServiceDetailsPage.tsx`)
- View service details
- Trust metrics displayed as-is (no computation)
- Request service form
- Only shows request button if service is active

#### MyRequestsPage (`pages/MyRequestsPage.tsx`)
- View requests I made (buyer view)
- Actions based on capability flags:
  - `canConfirmCompletion` → Confirm button
  - `canCancel` → Cancel button
  - `canChat` → Chat link
  - `canReview` → Review link

#### IncomingRequestsPage (`pages/IncomingRequestsPage.tsx`)
- View requests for my services (provider view)
- Actions based on capability flags:
  - `canAccept` → Accept button
  - `canMarkCompleted` → Mark Completed button
  - `canChat` → Chat link

#### MyServicesPage (`pages/MyServicesPage.tsx`)
- Manage my services (provider view)
- Create new services
- Activate/deactivate services
- Only visible if `hasMarketplaceProfile = true`

#### ChatPage (`pages/ChatPage.tsx`)
- Real-time chat for service request
- Loads messages via REST first
- SignalR connection only if `canChat = true`
- Auto-scroll to bottom
- Disable input if chat not allowed

## Chat Implementation Details

### SignalR Connection Rules
1. **Check permission first** - Only connect if backend says `canChat = true`
2. **REST before real-time** - Load messages via REST API first
3. **Join conversation** - Call `JoinConversation(requestId)` after connection
4. **Listen for messages** - Subscribe to `ReceiveMessage` event
5. **Leave on unmount** - Call `LeaveConversation(requestId)` before disconnect
6. **No optimistic updates** - Wait for `ReceiveMessage` event

### Connection Lifecycle
```typescript
useEffect(() => {
  if (!canChat) return; // Don't connect if not allowed
  
  const connection = new HubConnectionBuilder()
    .withUrl('/hubs/chat')
    .withAutomaticReconnect()
    .build();
  
  connection.start()
    .then(() => connection.invoke('JoinConversation', requestId));
  
  connection.on('ReceiveMessage', (message) => {
    // Append to messages via React Query
  });
  
  return () => {
    connection.invoke('LeaveConversation', requestId)
      .finally(() => connection.stop());
  };
}, [requestId, canChat]);
```

## Permission Model

### Backend Capability Flags
All actions controlled by backend flags:

#### ServiceRequest Capabilities
- `canAccept` - Provider can accept
- `canMarkCompleted` - Provider can mark completed
- `canConfirmCompletion` - Buyer can confirm
- `canCancel` - Buyer can cancel
- `canChat` - Both can chat
- `canReview` - Buyer can review

### UI Rendering Rules
```typescript
// ✅ CORRECT - Use backend flag
{request.canChat && <Link to={`/chat/${requestId}`}>Chat</Link>}

// ❌ WRONG - Never infer permission
{request.status === 'Accepted' && <Link to={`/chat/${requestId}`}>Chat</Link>}
```

## Hard Invariants

### Build Must Fail If:
1. Any component queries PostgreSQL directly
2. Any component builds Elasticsearch query
3. Any component computes trust metrics
4. Any component sorts search results (except backend sort param)
5. Any component changes `ServiceRequest.Status` directly
6. Any component infers permissions instead of using flags

### Soft Failure Pattern
```typescript
// Search API - soft failure
searchServices: (request) => {
  return apiClient.post('/marketplace/search', request)
    .catch(() => ({
      items: [],
      totalCount: 0,
      page: request.page,
      pageSize: request.pageSize,
    }));
}
```

## Files Created

### Infrastructure
- `PointOfSale.Web/src/core/apiClient.ts`
- `PointOfSale.Web/src/marketplace/types/index.ts`
- `PointOfSale.Web/src/marketplace/api/marketplaceApi.ts`

### Hooks (Query)
- `PointOfSale.Web/src/marketplace/hooks/useMarketplaceSearch.ts`
- `PointOfSale.Web/src/marketplace/hooks/useServiceDetails.ts`
- `PointOfSale.Web/src/marketplace/hooks/useMyRequests.ts`
- `PointOfSale.Web/src/marketplace/hooks/useIncomingRequests.ts`
- `PointOfSale.Web/src/marketplace/hooks/useRequestDetails.ts`
- `PointOfSale.Web/src/marketplace/hooks/useMyServices.ts`
- `PointOfSale.Web/src/marketplace/hooks/useChat.ts`

### Hooks (Mutation)
- `PointOfSale.Web/src/marketplace/hooks/useRequestService.ts`
- `PointOfSale.Web/src/marketplace/hooks/useAcceptRequest.ts`
- `PointOfSale.Web/src/marketplace/hooks/useMarkCompleted.ts`
- `PointOfSale.Web/src/marketplace/hooks/useConfirmCompletion.ts`
- `PointOfSale.Web/src/marketplace/hooks/useCancelRequest.ts`
- `PointOfSale.Web/src/marketplace/hooks/useCreateReview.ts`
- `PointOfSale.Web/src/marketplace/hooks/useCreateService.ts`
- `PointOfSale.Web/src/marketplace/hooks/useActivateService.ts`
- `PointOfSale.Web/src/marketplace/hooks/useDeactivateService.ts`

### Components
- `PointOfSale.Web/src/marketplace/components/ServiceCard.tsx`
- `PointOfSale.Web/src/marketplace/components/SearchBar.tsx`
- `PointOfSale.Web/src/marketplace/components/Filters.tsx`
- `PointOfSale.Web/src/marketplace/components/RequestCard.tsx`
- `PointOfSale.Web/src/marketplace/components/ChatMessage.tsx`
- `PointOfSale.Web/src/marketplace/components/ReviewModal.tsx`

### Pages
- `PointOfSale.Web/src/marketplace/pages/MarketplaceHome.tsx`
- `PointOfSale.Web/src/marketplace/pages/ServiceDetailsPage.tsx`
- `PointOfSale.Web/src/marketplace/pages/MyRequestsPage.tsx`
- `PointOfSale.Web/src/marketplace/pages/IncomingRequestsPage.tsx`
- `PointOfSale.Web/src/marketplace/pages/MyServicesPage.tsx`
- `PointOfSale.Web/src/marketplace/pages/ChatPage.tsx`

## Next Steps (Not Implemented)

### 1. React Query Provider Setup
```typescript
// App.tsx or main entry point
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30000, // 30 seconds
      retry: 1,
    },
  },
});

<QueryClientProvider client={queryClient}>
  <App />
</QueryClientProvider>
```

### 2. Routing Setup
```typescript
// Router configuration
import { BrowserRouter, Routes, Route } from 'react-router-dom';

<Routes>
  <Route path="/admin/marketplace" element={<MarketplaceHome />} />
  <Route path="/admin/marketplace/services/:serviceId" element={<ServiceDetailsPage />} />
  <Route path="/admin/marketplace/my-requests" element={<MyRequestsPage />} />
  <Route path="/admin/marketplace/incoming-requests" element={<IncomingRequestsPage />} />
  <Route path="/admin/marketplace/my-services" element={<MyServicesPage />} />
  <Route path="/admin/marketplace/requests/:requestId/chat" element={<ChatPage />} />
</Routes>
```

### 3. Error Boundary
```typescript
// ErrorBoundary component for graceful error handling
class ErrorBoundary extends React.Component {
  // Catch rendering errors
}
```

### 4. CSS/Styling
- Add styles for all components
- Responsive design
- Loading states
- Error states

### 5. User Context
- Get current user ID for chat message ownership
- Profile check for provider features

## Acceptance Criteria

### ✅ Completed
- [x] All components are dumb (no business logic)
- [x] All data fetching in hooks
- [x] TypeScript types match backend exactly
- [x] Soft failure on search errors
- [x] SignalR connects only if `canChat = true`
- [x] No optimistic updates
- [x] Actions based on capability flags only
- [x] No client-side sorting (except backend param)
- [x] No trust computation
- [x] No status manipulation

### ⏳ Pending (Integration)
- [ ] React Query provider configured
- [ ] Routing configured
- [ ] Error boundary added
- [ ] CSS styling added
- [ ] User context integrated
- [ ] Build and test

## Comparison to Backend

### Backend API Endpoints → Frontend Hooks

| Backend Endpoint | Frontend Hook | Type |
|-----------------|---------------|------|
| `POST /api/marketplace/search` | `useMarketplaceSearch` | Query |
| `GET /api/marketplace/services/{id}` | `useServiceDetails` | Query |
| `GET /api/marketplace/my-requests` | `useMyRequests` | Query |
| `GET /api/marketplace/incoming-requests` | `useIncomingRequests` | Query |
| `GET /api/marketplace/requests/{id}` | `useRequestDetails` | Query |
| `GET /api/marketplace/my-services` | `useMyServices` | Query |
| `GET /api/marketplace/requests/{id}/chat/messages` | `useChat` | Query |
| `POST /api/marketplace/requests` | `useRequestService` | Mutation |
| `POST /api/marketplace/requests/{id}/accept` | `useAcceptRequest` | Mutation |
| `POST /api/marketplace/requests/{id}/complete` | `useMarkCompleted` | Mutation |
| `POST /api/marketplace/requests/{id}/confirm` | `useConfirmCompletion` | Mutation |
| `POST /api/marketplace/requests/{id}/cancel` | `useCancelRequest` | Mutation |
| `POST /api/marketplace/reviews` | `useCreateReview` | Mutation |
| `POST /api/marketplace/my-services` | `useCreateService` | Mutation |
| `POST /api/marketplace/my-services/{id}/activate` | `useActivateService` | Mutation |
| `POST /api/marketplace/my-services/{id}/deactivate` | `useDeactivateService` | Mutation |

## Summary

Phase FE-1 delivers a complete, invariant-driven React frontend that:
- Acts as a thin projection layer over backend APIs
- Uses server-driven permissions exclusively
- Implements soft failure patterns
- Manages real-time chat with proper gating
- Follows all architectural principles from Phases 1-5

The frontend is ready for integration once React Query provider, routing, and styling are configured.
