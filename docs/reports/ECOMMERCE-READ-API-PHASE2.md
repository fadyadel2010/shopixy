# E-commerce Read API - Phase 2 Architecture

## Document Status: FROZEN
**Version:** 1.0  
**Date:** 2025-12-23  
**Phase:** 2.0 - Read API Contracts  

---

## 1. API Surface Definition

### Base Path
```
/api/storefront
```

### Endpoints

| Method | Endpoint | Description | Cache Strategy |
|--------|----------|-------------|----------------|
| GET | `/products/{slug}` | Get single product by slug | Redis → MongoDB |
| GET | `/products` | Get product list | Redis → MongoDB |
| GET | `/products/search` | Search products | Elasticsearch → MongoDB |

### Endpoint Details

#### 1.1 Get Single Product
```http
GET /api/storefront/products/{slug}
X-Tenant-Id: {tenant-guid} (REQUIRED)
```

**Path Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| slug | string | Yes | SEO-friendly product slug |

**Response:** `ProductReadDto`

---

#### 1.2 Get Product List
```http
GET /api/storefront/products
X-Tenant-Id: {tenant-guid} (REQUIRED)
```

**Query Parameters:**
| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| page | int | No | 1 | Page number (1-indexed) |
| pageSize | int | No | 20 | Items per page (max: 50) |
| categoryId | guid | No | null | Filter by category |
| isActive | bool | No | true | Filter active products only |

**Response:** `PagedResult<ProductListItemDto>`

---

#### 1.3 Search Products
```http
GET /api/storefront/products/search
X-Tenant-Id: {tenant-guid} (REQUIRED)
```

**Query Parameters:**
| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| q | string | Yes | - | Search query (min 2 chars) |
| page | int | No | 1 | Page number |
| pageSize | int | No | 20 | Items per page (max: 50) |
| categoryId | guid | No | null | Filter by category |
| minPrice | decimal | No | null | Minimum price filter |
| maxPrice | decimal | No | null | Maximum price filter |

**Response:** `SearchResult<ProductListItemDto>`

---

## 2. Request & Response Contracts

### 2.1 ProductReadDto (Single Product - Full Detail)
```csharp
public class ProductReadDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public string? Description { get; set; }
    public string? SKU { get; set; }
    public string? Barcode { get; set; }
    public decimal Price { get; set; }
    public string? CategoryName { get; set; }
    public Guid? CategoryId { get; set; }
    public string? Slug { get; set; }
    public List<string> Images { get; set; } = new();
    public bool IsAvailable { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
```

### 2.2 ProductListItemDto (List/Search - Minimal)
```csharp
public class ProductListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? NameAr { get; set; }
    public decimal Price { get; set; }
    public string? CategoryName { get; set; }
    public string? Slug { get; set; }
    public string? ThumbnailImage { get; set; }
    public bool IsAvailable { get; set; }
}
```

### 2.3 PagedResult<T> (List Response)
```csharp
public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
}
```

### 2.4 SearchResult<T> (Search Response)
```csharp
public class SearchResult<T>
{
    public List<T> Items { get; set; } = new();
    public string Query { get; set; } = string.Empty;
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public long SearchTimeMs { get; set; }
}
```

### 2.5 Error Response Contract
```csharp
public class ApiErrorResponse
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? TraceId { get; set; }
}
```

---

## 3. Read Flow Definition

