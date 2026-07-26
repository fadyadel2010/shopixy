# Services Marketplace - Phase FE-1 Hardening (Architecture Seal)

## Implementation Date
February 12, 2026

## Mission
Introduce structural safeguards to eliminate accidental violations, enforce determinism, and guarantee invariant compliance at compile time. This phase adds ZERO new features - only architectural hardening.

## Objectives Achieved

### ✅ 1. Query Key Standardization
**File**: `src/marketplace/queryKeys.ts`

All React Query keys are now namespaced and centralized:

```typescript
export const marketplaceKeys = {
  all: ['marketplace'] as const,
  profile: () => [...marketplaceKeys.all, 'profile'] as const,
  search: (params) => [...marketplaceKeys.all, 'search', params] as const,
  service: (serviceId) => [...marketplaceKeys.services(), serviceId] as const,
  myServices: () => [...marketplaceKeys.all, 'my-services'] as const,
  myRequests: () => [...marketplaceKeys.all, 'my-requests'] as const,
  incomingRequests: () => [...marketplaceKeys.all, 'incoming-requests'] as const,
  request: (requestId) => [...marketplaceKeys.requests(), requestId] as const,
  chat: (requestId) => [...marketplaceKeys.all, 'chat', requestId] as const,
};
```

**Rules Enforced**:
- No ad-hoc string keys
- All keys type-safe
- Hierarchical structure for invalidation
- Used by all hooks

### ✅ 2. Global Invalidation Policy
All mutation hooks now use global invalidation:

```typescript
onSuccess: () => {
  queryClient.invalidateQueries({ queryKey: marketplaceKeys.all });
}
```

**Benefits**:
- No selective invalidation complexity
- No manual cache patching
- No optimistic updates
- React Query refetches what's needed
- Deterministic cache state

**Updated Hooks**:
- `useRequestService`
- `useAcceptRequest`
- `useMarkCompleted`
- `useConfirmCompletion`
- `useCancelRequest`
- `useCreateReview`
- `useCreateService`
- `useActivateService`
- `useDeactivateService`

### ✅ 3. Readonly DTO Contracts
All response types are now immutable:

```typescript
export type ServiceResult = Readonly<{
  serviceId: string;
  title: string;
  averageRating: number;
  // ... all fields readonly
}>;

export type ServiceSearchResponse = Readonly<{
  items: readonly ServiceResult[];
  totalCount: number;
  page: number;
  pageSize: number;
}>;
```

**Rules**:
- Response types = `Readonly<{...}>` (immutable snapshots)
- Request types = `interface` (mutable for form state)
- No mutation allowed
- Data treated as snapshots from backend

**Readonly Types**:
- `MarketplaceProfile`
- `ServiceSearchResponse`
- `ServiceResult`
- `ServiceDetails`
- `ServiceRequest`
- `ServiceRequestDetails`
- `ChatMessage`
- `MyService`

**Mutable Types** (for forms):
- `ServiceSearchRequest`
- `CreateReviewRequest`

### ✅ 4. Layered Import Enforcement (ESLint)
**File**: `.eslintrc.cjs`

Hard rules enforced at lint time:

#### Global Rules
```javascript
'no-restricted-imports': [
  'error',
  {
    paths: [
      {
        name: 'axios',
        message: 'Use apiClient only'
      },
      {
        name: '@microsoft/signalr',
        message: 'SignalR only allowed in useChat hook'
      }
    ],
    patterns: [
      {
        group: ['**/api/marketplaceApi'],
        message: 'Components cannot import API layer. Use hooks.'
      }
    ]
  }
]
```

#### Component-Specific Rules
```javascript
// Components cannot import hooks or API
files: ['**/components/**/*.tsx'],
rules: {
  'no-restricted-imports': [
    'error',
    {
      patterns: [
        {
          group: ['**/hooks/**'],
          message: 'Components cannot call hooks. Only pages can use hooks.'
        },
        {
          group: ['**/api/**'],
          message: 'Components cannot import API layer.'
        }
      ]
    }
  ]
}
```

#### Exceptions
- `useChat.ts` - Only file allowed to import SignalR
- `hooks/**` and `api/**` - Can import apiClient
- `apiClient.ts` - Can import axios

### ✅ 5. Frontend Architecture Tests
**File**: `src/architecture-tests/frontendArchitecture.spec.ts`

Static tests that fail the build if invariants are violated:

#### Layer Separation Tests
- ✅ Components MUST NOT import apiClient
- ✅ Components MUST NOT import axios
- ✅ Components MUST NOT use fetch()
- ✅ Only useChat can import SignalR
- ✅ Only hooks/API can import apiClient

#### Query Key Tests
- ✅ All hooks MUST use marketplaceKeys factory
- ✅ No ad-hoc string query keys

#### Invalidation Tests
- ✅ All mutations MUST use global invalidation
- ✅ No optimistic updates allowed

#### Forbidden Operations Tests
- ✅ No client-side sorting of services
- ✅ No trust metric computation
- ✅ No status-based permission inference

#### Type Safety Tests
- ✅ Response types MUST be Readonly
- ✅ Request types can be mutable (for forms)

#### API Layer Purity Tests
- ✅ API layer MUST NOT contain business logic
- ✅ API methods MUST return Promise

### ✅ 6. Folder Ownership Model

