# Phase 8.0 – Tenant Registration System Design

## Document Status

| Property | Value |
|----------|-------|
| **Created** | 2025-12-26 |
| **Status** | Design Only (No Implementation) |
| **Author** | AI Agent |
| **Phase** | 8.0 |
| **Reviewers** | [ ] Product Owner, [ ] Tech Lead |

---

## Executive Summary

This document defines the **complete design specification** for the Tenant Registration system in the multi-tenant POS + E-commerce platform. The design covers the full tenant lifecycle from initial registration through first POS login, including subdomain allocation, automatic bootstrap, and onboarding checklist concepts.

**Key Principles:**
- POS is the **single source of truth** for all tenant data
- Storefront is **read-only** – never writes tenant configuration
- Tenant isolation is **mandatory and enforced at all layers**
- Storefront starts **disabled by default** (safety-first approach)
- All creation steps are **atomic** (succeed or rollback entirely)

---

## 1. Tenant Lifecycle

### 1.1 Lifecycle States

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          TENANT LIFECYCLE                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌──────────┐    ┌──────────┐    ┌──────────┐    ┌──────────┐              │
│  │ PENDING  │───▶│  ACTIVE  │───▶│ SUSPENDED│───▶│ DELETED  │              │
│  │ SETUP    │    │          │    │          │    │(Soft)    │              │
│  └──────────┘    └──────────┘    └──────────┘    └──────────┘              │
│       │                │                │                                   │
│       ▼                ▼                ▼                                   │
│  Bootstrap in     Operational      Frozen state                             │
│  progress         All features     Read-only access                         │
│                   enabled          No new transactions                      │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 1.2 State Definitions

| State | Description | POS Access | Storefront Access | Data Mutations |
|-------|-------------|------------|-------------------|----------------|
| `PENDING_SETUP` | Registration complete, bootstrap in progress | ❌ Blocked | ❌ Blocked | System only |
| `ACTIVE` | Fully operational | ✅ Full access | ✅ (if enabled) | ✅ Allowed |
| `SUSPENDED` | Frozen due to billing, policy, or admin action | ⚠️ Read-only | ❌ Blocked | ❌ Blocked |
| `DELETED` | Soft-deleted, data retained for recovery period | ❌ Blocked | ❌ Blocked | ❌ Blocked |

### 1.3 Registration to First POS Login Flow

