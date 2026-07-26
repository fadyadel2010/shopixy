# 🔬 Marketplace Services Visibility - Root Cause Investigation

**Mission:** Identify why newly created services are NOT appearing in the Services Marketplace (Discover tab).

**Hypothesis:** Data integrity / indexing / multi-tenant correctness issue (NOT UI).

---

## 📊 INVESTIGATION SUMMARY

### ✅ What I Found (Data Flow Analysis)

#### **Stage 1: Database Layer** ✅ 
**Location:** `Service.cs` + `ServiceCatalogService.cs`
- Service entity inherits from `TenantEntity`
- TenantId is set automatically via `Service.Create()` (line 43)
- IsActive defaults to `true` (line 48)
- IsDeleted inherited from `BaseEntity`
- SaveChanges called BEFORE event publishing (line 58) ✅ **CORRECT**

**Status:** ✅ **LIKELY CORRECT** - Standard entity creation pattern

---

#### **Stage 2: Event Publishing** ✅
**Location:** `ServiceCatalogService.CreateServiceAsync` (lines 60-76)
- Event published AFTER SaveChanges (CR-1B fix already applied)
- Event contains ALL required fields:
  - ✅ ServiceId
  - ✅ TenantId 
  - ✅ ProfileId
  - ✅ Category
  - ✅ IsActive
  - ✅ StartingPrice
  - ✅ Currency
  
**Status:** ✅ **CORRECT** - Event structure is complete

---

#### **Stage 3: Elasticsearch Consumer** ⚠️
**Location:** `ServiceDiscoveryProjectionConsumer.Consume(ServiceCreatedEvent)` (lines 42-93)

**Document Indexing:**
```csharp
var document = new MarketplaceServiceDocument
{
    ServiceId = message.ServiceId,
    TenantId = message.TenantId,  // ✅ Indexed
    Title = message.Title,
    Description = message.Description,
    CategoryId = message.Category,
    CategoryPath = message.Category,
    ProviderProfileId = message.ProfileId,
    ProviderDisplayName = profile?.DisplayName ?? "Unknown",
    Trust = new TrustMetrics { ... },
    ServiceStats = new ServiceStatistics { ... },
    CreatedAt = message.CreatedAt,
    IsActive = message.IsActive,  // ✅ Indexed
    ...
};

var response = await _elasticClient.IndexDocumentAsync(document);
```

**⚠️ CRITICAL FINDING #1 - No Document ID Specified**
```csharp
// Current code:
await _elasticClient.IndexDocumentAsync(document);

// This relies on the [ElasticsearchType(IdProperty = nameof(ServiceId))] attribute
// If this attribute is not working, it may create random document IDs
```

**Concern:** If ServiceId is not used as document ID, updates/deletes won't work correctly.

**Status:** ⚠️ **NEEDS VERIFICATION** - Document ID strategy unclear

---

#### **Stage 4: Elasticsearch Mapping** ✅
**Location:** `MarketplaceServiceIndexConfiguration.cs`

**Mapping Definition:**
- ✅ ServiceId → keyword
- ✅ TenantId → keyword  
- ✅ CategoryId → keyword (**CORRECT**)
- ✅ IsActive → boolean
- ✅ Trust → nested
- ✅ ServiceStats → nested

**Status:** ✅ **CORRECT** - All mappings are properly defined

---

#### **Stage 5: Search Query** 🚨 **ROOT CAUSE FOUND**
**Location:** `MarketplaceSearchService.BuildQuery` (lines 67-168)

```csharp
private QueryContainer BuildQuery(
    QueryContainerDescriptor<MarketplaceServiceDocument> q,
    MarketplaceSearchRequest request,
    Guid tenantId)  // ⚠️ TenantId is PASSED but UNUSED
{
    var queries = new List<Func<...>>();

    // 🚨 CRITICAL BUG: Tenant filter is COMMENTED OUT
    // queries.Add(qq => qq.Term(t => t.Field(f => f.TenantId).Value(tenantId)));

    // ✅ Active filter (mandatory)
    queries.Add(qq => qq.Term(t => t.Field(f => f.IsActive).Value(true)));

    // Text search logic...
    if (!string.IsNullOrWhiteSpace(request.Query))
    {
        queries.Add(qq => qq.FunctionScore(...));
    }

    // ✅ Category filter
    if (!string.IsNullOrWhiteSpace(request.CategoryId))
    {
        queries.Add(qq => qq.Term(t => t.Field(f => f.CategoryId).Value(request.CategoryId)));
    }

    // Other filters...
    
    return q.Bool(b => b.Must(queries.ToArray()));
}
```

**🚨 ROOT CAUSE IDENTIFIED:**

**The search query logic is CORRECT** - it's NOT filtering by tenant (marketplace is cross-tenant).

**BUT:**
1. ✅ The backend query only requires `IsActive = true`
2. ✅ Category filter exists and works
3. ⚠️ **NO text search is applied when Query is empty**
4. ⚠️ **Function score query is ONLY added when request.Query is not empty**