### 3.1 Single Product Read Flow
```
┌─────────────────────────────────────────────────────────────────┐
│  GET /api/storefront/products/{slug}                           │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  1. Validate X-Tenant-Id header                                 │
│     - Missing → 400 Bad Request                                 │
│     - Invalid GUID → 400 Bad Request                            │
│     - Tenant not found → 404 Not Found                          │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  2. Check Redis Cache                                           │
│     Key: tenant:{tenantId}:product:slug:{slug}                  │
│     - HIT → Return cached ProductReadDto                        │
│     - MISS → Continue to MongoDB                                │
│     - ERROR → Log & Continue (graceful degradation)             │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  3. Query MongoDB                                               │
│     Filter: { TenantId, Slug, IsOnlineVisible: true }           │
│     - FOUND → Map to ProductReadDto                             │
│     - NOT FOUND → 404 Not Found                                 │
│     - ERROR → 503 Service Unavailable                           │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  4. Populate Redis Cache (async, fire-and-forget)               │
│     TTL: 10 minutes                                             │
│     - SUCCESS → Cache populated                                 │
│     - ERROR → Log only (do not fail request)                    │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  5. Return ProductReadDto                                       │
│     Status: 200 OK                                              │
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 Product List Read Flow
```
┌─────────────────────────────────────────────────────────────────┐
│  GET /api/storefront/products?page=1&pageSize=20                │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  1. Validate X-Tenant-Id header (same as single product)        │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  2. Check Redis Cache                                           │
│     Key: tenant:{tenantId}:products:list:{hash(queryParams)}    │
│     - HIT → Return cached PagedResult                           │
│     - MISS → Continue to MongoDB                                │
│     - ERROR → Log & Continue                                    │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  3. Query MongoDB                                               │
│     Filter: { TenantId, IsOnlineVisible: true, ...filters }     │
│     Sort: CreatedAtUtc DESC                                     │
│     Skip/Limit: Pagination applied                              │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  4. Populate Redis Cache (async)                                │
│     TTL: 5 minutes (shorter for lists)                          │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  5. Return PagedResult<ProductListItemDto>                      │
│     Status: 200 OK                                              │
└─────────────────────────────────────────────────────────────────┘
```

### 3.3 Product Search Flow
```
┌─────────────────────────────────────────────────────────────────┐
│  GET /api/storefront/products/search?q=laptop                   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  1. Validate X-Tenant-Id header (same as above)                 │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  2. Validate Search Query                                       │
│     - Empty/null → 400 Bad Request                              │
│     - Less than 2 chars → 400 Bad Request                       │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  3. Query Elasticsearch                                         │
│     Index: products                                             │
│     Query: Multi-match on Name, NameAr, SKU, CategoryName       │
│     Filter: TenantId (MUST), IsActive, IsOnlineVisible          │
│     - SUCCESS → Get list of ProductIds + scores                 │
│     - ERROR → Fallback to MongoDB text search                   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  4. Hydrate from MongoDB (if needed)                            │
│     - Elasticsearch returns minimal data                        │
│     - Fetch full documents from MongoDB by ProductIds           │
│     - Preserve search result order                              │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  5. Return SearchResult<ProductListItemDto>                     │
│     Status: 200 OK                                              │
│     Note: Search results are NOT cached (dynamic)               │
└─────────────────────────────────────────────────────────────────┘
```

---

## 4. Tenant Enforcement Rules

### 4.1 Header Requirement
| Header | Required | Format | Description |
|--------|----------|--------|-------------|
| X-Tenant-Id | YES | GUID | Tenant identifier |

### 4.2 Validation Rules
```csharp
// Validation sequence (in order)
1. Header presence check
   - Missing → 400 Bad Request, Code: "TENANT_HEADER_MISSING"

2. GUID format validation
   - Invalid GUID → 400 Bad Request, Code: "TENANT_HEADER_INVALID"

3. Tenant existence check (optional - may be deferred to query)
   - Tenant not found → 404 Not Found, Code: "TENANT_NOT_FOUND"