```
                    ┌─────────────────────────────────────────────────────────────────┐
                    │                 REGISTRATION FLOW                               │
                    └─────────────────────────────────────────────────────────────────┘
                                              │
                                              ▼
┌─────────────────────────────────────────────────────────────────────────────────────┐
│ STEP 1: Initial Registration Request                                                │
├─────────────────────────────────────────────────────────────────────────────────────┤
│ • Business name                                                                     │
│ • Owner email (becomes primary admin)                                               │
│ • Desired subdomain (slug)                                                          │
│ • Business type (optional)                                                          │
│ • Country/Currency (optional, defaults applied)                                     │
└─────────────────────────────────────────────────────────────────────────────────────┘
                                              │
                                              ▼
┌─────────────────────────────────────────────────────────────────────────────────────┐
│ STEP 2: Validation                                                                  │
├─────────────────────────────────────────────────────────────────────────────────────┤
│ • Subdomain uniqueness check                                                        │
│ • Subdomain format validation (regex)                                               │
│ • Reserved subdomain rejection                                                      │
│ • Email format validation                                                           │
│ • Business name length/format check                                                 │
└─────────────────────────────────────────────────────────────────────────────────────┘
                                              │
                                              ▼
┌─────────────────────────────────────────────────────────────────────────────────────┐
│ STEP 3: Atomic Bootstrap (SINGLE DATABASE TRANSACTION)                              │
├─────────────────────────────────────────────────────────────────────────────────────┤
│ 3a. Create Tenant record (state: PENDING_SETUP)                                     │
│ 3b. Create primary Admin User (linked to Tenant)                                    │
│ 3c. Create StoreConfig with safe defaults                                           │
│ 3d. Create default Warehouse                                                        │
│ 3e. Create default Payment Methods (COD)                                            │
│ 3f. Create default Category root                                                    │
│ 3g. Create Onboarding Checklist record                                              │
│ 3h. Update Tenant state → ACTIVE                                                    │
│                                                                                     │
│ ⚠️ ANY FAILURE = FULL ROLLBACK                                                      │
└─────────────────────────────────────────────────────────────────────────────────────┘
                                              │
                                              ▼
┌─────────────────────────────────────────────────────────────────────────────────────┐
│ STEP 4: Post-Bootstrap Actions (Async, Non-Blocking)                                │
├─────────────────────────────────────────────────────────────────────────────────────┤
│ • Emit TenantCreatedEvent (for analytics/logging)                                   │
│ • Send welcome email with login credentials                                         │
│ • Initialize audit log                                                              │
│ • Warm cache (optional)                                                             │
└─────────────────────────────────────────────────────────────────────────────────────┘
                                              │
                                              ▼
┌─────────────────────────────────────────────────────────────────────────────────────┐
│ STEP 5: First POS Login                                                             │
├─────────────────────────────────────────────────────────────────────────────────────┤
│ • User logs in with provided credentials                                            │
│ • System identifies tenant from user                                                │
│ • Onboarding checklist presented                                                    │
│ • Storefront remains disabled until explicitly enabled                              │
└─────────────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Subdomain Strategy & Validation Rules

### 2.1 Subdomain Allocation Model

Each tenant receives a unique subdomain for their storefront:

```
Pattern: {slug}.{platform-domain}

Examples:
  - acme-electronics.myposplatform.com
  - fashion-house.myposplatform.com
  - gadget-store.myposplatform.com
```

### 2.2 Subdomain Validation Rules

#### Format Requirements

| Rule | Constraint | Example |
|------|------------|---------|
| **Length** | 3-63 characters | ✅ `my-store` ❌ `ab` |
| **Character Set** | Lowercase alphanumeric + hyphen | ✅ `cool-store` ❌ `Cool_Store` |
| **Start/End** | Must start/end with alphanumeric | ✅ `store1` ❌ `-store-` |
| **No Consecutive Hyphens** | Cannot have `--` | ✅ `my-store` ❌ `my--store` |
| **Unicode** | ASCII only (no internationalized domain) | ✅ `store` ❌ `متجر` |

#### Validation Regex
```regex
^[a-z0-9]([a-z0-9-]{1,61}[a-z0-9])?$
```

### 2.3 Reserved Subdomains

The following subdomains are **permanently reserved** and cannot be allocated:

| Category | Reserved Names |
|----------|----------------|
| **System** | `www`, `api`, `admin`, `app`, `portal`, `dashboard` |
| **Infrastructure** | `static`, `cdn`, `assets`, `media`, `files`, `img`, `images` |
| **Mail** | `mail`, `smtp`, `imap`, `pop`, `mx`, `email` |
| **Development** | `dev`, `staging`, `test`, `qa`, `uat`, `sandbox`, `demo` |
| **Security** | `auth`, `oauth`, `sso`, `login`, `secure`, `ssl` |
| **Offensive** | (list of inappropriate terms - maintained separately) |
| **Generic** | `store`, `shop`, `buy`, `sell`, `ecommerce`, `pos`, `online` |
| **Local** | `localhost`, `local`, `internal` |

### 2.4 Subdomain Uniqueness

- Subdomains are **globally unique** across all tenants
- Uniqueness check is **case-insensitive** (`STORE` === `store`)
- Soft-deleted tenant subdomains become available after **90-day retention period**
- Optional: Subdomain reservation for premium/enterprise plans (future)

### 2.5 Subdomain Modification

| Allowed | Not Allowed |
|---------|-------------|
| Change subdomain (admin action) | Have multiple subdomains per tenant |
| Release subdomain (on delete) | Re-use subdomain immediately after release |

---

## 3. Automatic Bootstrap Steps (Transactional)

### 3.1 Bootstrap Transaction Scope

All bootstrap steps execute within a **single database transaction**. Failure at any step results in **complete rollback** – no partial tenants.

```csharp
// Conceptual flow (not implementation)
using var transaction = await dbContext.Database.BeginTransactionAsync();