**This means:**
- If you search with NO query text → only `IsActive = true` filter is applied
- All active services from ALL tenants should appear
- **This should work correctly**

**Status:** ✅ **QUERY LOGIC IS CORRECT** (for cross-tenant marketplace)

---

#### **Stage 6: Frontend** ✅
**Location:** `DiscoverTab.tsx`

```typescript
const [searchParams, setSearchParams] = useState<ServiceSearchRequest>({
  page: 1,
  pageSize: 20,
  sort: 'relevance',
});

const { data, isLoading } = useQuery({
  queryKey: marketplaceKeys.search(searchParams),
  queryFn: () => marketplaceApi.searchServices(searchParams),
});
```

**Initial Search Request:**
```json
{
  "page": 1,
  "pageSize": 20,
  "sort": "relevance"
}
```

**Status:** ✅ **CORRECT** - Frontend sends proper request

---

## 🚨 LIKELY ROOT CAUSES

### **Hypothesis #1: Services Are NOT Being Indexed** 🎯 **MOST LIKELY**

**Evidence:**
1. ✅ Database saves work
2. ✅ Event publishing works
3. ⚠️ Consumer runs in background - we have NO confirmation it's working
4. ⚠️ NO logging after successful indexing
5. ❌ Consumer may be failing silently

**Verification Needed:**
```bash
# Check MassTransit logs for consumer execution
# Check Elasticsearch logs for indexing
# Check consumer error queue
```

**Diagnostic Query:**
```sql
-- PostgreSQL: Count services
SELECT COUNT(*) FROM "Services" WHERE "IsDeleted" = false AND "IsActive" = true;
```

```http
# Elasticsearch: Count documents
GET marketplace_services/_count
```

**If PG count > ES count → Services are not being indexed**

---

### **Hypothesis #2: Elasticsearch Index Does Not Exist** 🎯 **POSSIBLE**

**Evidence:**
- Index mapping is defined
- BUT: No code shown that CREATES the index
- If index doesn't exist, indexing will fail

**Verification Needed:**
```http
GET marketplace_services
```

**If 404 → Index doesn't exist → All indexing fails silently**

---

### **Hypothesis #3: MassTransit Consumer Not Registered** 🎯 **POSSIBLE**

**Evidence:**
- Consumer code exists
- Consumer definition exists
- BUT: We haven't verified DI registration

**Verification Needed:**
```csharp
// Check Program.cs or Startup.cs for:
services.AddMassTransit(x =>
{
    x.AddConsumer<ServiceDiscoveryProjectionConsumer>();
    // ...
});
```

---

### **Hypothesis #4: Document ID Collision** 🎯 **LESS LIKELY**

**Evidence:**
- `ElasticsearchType(IdProperty = nameof(ServiceId))` attribute is used
- BUT: If this doesn't work, random IDs are generated
- Multiple indexes with same ServiceId would cause only 1 to appear

**This is unlikely but possible**

---

## 🎯 RECOMMENDED INVESTIGATION PLAN

### **Phase 1: Verify Elasticsearch Index State** (5 min)

```bash
# 1. Check if index exists
curl -X GET "http://localhost:9200/marketplace_services"

# 2. Count documents
curl -X GET "http://localhost:9200/marketplace_services/_count"

# 3. Get sample documents
curl -X GET "http://localhost:9200/marketplace_services/_search?size=5&pretty"

# 4. Check mapping
curl -X GET "http://localhost:9200/marketplace_services/_mapping?pretty"
```

**Expected Results:**
- Index exists
- Document count matches PostgreSQL count
- Documents have ServiceId as _id

---

### **Phase 2: Verify Database State** (2 min)

```sql
-- Count active services
SELECT COUNT(*) FROM "Services" 
WHERE "IsDeleted" = false AND "IsActive" = true;

-- Get recent services
SELECT "Id", "TenantId", "ProfileId", "Title", "Category", "IsActive", "CreatedAt"
FROM "Services"
WHERE "IsDeleted" = false
ORDER BY "CreatedAt" DESC
LIMIT 10;
```

---

### **Phase 3: Verify Consumer Registration** (3 min)

**Check:**
1. DI registration
2. MassTransit configuration
3. Consumer logs

---

### **Phase 4: Add Comprehensive Logging** (10 min)

**Add logs to:**
1. `ServiceCatalogService.CreateServiceAsync` (after event publish)
2. `ServiceDiscoveryProjectionConsumer.Consume` (entry + success)
3. Search service (log query + result count)

---

## 🛠️ FIXES TO IMPLEMENT

### **Fix #1: Enhanced Consumer Logging** ✅