```

### 4.3 Data Isolation Rules
```
┌─────────────────────────────────────────────────────────────────┐
│  CRITICAL: ALL QUERIES MUST INCLUDE TenantId FILTER            │
├─────────────────────────────────────────────────────────────────┤
│  ✓ Redis keys are prefixed with tenant:{tenantId}              │
│  ✓ MongoDB queries filter by TenantId                          │
│  ✓ Elasticsearch queries filter by TenantId                    │
│  ✗ NEVER return data from other tenants                        │
│  ✗ NEVER cache without tenant prefix                           │
└─────────────────────────────────────────────────────────────────┘
```

---

## 5. Error Handling Semantics

### 5.1 HTTP Status Code Matrix

| Scenario | Status Code | Error Code | Message |
|----------|-------------|------------|---------|
| Missing X-Tenant-Id | 400 | TENANT_HEADER_MISSING | X-Tenant-Id header is required |
| Invalid X-Tenant-Id format | 400 | TENANT_HEADER_INVALID | X-Tenant-Id must be a valid GUID |
| Tenant not found | 404 | TENANT_NOT_FOUND | Tenant not found |
| Product not found | 404 | PRODUCT_NOT_FOUND | Product not found |
| Invalid search query | 400 | INVALID_SEARCH_QUERY | Search query must be at least 2 characters |
| Invalid pagination | 400 | INVALID_PAGINATION | Page and pageSize must be positive integers |
| MongoDB unavailable | 503 | SERVICE_UNAVAILABLE | Service temporarily unavailable |
| Rate limit exceeded | 429 | RATE_LIMIT_EXCEEDED | Too many requests |
| Internal error | 500 | INTERNAL_ERROR | An unexpected error occurred |

### 5.2 Graceful Degradation Rules

| Component | Failure Behavior | Impact |
|-----------|------------------|--------|
| **Redis (cache miss/error)** | Continue to MongoDB | Slower response, no user impact |
| **Redis (write error)** | Log and continue | Cache not populated, no user impact |
| **Elasticsearch (error)** | Fallback to MongoDB text search | Degraded search quality, still functional |
| **MongoDB (error)** | Return 503 | Request fails (MongoDB is required) |

### 5.3 Error Response Format
```json
{
  "code": "PRODUCT_NOT_FOUND",
  "message": "Product not found",
  "traceId": "abc123-def456"
}
```

---

## 6. Cache Key Strategy

### 6.1 Key Patterns
```
# Single product by slug
tenant:{tenantId}:product:slug:{slug}

# Single product by ID
tenant:{tenantId}:product:id:{productId}

# Product list (with query hash)
tenant:{tenantId}:products:list:{queryHash}

# Category products
tenant:{tenantId}:products:category:{categoryId}:{page}:{pageSize}
```

### 6.2 TTL Configuration
| Cache Type | TTL | Rationale |
|------------|-----|-----------|
| Single Product | 10 min | Products change infrequently |
| Product List | 5 min | Lists are more dynamic |
| Search Results | NOT CACHED | Search must be fresh |

### 6.3 Invalidation Triggers
| Event | Invalidation Action |
|-------|---------------------|
| ProductCreatedEvent | Invalidate list caches for tenant |
| ProductUpdatedEvent | Invalidate specific product + list caches |
| ProductDeletedEvent | Invalidate specific product + list caches |

---

## 7. Non-Goals (Phase 2.0)

| Feature | Status | Notes |
|---------|--------|-------|
| Write operations | ❌ NOT IN SCOPE | Read-only phase |
| Complex sorting | ❌ NOT IN SCOPE | Default sort only |
| Faceted search | ❌ NOT IN SCOPE | Future phase |
| Product reviews | ❌ NOT IN SCOPE | Future phase |
| Inventory levels | ❌ NOT IN SCOPE | Future phase |
| Wishlists | ❌ NOT IN SCOPE | Future phase |
| Cart operations | ❌ NOT IN SCOPE | Future phase |
| PostgreSQL access | ❌ NOT IN SCOPE | Read from MongoDB only |

---

## 8. Implementation Checklist (Phase 2.1)

```markdown
### Phase 2.1: Read API Implementation
- [ ] Create StorefrontController
- [ ] Create ProductReadDto and related DTOs
- [ ] Implement GetProductBySlug endpoint
- [ ] Implement GetProductList endpoint
- [ ] Implement SearchProducts endpoint
- [ ] Add Redis cache integration
- [ ] Add Elasticsearch search integration
- [ ] Add graceful degradation handling
- [ ] Add structured logging
- [ ] Add unit tests
- [ ] Add integration tests
```

---

## Document Sign-off

| Role | Status |
|------|--------|
| Architecture | ✅ Approved |
| Frontend | 🔲 Pending Review |
| Backend | 🔲 Pending Review |

---

**END OF DOCUMENT**