try {
    // Step 1: Create Tenant
    // Step 2: Create Admin User
    // Step 3: Create StoreConfig
    // Step 4: Create Default Warehouse
    // Step 5: Create Default Payment Methods
    // Step 6: Create Root Category
    // Step 7: Create Onboarding Record
    // Step 8: Finalize Tenant State
    
    await transaction.CommitAsync();
} catch {
    await transaction.RollbackAsync();
    throw;
}
```

### 3.2 Bootstrap Steps Detail

#### Step 1: Create Tenant Record

| Field | Value | Notes |
|-------|-------|-------|
| `Id` | New GUID | Immutable after creation |
| `Name` | From registration | Display name |
| `Slug` | Validated subdomain | Unique, immutable initially |
| `IsActive` | `true` | Active for operations |
| `Status` | `PENDING_SETUP` | Transitions to ACTIVE after bootstrap |
| `ContactEmail` | From registration | Primary contact |
| `CreatedAtUtc` | Current UTC time | Audit timestamp |
| `Plan` | `FREE` or `TRIAL` | Billing-related (future) |

#### Step 2: Create Primary Admin User

| Field | Value | Notes |
|-------|-------|-------|
| `Id` | New GUID | User identifier |
| `TenantId` | From Step 1 | Links user to tenant |
| `Email` | From registration | Login credential |
| `PasswordHash` | Generated or from input | Secure password |
| `Role` | `OWNER` | Highest privilege level |
| `IsActive` | `true` | Can log in immediately |
| `CreatedAtUtc` | Current UTC time | Audit timestamp |

#### Step 3: Create StoreConfig

| Field | Value | Notes |
|-------|-------|-------|
| `Id` | New GUID | Config identifier |
| `TenantId` | From Step 1 | One per tenant |
| `StoreName` | From tenant name | Can be changed later |
| `StorefrontStatus` | `disabled` | **Safety: starts disabled** |
| `ThemeId` | `default` | Default theme |
| `Theme` | Default colors/fonts | Safe defaults |
| `Behavior.EnableGuestCheckout` | `true` | Recommended default |
| `CreatedAtUtc` | Current UTC time | Audit timestamp |

**Critical:** `StorefrontStatus = disabled` ensures no accidental public exposure.

#### Step 4: Create Default Warehouse

| Field | Value | Notes |
|-------|-------|-------|
| `Id` | New GUID | Warehouse identifier |
| `TenantId` | From Step 1 | Tenant-scoped |
| `Name` | `Main Warehouse` | Localized if applicable |
| `IsDefault` | `true` | Primary warehouse |
| `IsActive` | `true` | Ready for inventory |

#### Step 5: Create Default Payment Methods

| Method | Enabled | Notes |
|--------|---------|-------|
| `COD` (Cash on Delivery) | `true` | Safe default |
| `CREDIT_CARD` | `false` | Requires merchant setup |
| `BANK_TRANSFER` | `false` | Requires bank details |

#### Step 6: Create Root Category

| Field | Value | Notes |
|-------|-------|-------|
| `Id` | New GUID | Category identifier |
| `TenantId` | From Step 1 | Tenant-scoped |
| `Name` | `Uncategorized` | Default parent |
| `ParentId` | `null` | Root level |
| `IsActive` | `true` | Available for products |

#### Step 7: Create Onboarding Checklist Record

| Checklist Item | Status | Required |
|----------------|--------|----------|
| Add first product | `pending` | ✅ |
| Set up categories | `pending` | ❌ |
| Configure store details | `pending` | ✅ |
| Add warehouse stock | `pending` | ✅ |
| Enable storefront | `pending` | ✅ |
| Complete payment setup | `pending` | ❌ |

#### Step 8: Finalize Tenant State

```
Tenant.Status: PENDING_SETUP → ACTIVE
```

### 3.3 Bootstrap Timing

| Metric | Target | Acceptable |
|--------|--------|------------|
| Total bootstrap time | < 500ms | < 2s |
| Database round trips | ≤ 3 | ≤ 5 |
| Transaction duration | < 1s | < 3s |

### 3.4 Bootstrap Idempotency

- Bootstrap uses **idempotency key** (request ID)
- Duplicate requests return existing tenant if already created
- No duplicate tenants possible for same registration request

---

## 4. POS ↔ Storefront Relationship

### 4.1 Architectural Boundary

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         SYSTEM ARCHITECTURE                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│    ┌────────────────────────────┐      ┌────────────────────────────┐       │
│    │         POS ADMIN          │      │        STOREFRONT          │       │
│    │       (React SPA)          │      │       (React SPA)          │       │
│    │                            │      │                            │       │
│    │  • Full CRUD operations    │      │  • READ-ONLY display       │       │
│    │  • Inventory management    │      │  • Cart (ephemeral)        │       │
│    │  • Order management        │      │  • Checkout                │       │
│    │  • Store configuration     │      │  • Order placement         │       │
│    │  • User management         │      │                            │       │
│    └──────────────┬─────────────┘      └──────────────┬─────────────┘       │
│                   │                                   │                     │
│                   │         ┌─────────────┐           │                     │
│                   └────────▶│   BACKEND   │◀──────────┘                     │
│                             │    API      │                                 │
│                             │             │                                 │
│                             │ • Auth      │                                 │
│                             │ • Business  │                                 │
│                             │   Logic     │                                 │
│                             └──────┬──────┘                                 │
│                                    │                                        │
│                    ┌───────────────┼───────────────┐                        │
│                    ▼               ▼               ▼                        │
│              ┌──────────┐   ┌──────────┐   ┌──────────┐                     │
│              │PostgreSQL│   │  MongoDB │   │  Redis   │                     │
│              │ (Truth)  │   │  (Read)  │   │ (Cache)  │                     │
│              └──────────┘   └──────────┘   └──────────┘                     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 4.2 Data Flow Rules

| Direction | Allowed Operations | Notes |
|-----------|-------------------|-------|
| POS → PostgreSQL | Full CRUD | Source of truth |
| POS → MongoDB | Sync (via events) | Denormalized reads |
| POS → Redis | Cache invalidation | On data changes |
| Storefront → Redis | Read | Cache-first pattern |
| Storefront → MongoDB | Read | When cache miss |
| Storefront → PostgreSQL | ❌ Never | No direct access |

### 4.3 Tenant Context Propagation

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                     TENANT CONTEXT FLOW                                      │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  POS Login                                                                  │
│      │ User authenticates with credentials                                  │
│      ▼                                                                      │
│  JWT Token                                                                  │
│      │ Contains: UserId, TenantId, Role                                     │
│      ▼                                                                      │
│  API Request                                                                │
│      │ Authorization: Bearer {jwt}                                          │
│      ▼                                                                      │
│  TenantContext.TenantId = extracted from JWT                                │
│                                                                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Storefront Bootstrap                                                       │
│      │ Resolves tenant from subdomain                                       │
│      ▼                                                                      │
│  API Request                                                                │
│      │ X-Tenant-Id: {tenant-guid}                                           │
│      │ X-Session-Id: {session-guid}                                         │
│      ▼                                                                      │
│  TenantContext.TenantId = extracted from header                             │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 4.4 Storefront Visibility Control

The POS controls whether the Storefront is accessible:

| StoreConfig.StorefrontStatus | Storefront Behavior |
|------------------------------|---------------------|
| `disabled` | Show "Store Not Available" page |
| `live` | Full shopping experience |
| `maintenance` | Show maintenance message |

**Flow:**
1. POS Admin → Settings → Online Store
2. Toggle Storefront status
3. Save (emits StoreConfigUpdatedEvent)
4. Cache invalidated
5. Storefront reads new status on next request

---

## 5. Default States After Registration

### 5.1 Tenant Defaults

| Entity | Default State | Rationale |
|--------|---------------|-----------|
| **Tenant** | `ACTIVE` | Ready for use |
| **StorefrontStatus** | `disabled` | Safety: no accidental exposure |
| **Primary User** | `OWNER` role, active | Can log in immediately |
| **Warehouse** | 1 default warehouse | Ready for inventory |
| **Categories** | 1 root "Uncategorized" | Products can be created |
| **Products** | None | Merchant adds these |
| **Payment Methods** | COD enabled | Simple start |
| **Shipping** | None configured | Merchant must add |

### 5.2 Configuration Defaults

#### Theme Defaults

```json
{
  "themeId": "default",
  "theme": {
    "colors": {
      "primary": "#2563eb",
      "secondary": "#0f172a",
      "background": "#ffffff",
      "text": "#020617",
      "muted": "#64748b"
    },
    "font": {
      "family": "Inter",
      "baseSize": "16px"
    },
    "ui": {
      "borderRadius": "8px",
      "buttonStyle": "solid"
    },
    "layout": {
      "productCardVariant": "default",
      "headerVariant": "standard"
    }
  }
}
```

#### Behavior Defaults

```json
{
  "behavior": {
    "enableGuestCheckout": true,
    "showOutOfStockProducts": false,
    "enableSearch": true
  }
}
```

### 5.3 What's NOT Created by Default

| Entity | Reason |
|--------|--------|
| Products | Merchant-specific |
| Custom categories | Merchant-specific |
| Users (beyond owner) | Optional |
| Shipping methods | Region-specific |
| Online payment | Requires merchant account |
| Custom domain | Premium feature |

---

## 6. Onboarding Checklist Concept

### 6.1 Checklist Purpose

Guide new merchants through essential setup steps to achieve a operational online store.

### 6.2 Checklist Items

| Step | Title | Description | Required | Verification |
|------|-------|-------------|----------|--------------|
| 1 | Complete Store Profile | Add store name, description, logo | ✅ | `StoreConfig.StoreName != default` |
| 2 | Add First Category | Create at least one product category | ❌ | `Categories.Count > 1` |
| 3 | Add First Product | Create at least one product | ✅ | `Products.Count >= 1` |
| 4 | Set Up Inventory | Add stock to warehouse | ✅ | `WarehouseStock.Sum > 0` |
| 5 | Configure Shipping | Add at least one shipping method | ✅ | `ShippingMethods.Count >= 1` |
| 6 | Review Payment Options | Verify payment methods | ❌ | Always complete (COD default) |
| 7 | Enable Storefront | Go live | ✅ | `StorefrontStatus = 'live'` |

### 6.3 Checklist UI Concept

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  🎉 Welcome to Your New Store!                                               │
│                                                                              │
│  Complete these steps to launch your online store                            │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │ ✅ Complete Store Profile                              [Completed]  │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │ ⬜ Add First Category                                    [→ Start]  │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │ ⬜ Add First Product                                     [→ Start]  │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │ ⬜ Set Up Inventory                                    [Blocked] 🔒 │    │
│  │    (Complete "Add First Product" first)                             │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │ ⬜ Configure Shipping                                    [→ Start]  │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │ ⬜ Enable Storefront                                   [Blocked] 🔒 │    │
│  │    (Complete required steps first)                                  │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                              │
│  Progress: ███░░░░░░░ 2/7 Complete                                           │
│                                                                              │
│  [Skip for Now]                         [I'll set this up later →]          │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 6.4 Checklist Data Model

```
OnboardingChecklist {
    Id: GUID
    TenantId: GUID (unique)
    Items: [
        { Key: "store_profile", Status: "completed" | "pending" | "skipped" }
        { Key: "first_category", Status: "pending" }
        { Key: "first_product", Status: "pending" }
        { Key: "inventory_setup", Status: "blocked" }
        { Key: "shipping_config", Status: "pending" }
        { Key: "payment_review", Status: "completed" }
        { Key: "enable_storefront", Status: "blocked" }
    ]
    CompletedAtUtc: DateTime? (null until all required complete)
    DismissedAtUtc: DateTime? (if user chose to hide)
    CreatedAtUtc: DateTime
    UpdatedAtUtc: DateTime
}
```

### 6.5 Checklist Behavior

- Appears on POS dashboard after first login
- Progress saved and persists across sessions
- Can be dismissed (hidden) but not deleted
- Reappears if tenant goes back to incomplete state
- "Enable Storefront" only unlocks when all required steps complete

---

## 7. Edge Cases & Failure Handling

### 7.1 Registration Failures

| Scenario | System Response | User Message |
|----------|-----------------|--------------|
| Subdomain already taken | Reject with 409 Conflict | "This subdomain is not available. Try another." |
| Invalid subdomain format | Reject with 400 Bad Request | "Subdomain must be 3-63 characters, lowercase letters, numbers, and hyphens only." |
| Reserved subdomain | Reject with 400 Bad Request | "This subdomain is reserved and cannot be used." |
| Email already registered | Reject with 409 Conflict | "An account with this email already exists." |
| Database transaction failure | Rollback, reject with 500 | "Registration failed. Please try again." |
| Email service down | Complete registration, queue email | (Silent retry, no user impact) |

### 7.2 Bootstrap Failures

| Failure Point | Recovery Action | Rollback Scope |
|---------------|-----------------|----------------|
| Tenant creation fails | Full rollback | Nothing persisted |
| User creation fails | Full rollback | Tenant deleted |
| StoreConfig creation fails | Full rollback | All deleted |
| Warehouse creation fails | Full rollback | All deleted |
| Event publishing fails | Log warning, continue | Transaction committed |
| Email sending fails | Queue for retry | Transaction committed |

### 7.3 Tenant State Transition Failures

| Transition | Failure Scenario | Handling |
|------------|------------------|----------|
| PENDING → ACTIVE | Bootstrap incomplete | Retry bootstrap |
| ACTIVE → SUSPENDED | Concurrent update | Optimistic concurrency, retry |
| SUSPENDED → ACTIVE | Payment still failed | Reject transition |
| Any → DELETED | Active orders exist | Reject with reason |

### 7.4 Subdomain Conflicts

| Scenario | Resolution |
|----------|------------|
| Race condition (two registrations same slug) | Database unique constraint, first wins, second gets 409 |
| Slug becomes reserved after registration | Existing tenant keeps slug, no new allocations |
| Tenant deleted, slug in retention | Slug unavailable for 90 days |
| Custom domain conflicts with subdomain | Custom domain takes priority (future feature) |

### 7.5 Data Migration Edge Cases

| Scenario | Handling |
|----------|----------|
| Existing tenant without StoreConfig | Bootstrap creates default on first access |
| Existing tenant without Warehouse | Bootstrap creates default on first access |
| Existing tenant without Onboarding record | Create on first dashboard access |
| Corrupted tenant data | Admin intervention required |

---

## 8. Explicit Non-Goals (NOT in Scope)

The following features are **explicitly excluded** from Phase 8.0:

### 8.1 Authentication & Authorization

| Not Included | Rationale |
|--------------|-----------|
| OAuth / Social login | Phase 8.1+ |
| Two-factor authentication | Phase 8.2+ |
| SSO / SAML | Enterprise feature |
| Password reset flow | Phase 8.1 |
| Email verification | Phase 8.1 |

### 8.2 Billing & Plans

| Not Included | Rationale |
|--------------|-----------|
| Subscription plans | Future phase |
| Payment processing | Requires billing system |
| Usage quotas | Requires billing system |
| Feature flags per plan | Requires plan system |
| Trial expiration | Requires plan system |

### 8.3 Advanced Tenant Features

| Not Included | Rationale |
|--------------|-----------|
| Custom domain mapping | Phase 8.5+ |
| Multi-subdomain per tenant | Not in roadmap |
| Tenant cloning | Not in roadmap |
| Tenant migration (between environments) | Operational, not product |
| White-labeling | Enterprise feature |

### 8.4 Self-Service Operations

| Not Included | Rationale |
|--------------|-----------|
| Self-service subdomain change | Admin-only initially |
| Self-service account deletion | Legal/compliance review needed |
| Self-service data export | GDPR feature, future phase |
| Self-service plan upgrade | Requires billing |

### 8.5 Infrastructure

| Not Included | Rationale |
|--------------|-----------|
| Auto-scaling per tenant | Infrastructure concern |
| Tenant-specific database sharding | Not needed at current scale |
| Geographic tenant routing | CDN/infrastructure concern |
| Tenant-specific rate limiting | Phase 8.3+ |

---

## 9. Data Model Extensions

### 9.1 Tenant Entity (Extended)

```
Tenant {
    // Existing fields
    Id: GUID (PK)
    Name: string (100 chars)
    Slug: string (63 chars, unique)
    IsActive: boolean
    ContactEmail: string
    CreatedAtUtc: DateTime
    
    // New fields for Phase 8.0
    Status: enum (PENDING_SETUP, ACTIVE, SUSPENDED, DELETED)
    Plan: enum (FREE, TRIAL, BASIC, PRO) - default FREE
    TrialEndsAtUtc: DateTime? - null if no trial
    SuspendedReason: string? - null if not suspended
    DeletedAtUtc: DateTime? - soft delete timestamp
    BootstrapVersion: int - tracks bootstrap schema version
}
```

### 9.2 TenantUser Entity (New)

```
TenantUser {
    Id: GUID (PK)
    TenantId: GUID (FK → Tenant)
    Email: string (unique per tenant)
    PasswordHash: string
    Role: enum (OWNER, ADMIN, MANAGER, CASHIER)
    IsActive: boolean
    LastLoginAtUtc: DateTime?
    CreatedAtUtc: DateTime
    UpdatedAtUtc: DateTime
}
```

### 9.3 OnboardingChecklist Entity (New)

```
OnboardingChecklist {
    Id: GUID (PK)
    TenantId: GUID (FK → Tenant, unique)
    ItemsJson: JSON (serialized checklist state)
    CompletedAtUtc: DateTime?
    DismissedAtUtc: DateTime?
    CreatedAtUtc: DateTime
    UpdatedAtUtc: DateTime
}
```

---

## 10. API Contracts (Conceptual)

### 10.1 Registration Endpoint

```
POST /api/platform/register