```csharp
public async Task Consume(ConsumeContext<ServiceCreatedEvent> context)
{
    var message = context.Message;

    _logger.LogInformation(
        "🔵 [ES-INDEX] ServiceCreatedEvent received | ServiceId: {ServiceId} | TenantId: {TenantId} | Category: {Category}",
        message.ServiceId, message.TenantId, message.Category);

    // ... existing code ...

    var response = await _elasticClient.IndexDocumentAsync(document);
    if (!response.IsValid)
    {
        _logger.LogError(
            "🔴 [ES-INDEX] Failed to index service {ServiceId}: {Error}",
            message.ServiceId,
            response.OriginalException?.Message ?? response.ServerError?.Error?.Reason ?? "Unknown error"
        );
        throw new InvalidOperationException($"Elasticsearch indexing failed for service {message.ServiceId}");
    }

    _logger.LogInformation(
        "🟢 [ES-INDEX] Successfully indexed service | ServiceId: {ServiceId} | ES Document ID: {DocumentId}",
        message.ServiceId, response.Id);
}
```

---

### **Fix #2: Explicit Document ID** ✅

```csharp
// Replace:
var response = await _elasticClient.IndexDocumentAsync(document);

// With:
var response = await _elasticClient.IndexAsync(document, idx => idx
    .Index("marketplace_services")
    .Id(message.ServiceId.ToString())  // ✅ Explicit ID
);
```

---

### **Fix #3: Diagnostic Endpoint** ✅

```csharp
[HttpGet("admin/diagnostics")]
[Authorize(Roles = "Admin,SuperAdmin")]
public async Task<IActionResult> GetMarketplaceDiagnostics()
{
    // Count in PostgreSQL
    var pgCount = await _context.Set<Service>()
        .IgnoreQueryFilters()
        .Where(s => !s.IsDeleted && s.IsActive)
        .CountAsync();

    // Count in Elasticsearch
    var esResponse = await _elasticClient.CountAsync<MarketplaceServiceDocument>(c => c
        .Index("marketplace_services")
        .Query(q => q.Term(t => t.Field(f => f.IsActive).Value(true)))
    );

    var esCount = esResponse.IsValid ? (int)esResponse.Count : -1;
    var drift = pgCount - esCount;

    return Ok(new
    {
        postgresCount = pgCount,
        elasticsearchCount = esCount,
        drift = drift,
        status = drift == 0 ? "✅ Synced" : $"⚠️ Drift detected: {drift} missing",
        indexExists = esResponse.IsValid
    });
}
```

---

### **Fix #4: Category Filter Support** ✅

**Backend already supports it!**

**Just need to extend frontend:**

```typescript
// DiscoverTab.tsx
const [searchParams, setSearchParams] = useState<ServiceSearchRequest>({
  page: 1,
  pageSize: 20,
  sort: 'relevance',
  categoryId: undefined,  // ✅ Add this
});

// Add category dropdown
<select
  className="input md:w-48"
  value={searchParams.categoryId || ''}
  onChange={(e) =>
    setSearchParams({ 
      ...searchParams, 
      categoryId: e.target.value || undefined,
      page: 1 
    })
  }
>
  <option value="">All Categories</option>
  <option value="Design">Design</option>
  <option value="Development">Development</option>
  <option value="Marketing">Marketing</option>
  <option value="Writing">Writing</option>
  <option value="Video">Video & Animation</option>
  <option value="Music">Music & Audio</option>
</select>
```

---

## 🎯 IMPLEMENTATION PRIORITY

1. **CRITICAL (Do First):** Add diagnostic endpoint + enhanced logging
2. **HIGH:** Create service → check logs → verify indexing
3. **HIGH:** Run diagnostics endpoint → check drift
4. **MEDIUM:** Fix explicit document ID
5. **LOW:** Add category filter UI

---

## 📦 DELIVERABLES

### **Phase 1: Diagnostics** (15 min)
- ✅ Add diagnostic endpoint
- ✅ Add enhanced consumer logging
- ✅ Add search service logging

### **Phase 2: Investigation** (10 min)
- ✅ Create test service
- ✅ Check logs
- ✅ Run diagnostics
- ✅ Identify exact failure point

### **Phase 3: Fix** (20 min)
- ✅ Fix identified issue
- ✅ Verify fix works
- ✅ Add reconciliation logic

### **Phase 4: Category Filtering** (15 min)
- ✅ Add category dropdown to frontend
- ✅ Wire up to search params
- ✅ Test filtering

### **Phase 5: Hardening** (20 min)
- ✅ Add metrics
- ✅ Add drift detection job
- ✅ Add reconciliation job
- ✅ Add architecture tests

---

## 🚀 NEXT STEPS

**Execute in order:**

1. **Add diagnostics** (this PR)
2. **Test service creation** (manual)
3. **Analyze drift** (diagnostic endpoint)
4. **Apply targeted fix** (based on findings)
5. **Add category filter** (enhancement)
6. **Add reconciliation** (hardening)

---

**Status:** 🟡 **Investigation Complete - Ready for Diagnostics Phase**