```
/core        → Infrastructure (apiClient)
/api         → HTTP access (pure data fetching)
/hooks       → Server state (React Query)
/components  → Pure UI (no data fetching)
/pages       → Orchestration (uses hooks)
```

**Rules**:
- Components NEVER call hooks
- Pages use hooks
- Hooks call API only
- API calls apiClient only

## Files Created/Modified

### Created
- `PointOfSale.Web/src/marketplace/queryKeys.ts` - Centralized query key factory
- `PointOfSale.Web/.eslintrc.cjs` - Import restriction rules
- `PointOfSale.Web/src/architecture-tests/frontendArchitecture.spec.ts` - Static architecture tests
- `PointOfSale.Web/vitest.config.ts` - Test configuration

### Modified (Hardened)
- `PointOfSale.Web/src/marketplace/types/index.ts` - Made response types Readonly
- All hooks in `PointOfSale.Web/src/marketplace/hooks/` - Updated to use:
  - Standardized query keys
  - Global invalidation
  - No optimistic updates

## Running Architecture Tests

```bash
# Run all tests
npm test

# Run architecture tests only
npm run test:arch

# Run tests with UI
npm run test:ui
```

## Build Integration

Architecture tests run as part of the build process. Build will FAIL if:
- Any component imports apiClient
- Any file (except useChat) imports SignalR
- Any component uses fetch/axios
- Any hook uses ad-hoc query keys
- Any mutation uses selective invalidation
- Any mutation uses optimistic updates
- Any component sorts service results
- Any component computes trust metrics
- Any component infers permissions from status

## Hardening Benefits

### Compile-Time Safety
- TypeScript enforces Readonly types
- ESLint enforces import restrictions
- Architecture tests enforce layer separation

### Runtime Safety
- Global invalidation prevents cache inconsistencies
- No optimistic updates prevents race conditions
- Readonly types prevent accidental mutations

### Developer Experience
- Clear error messages when rules violated
- Centralized query keys (autocomplete)
- Type-safe query key factory
- Impossible to bypass hooks

### Maintenance
- Architectural invariants documented in code
- Tests prevent regression
- Clear ownership model
- Easy to onboard new developers

## Comparison: Before vs After

### Before Hardening
```typescript
// ❌ Ad-hoc query keys
queryKey: ['service-details', serviceId]

// ❌ Selective invalidation
queryClient.invalidateQueries({ queryKey: ['my-services'] });
queryClient.invalidateQueries({ queryKey: ['service-details', serviceId] });

// ❌ Mutable types
export interface ServiceResult {
  serviceId: string;
  averageRating: number;
}

// ❌ No import restrictions
import axios from 'axios'; // Anywhere
```

### After Hardening
```typescript
// ✅ Standardized query keys
queryKey: marketplaceKeys.service(serviceId)

// ✅ Global invalidation
queryClient.invalidateQueries({ queryKey: marketplaceKeys.all });

// ✅ Immutable types
export type ServiceResult = Readonly<{
  serviceId: string;
  averageRating: number;
}>;

// ✅ Import restrictions enforced
import axios from 'axios'; // ESLint error!
```

## Forbidden Patterns (Now Impossible)

### ❌ Component Calling API Directly
```typescript
// ESLint error: Components cannot import API layer
import { marketplaceApi } from '../api/marketplaceApi';
```

### ❌ Ad-Hoc Query Keys
```typescript
// Architecture test fails
queryKey: ['my-custom-key', id]
```

### ❌ Selective Invalidation
```typescript
// Architecture test fails
queryClient.invalidateQueries({ queryKey: ['my-services'] });
```

### ❌ Optimistic Updates
```typescript
// Architecture test fails
onMutate: async (newData) => {
  queryClient.setQueryData(['key'], newData);
}
```

### ❌ Mutating DTOs
```typescript
// TypeScript error: Cannot assign to 'averageRating' because it is a read-only property
service.averageRating = 5.0;
```

### ❌ Client-Side Sorting
```typescript
// Architecture test fails
services.sort((a, b) => b.rating - a.rating);
```

### ❌ Trust Computation
```typescript
// Architecture test fails
const avgRating = reviews.reduce((sum, r) => sum + r.rating, 0) / reviews.length;
```

### ❌ Status-Based Permissions
```typescript
// Architecture test fails
{request.status === 'Accepted' && <ChatButton />}
// Must use: {request.canChat && <ChatButton />}
```

## Success Criteria

### ✅ Compile-Time Constraints
- Impossible to bypass hooks
- Impossible to mutate DTOs
- Impossible to use wrong imports

### ✅ Deterministic Data Flow
- Global invalidation only
- No optimistic updates
- No manual cache patching

### ✅ Abuse-Resistant
- ESLint catches violations
- Architecture tests catch violations
- TypeScript catches mutations

### ✅ Maintainable
- Clear ownership model
- Documented invariants
- Easy to understand

## Next Steps

The frontend is now sealed with the same rigor as the backend:
- ✅ Invariant-driven
- ✅ Layered
- ✅ Deterministic
- ✅ Abuse-resistant

Phase FE-1 is complete and hardened. The architecture is now enforced at:
1. **TypeScript level** - Type safety
2. **ESLint level** - Import restrictions
3. **Test level** - Architecture invariants
4. **Runtime level** - Readonly enforcement

The frontend behaves exactly like the backend: a thin, deterministic projection layer with zero business logic.