Request:
{
    "businessName": "Acme Electronics",
    "email": "owner@acme.com",
    "password": "SecureP@ssw0rd",
    "subdomain": "acme-electronics",
    "country": "US",        // optional
    "currency": "USD"       // optional
}

Success Response (201 Created):
{
    "tenantId": "uuid",
    "subdomain": "acme-electronics",
    "storeName": "Acme Electronics",
    "storefrontUrl": "https://acme-electronics.myposplatform.com",
    "posUrl": "https://pos.myposplatform.com",
    "status": "active",
    "message": "Registration successful. Check your email for login details."
}

Error Response (409 Conflict):
{
    "error": "SUBDOMAIN_TAKEN",
    "message": "This subdomain is not available.",
    "suggestions": ["acme-electronics-1", "acme-store", "acme-shop"]
}
```

### 10.2 Subdomain Availability Check

```
GET /api/platform/subdomain/check?slug=acme-electronics

Response (Available):
{
    "available": true,
    "slug": "acme-electronics"
}

Response (Taken):
{
    "available": false,
    "slug": "acme-electronics",
    "suggestions": ["acme-electronics-1", "acme-store", "acme-shop"]
}
```

---

## 11. Events Emitted

### 11.1 Integration Events

| Event | Trigger | Payload |
|-------|---------|---------|
| `TenantCreatedEvent` | After successful bootstrap | TenantId, Slug, Name, OwnerEmail |
| `TenantStatusChangedEvent` | Status transition | TenantId, OldStatus, NewStatus, Reason |
| `TenantDeletedEvent` | Soft delete | TenantId, DeletedAtUtc |
| `TenantUserCreatedEvent` | User added | TenantId, UserId, Email, Role |

### 11.2 Event Consumers (Future)

- Analytics service: Track registrations
- Notification service: Send emails
- Audit service: Log state changes
- Billing service: Initialize billing record

---

## 12. Security Considerations

### 12.1 Data Isolation

- All queries include `TenantId` filter (global query filter)
- No cross-tenant data access possible at DB level
- Tenant context validated on every request

### 12.2 Authentication

- Password hashing: bcrypt with work factor 12
- JWT tokens contain TenantId claim
- Token expiry: 24 hours (configurable)
- Refresh tokens: 30 days (future)

### 12.3 Rate Limiting

- Registration: 5 requests per IP per hour
- Subdomain check: 30 requests per IP per minute
- Failed login: 5 attempts lock for 15 minutes

---

## 13. Success Criteria

Before implementation begins, verify this design meets:

| Criterion | Validation |
|-----------|------------|
| ✅ Complete lifecycle defined | All states, transitions documented |
| ✅ Subdomain rules comprehensive | Validation, reservation, conflicts covered |
| ✅ Bootstrap is atomic | Single transaction, rollback on any failure |
| ✅ POS/Storefront boundary clear | Read-only storefront, POS controls everything |
| ✅ Defaults are safe | Storefront disabled, minimal configuration |
| ✅ Onboarding guidance exists | Checklist concept defined |
| ✅ Edge cases documented | Failure scenarios and handling specified |
| ✅ Non-goals explicit | Clear boundaries on what's not included |
| ✅ No implementation decisions skipped | All questions answered |
| ✅ Suitable for first merchants | Can onboard real users with this design |

---

## 14. Implementation Readiness Checklist

Before moving to implementation phase:

- [ ] Design reviewed by Product Owner
- [ ] Design reviewed by Tech Lead
- [ ] Edge cases validated against real scenarios
- [ ] Database schema impact assessed
- [ ] API contracts reviewed for consistency
- [ ] Security review completed
- [ ] Performance targets defined
- [ ] Monitoring requirements documented
- [ ] Rollback strategy defined

---

## Appendix A: Glossary

| Term | Definition |
|------|------------|
| **Tenant** | A business/store that uses the platform |
| **Subdomain** | The unique identifier in the URL (slug.platform.com) |
| **Slug** | URL-safe version of tenant identifier |
| **Bootstrap** | The atomic process of setting up a new tenant |
| **POS** | Point of Sale - the admin application |
| **Storefront** | The customer-facing e-commerce site |
| **Onboarding** | The guided setup process for new merchants |

## Appendix B: Related Documents

- `MULTI-TENANCY-FOUNDATION.md` - Tenant isolation architecture
- `PHASE-7.1-STORE-CONFIG-IMPLEMENTATION.md` - Store configuration
- `PHASE-7.4-TENANT-RESOLUTION-BOOTSTRAP.md` - Storefront bootstrap
- `ECOMMERCE-ARCHITECTURE.md` - Overall system architecture
- `LAUNCH-NOTES.md` - Deployment and operational notes

---

*End of Design Document*
