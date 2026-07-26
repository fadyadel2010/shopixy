# Storefront Publishing & Preview Issue Resolution Summary

## 1. Issue Description
Users reported two critical failures in the Storefront Builder:
1.  **Preview Failure**: Clicking "Preview" resulted in a 400 Bad Request error.
2.  **Publish Failure**: Changes made and published in the Admin Theme Editor were **not persisting** to the live Storefront application, despite the "Success" toast notification.

## 2. Root Cause Analysis

### A. Frontend Preview URL Mismatch
*   **Problem**: The Admin app was attempting to load the preview from `/api/storefront/preview`, which is a non-existent API endpoint.
*   **Impact**: The preview iframe showed an error instead of the storefront.

### B. Event Type Mismatch (The "Silent Failure")
*   **Problem**: When a user clicks "Publish", the backend emits a `StoreConfigPublishedEvent`. However, the background worker (`StorefrontConfigProjectionConsumer`) was configured to listen **only** for `StoreConfigUpdatedEvent`.
*   **Impact**: The "Publish" event was ignored by the consumer. Use SQL database was updated, but the MongoDB (Storefront Read Model) was never updated. This caused the live storefront to continue serving old data.

### C. Message Broker Topology Stale Bindings
*   **Problem**: Even after updating the code to listen for `StoreConfigPublishedEvent`, the existing RabbitMQ queue (`storefront-config-projection`) retained old bindings and did not automatically route the new event type.
*   **Impact**: The consumer still didn't receive the events even with the correct code.

## 3. Implementation of Fixes

### Frontend Fixes
1.  **`StorefrontBuilderPage.jsx`**: Updated the preview URL to point directly to the Storefront application (`http://localhost:5174?tenant=...`) instead of an API call.
2.  **`ThemeEditorPage.jsx`**: Fixed `showToast` calls (replaced generic method with `showSuccess`/`showError`).
3.  **`storeConfigService.js`**: Corrected API routes to match the backend (e.g., changed `themes/publish` to `theme/publish`).

### Backend Fixes
1.  **`StorefrontConfigProjectionConsumer.cs`**:
    *   Updated to implement `IConsumer<StoreConfigPublishedEvent>`, `IConsumer<StoreConfigCreatedEvent>`, and `IConsumer<StoreConfigRolledBackEvent>`.
    *   Refactored `Consume` logic to a shared `ProjectStoreConfigAsync` method to handle all event types uniformly.
    *   Added a `StorefrontConfigProjectionConsumerDefinition` class to explicitly configure the queue.
2.  **`Program.cs`**:
    *   Registered the consumer with its explicit definition: `x.AddConsumer<StorefrontConfigProjectionConsumer, StorefrontConfigProjectionConsumerDefinition>()`.
    *   This forces MassTransit to use a **new queue name (`storefront-config-projection-v2`)**, which resets the RabbitMQ topology and ensures all event bindings are fresh and correct.

## 4. Verification Steps

To confirm the fix is working:
1.  **Start the API**: Ensure the latest build is running.
2.  **Check Logs**: Look for the startup log: `Configured endpoint storefront-config-projection-v2`.
3.  **Perform Action**: In Admin -> Storefront, change a text value and click **Publish**.
4.  **Verify**:
    *   **Logs**: You should see `Received StoreConfigPublishedEvent` and `StorefrontConfig projected for Tenant ...`.
    *   **Storefront**: Refresh `http://localhost:5174?tenant=YOUR_TENANT_ID`. The changes should appear immediately.
