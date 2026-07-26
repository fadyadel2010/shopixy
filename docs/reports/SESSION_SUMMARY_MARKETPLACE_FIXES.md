# Marketplace Enhancements Summary
**Date:** 2026-02-18

This document summarizes the changes made to the PointOfSale Marketplace module, focusing on Marketing Review Evidence, Admin Notifications, and Search Engine fixes.

## 1. Marketing Review Evidence Display
**Goal:** Ensure marketing-specific evidence (Campaign Name, Platform, Orders, Revenue) is captured during review creation and displayed on the service details page.

### 🔍 Backend Changes
*   **Controller:** `MarketplaceController.cs`
    *   Updated `GetServiceReviews` to eagerly load `MarketingReviewEvidence`.
    *   Modified the response DTO to include marketing fields contextually.
    *   Updated `GetMyRequests` to return `Service.Category` so the frontend knows when to show the marketing-specific review form.

### 🎨 Frontend Changes
*   **Types:** `types/index.ts`
    *   Added `MarketingEvidence` interface and `MarketingPlatform` type.
    *   Updated `CreateReviewRequest` to include optional evidence.
*   **Components:**
    *   `ReviewModal.tsx`: Rewritten to conditionally render marketing fields (Campaign, Platform, Dates) when the service category is "Marketing". Added validation logic.
    *   `ServiceDetailsPage.tsx`: Updated the review card section to render a "Campaign Details" badge displaying evidence metrics (orders, revenue, verification type) when available.
    *   `MyRequestsTab.tsx`: Passed service category to the review modal.

---

## 2. Marketplace Notification Bell
**Goal:** Provide real-time visibility for the Admin/Provider when new service requests arrive.

### 🔔 Features
*   **Real-time Polling:** Checks for new incoming requests every 30 seconds.
*   **Smart "Seen" Logic:**
    *   Red badge shows count of *unseen* pending requests.
    *   Clicking the bell or a request marks it as "seen" in `localStorage` and clears the badge.
    *   Automatically cleans up seen IDs when requests are no longer pending (accepted/rejected).
*   **Navigation Logic:**
    *   **Pending Requests:** Navigates to `Incoming Requests` tab (since chat is locked until accepted).
    *   **Accepted Requests:** Chat would be accessible normally.
*   **UI:** Added to the top header in `Layout.jsx` next to the settings icon.

### 📂 Files Created/Modified
*   `src/components/MarketplaceNotificationBell.jsx` (New)
*   `src/components/Layout.jsx` (Modified)

---

## 3. Search Engine & Discovery Fixes
**Goal:** Fix broken sort options, improve text search relevance, and optimize performance.

### 🛠️ Backend Fixes (Elasticsearch)
*   **Mapping Change:** `MarketplaceServiceIndexConfiguration.cs` & `MarketplaceServiceDocument.cs`
    *   Changed `Trust` and `ServiceStats` from `[Nested]` to `[Object]`.
    *   **Why?** `FunctionScore` queries cannot access nested fields for scoring (e.g., boosting by rating), causing text searches to return 0 results.
*   **Search Logic:** `MarketplaceSearchService.cs`
    *   **Rewrote `BuildQuery`:** Removed `Nested()` query wrappers (no longer needed for Object types).
    *   **Enhanced Text Search:** Implemented a hybrid query using `BestFields` (with fuzzy matching for typos) AND `PhrasePrefix` (for partial word/autocomplete behavior).
    *   **Fixed Sort:** Implemented missing sort cases: `newest`, `price_low`, `price_high` (previously fell back to relevance).
*   **Reindexing:** `MarketplaceController.cs`
    *   Updated `reindex` endpoint to **delete and recreate** the index before indexing. This is critical to apply the new Object mapping (you cannot change Nested->Object on an existing index).

### ⚡ Frontend Optimization
*   **Component:** `DiscoverTab.tsx`
    *   **Debouncing:** Added 350ms debounce to the search input to prevent hammering the server on every keystroke.
    *   **Sort Types:** Fixed TypeScript definitions to include all sort options (`newest`, etc.) and removed unsafe casts.

---

## ✅ Next Steps
To apply the backend search changes:
1. Ensure the API is running (`dotnet run`).
2. Trigger a reindex to update the Elasticsearch mapping and data:
   ```bash
   # Powershell / Terminal
   curl -X POST http://localhost:5010/api/marketplace/admin/reindex \
     -H "Authorization: Bearer <YOUR_TOKEN>" \
     -H "X-Tenant-Id: <YOUR_TENANT_ID>"
   ```
