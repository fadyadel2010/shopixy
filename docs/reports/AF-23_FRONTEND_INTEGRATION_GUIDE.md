# AF-23: Frontend Integration Guide

## 🎨 UX Implementation Patterns

This guide shows how to integrate the AF-23 Readiness Gate into your frontend application.

---

## 📡 API Endpoint

```typescript
GET /api/admin/affiliate/readiness
Authorization: Bearer {admin-jwt-token}

Response:
{
  isReady: boolean;
  storefrontLive: boolean;
  hasPaymentMethod: boolean;
  hasShippingMethod: boolean;
  missingRequirements: string[];
}
```

---

## 🔧 TypeScript Types

```typescript
// types/affiliate.ts
export interface AffiliateReadinessResult {
  isReady: boolean;
  storefrontLive: boolean;
  hasPaymentMethod: boolean;
  hasShippingMethod: boolean;
  missingRequirements: string[];
}

export interface AffiliateReadinessCheck {
  loading: boolean;
  data: AffiliateReadinessResult | null;
  error: Error | null;
}
```

---

## 🪝 React Hook (Recommended)

```typescript
// hooks/useAffiliateReadiness.ts
import { useState, useEffect } from 'react';
import { AffiliateReadinessResult } from '@/types/affiliate';

export function useAffiliateReadiness() {
  const [loading, setLoading] = useState(true);
  const [data, setData] = useState<AffiliateReadinessResult | null>(null);
  const [error, setError] = useState<Error | null>(null);

  useEffect(() => {
    async function checkReadiness() {
      try {
        setLoading(true);
        const response = await fetch('/api/admin/affiliate/readiness', {
          headers: {
            'Authorization': `Bearer ${getAuthToken()}`,
          },
        });

        if (!response.ok) {
          throw new Error('Failed to check affiliate readiness');
        }

        const result = await response.json();
        setData(result);
      } catch (err) {
        setError(err as Error);
      } finally {
        setLoading(false);
      }
    }

    checkReadiness();
  }, []);

  return { loading, data, error };
}
```

---

## 🎯 Pattern 1: Product Edit Modal

### Component Structure
```
ProductEditModal
├── ProductForm
│   ├── BasicInfo
│   ├── Pricing
│   └── AffiliateSection ← Readiness check here
└── Actions
```

### Implementation

```tsx
// components/ProductEditModal/AffiliateSection.tsx
import { useAffiliateReadiness } from '@/hooks/useAffiliateReadiness';
import { Alert, Switch, Button } from '@/components/ui';

interface AffiliateSectionProps {
  productId: string;
  isAffiliateEnabled: boolean;
  commissionRate: number | null;
  onToggle: (enabled: boolean) => void;
}

export function AffiliateSection({
  productId,
  isAffiliateEnabled,
  commissionRate,
  onToggle,
}: AffiliateSectionProps) {
  const { loading, data: readiness } = useAffiliateReadiness();

  if (loading) {
    return <div>Checking affiliate availability...</div>;
  }

  // ─── NOT READY: Show Warning Card ───────────────────────────────────
  if (!readiness?.isReady) {
    return (
      <div className="space-y-4">
        <Alert variant="warning">
          <AlertTitle>Affiliate Marketing Not Available</AlertTitle>
          <AlertDescription>
            Complete the following setup to enable affiliate marketing:
          </AlertDescription>
        </Alert>

        <div className="space-y-2">
          <ChecklistItem
            checked={readiness?.storefrontLive ?? false}
            label="Storefront must be live"
            action={
              !readiness?.storefrontLive && (
                <Button
                  variant="link"
                  onClick={() => navigate('/admin/storefront/settings')}
                >
                  Go to Storefront Settings
                </Button>
              )
            }
          />

          <ChecklistItem
            checked={readiness?.hasPaymentMethod ?? false}
            label="At least one active payment method"
            action={
              !readiness?.hasPaymentMethod && (
                <Button
                  variant="link"
                  onClick={() => navigate('/admin/settings/payment-methods')}
                >
                  Configure Payment Methods
                </Button>
              )
            }
          />

          <ChecklistItem
            checked={readiness?.hasShippingMethod ?? false}
            label="At least one active shipping method"
            action={
              !readiness?.hasShippingMethod && (
                <Button
                  variant="link"
                  onClick={() => navigate('/admin/settings/shipping-methods')}
                >
                  Configure Shipping Methods
                </Button>
              )
            }
          />
        </div>
      </div>
    );
  }

  // ─── READY: Show Affiliate Toggle ───────────────────────────────────
  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h3 className="text-lg font-medium">Affiliate Marketing</h3>
          <p className="text-sm text-gray-500">
            Allow affiliates to promote this product and earn commission
          </p>
        </div>
        <Switch
          checked={isAffiliateEnabled}
          onCheckedChange={onToggle}
        />
      </div>

      {isAffiliateEnabled && (
        <div className="space-y-2">
          <label>Commission Rate (%)</label>
          <input
            type="number"
            min="0.01"
            max="50"
            step="0.01"
            value={commissionRate ?? 10}
            onChange={(e) => onCommissionChange(parseFloat(e.target.value))}
          />
        </div>
      )}
    </div>
  );
}

// Helper Component
function ChecklistItem({
  checked,
  label,
  action,
}: {
  checked: boolean;
  label: string;
  action?: React.ReactNode;
}) {
  return (
    <div className="flex items-center justify-between p-3 border rounded">
      <div className="flex items-center gap-2">
        {checked ? (
          <CheckCircle className="text-green-500" />
        ) : (
          <XCircle className="text-red-500" />
        )}
        <span className={checked ? 'text-gray-900' : 'text-gray-500'}>
          {label}
        </span>
      </div>
      {action}
    </div>
  );
}
```

---

## 🎯 Pattern 2: Affiliate Management Page

### Component Structure
```
AffiliateManagementPage
├── Header
├── ReadinessGate ← Check here
└── AffiliateList (only if ready)
```

### Implementation

```tsx
// pages/admin/affiliates/index.tsx
import { useAffiliateReadiness } from '@/hooks/useAffiliateReadiness';
import { Alert, Button, Card } from '@/components/ui';

export function AffiliateManagementPage() {
  const { loading, data: readiness } = useAffiliateReadiness();

  if (loading) {
    return <LoadingSpinner />;
  }

  // ─── NOT READY: Show Setup Guide ────────────────────────────────────
  if (!readiness?.isReady) {
    return (
      <div className="max-w-2xl mx-auto py-12">
        <Card className="p-8">
          <div className="text-center mb-6">
            <h1 className="text-2xl font-bold mb-2">
              Affiliate System Not Ready
            </h1>
            <p className="text-gray-600">
              Complete the setup below to start using the affiliate system
            </p>
          </div>

          <div className="space-y-4">
            <SetupStep
              number={1}
              title="Enable Storefront"
              description="Your storefront must be live for affiliates to promote products"
              completed={readiness?.storefrontLive ?? false}
              action={
                <Button onClick={() => navigate('/admin/storefront/settings')}>
                  Go to Storefront Settings
                </Button>
              }
            />

            <SetupStep
              number={2}
              title="Configure Payment Methods"
              description="Add at least one payment method for customer transactions"
              completed={readiness?.hasPaymentMethod ?? false}
              action={
                <Button onClick={() => navigate('/admin/settings/payment-methods')}>
                  Configure Payment Methods
                </Button>
              }
            />

            <SetupStep
              number={3}
              title="Configure Shipping Methods"
              description="Add at least one shipping method for order fulfillment"
              completed={readiness?.hasShippingMethod ?? false}
              action={
                <Button onClick={() => navigate('/admin/settings/shipping-methods')}>
                  Configure Shipping Methods
                </Button>
              }
            />
          </div>

          <div className="mt-8 p-4 bg-blue-50 rounded">
            <p className="text-sm text-blue-800">
              <strong>Why these requirements?</strong> The affiliate system
              requires a fully operational storefront to ensure affiliates can
              successfully promote products and customers can complete purchases.
            </p>
          </div>
        </Card>
      </div>
    );
  }

  // ─── READY: Show Affiliate Management ───────────────────────────────
  return (
    <div>
      <PageHeader
        title="Affiliate Management"
        description="Manage your affiliate partners and track their performance"
      />
      <AffiliateList />
    </div>
  );
}

// Helper Component
function SetupStep({
  number,
  title,
  description,
  completed,
  action,
}: {
  number: number;
  title: string;
  description: string;
  completed: boolean;
  action: React.ReactNode;
}) {
  return (
    <div className="flex gap-4 p-4 border rounded">
      <div className="flex-shrink-0">
        {completed ? (
          <div className="w-8 h-8 rounded-full bg-green-500 flex items-center justify-center">
            <Check className="text-white" />
          </div>
        ) : (
          <div className="w-8 h-8 rounded-full bg-gray-200 flex items-center justify-center">
            <span className="text-gray-600 font-medium">{number}</span>
          </div>
        )}
      </div>
      <div className="flex-1">
        <h3 className="font-medium mb-1">{title}</h3>
        <p className="text-sm text-gray-600 mb-3">{description}</p>
        {!completed && action}
      </div>
    </div>
  );
}
```

---

## 🎯 Pattern 3: Inline Warning Banner

### Use Case
Show a dismissible banner at the top of admin pages when affiliate is not ready.

```tsx
// components/AffiliateReadinessBanner.tsx
import { useAffiliateReadiness } from '@/hooks/useAffiliateReadiness';
import { Alert, Button } from '@/components/ui';
import { useState } from 'react';

export function AffiliateReadinessBanner() {
  const { data: readiness } = useAffiliateReadiness();
  const [dismissed, setDismissed] = useState(false);

  if (dismissed || readiness?.isReady) {
    return null;
  }

  return (
    <Alert variant="warning" className="mb-4">
      <div className="flex items-center justify-between">
        <div>
          <AlertTitle>Affiliate System Incomplete</AlertTitle>
          <AlertDescription>
            {readiness?.missingRequirements.join(', ')}
          </AlertDescription>
        </div>
        <div className="flex gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => navigate('/admin/affiliates/setup')}
          >
            Complete Setup
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => setDismissed(true)}
          >
            Dismiss
          </Button>
        </div>
      </div>
    </Alert>
  );
}

// Usage in layout
export function AdminLayout({ children }) {
  return (
    <div>
      <AdminHeader />
      <AffiliateReadinessBanner />
      <main>{children}</main>
    </div>
  );
}
```

---

## 🎯 Pattern 4: Navigation Menu Badge

### Use Case
Show a badge on the "Affiliates" menu item when setup is incomplete.

```tsx
// components/AdminSidebar.tsx
import { useAffiliateReadiness } from '@/hooks/useAffiliateReadiness';

export function AdminSidebar() {
  const { data: readiness } = useAffiliateReadiness();

  return (
    <nav>
      <NavItem href="/admin/dashboard" icon={Home}>
        Dashboard
      </NavItem>
      <NavItem href="/admin/products" icon={Package}>
        Products
      </NavItem>
      <NavItem href="/admin/affiliates" icon={Users}>
        Affiliates
        {!readiness?.isReady && (
          <Badge variant="warning" className="ml-2">
            Setup Required
          </Badge>
        )}
      </NavItem>
    </nav>
  );
}
```

---

## 🔄 Error Handling

### Handle API Errors

```typescript
// hooks/useAffiliateReadiness.ts (enhanced)
export function useAffiliateReadiness() {
  const [loading, setLoading] = useState(true);
  const [data, setData] = useState<AffiliateReadinessResult | null>(null);
  const [error, setError] = useState<Error | null>(null);

  useEffect(() => {
    async function checkReadiness() {
      try {
        setLoading(true);
        const response = await fetch('/api/admin/affiliate/readiness', {
          headers: {
            'Authorization': `Bearer ${getAuthToken()}`,
          },
        });

        if (response.status === 401) {
          // Unauthorized - redirect to login
          window.location.href = '/login';
          return;
        }

        if (response.status === 403) {
          // Forbidden - user doesn't have admin role
          throw new Error('You do not have permission to access affiliate features');
        }

        if (!response.ok) {
          throw new Error('Failed to check affiliate readiness');
        }

        const result = await response.json();
        setData(result);
      } catch (err) {
        setError(err as Error);
        console.error('[AF-23] Readiness check failed:', err);
      } finally {
        setLoading(false);
      }
    }

    checkReadiness();
  }, []);

  return { loading, data, error };
}
```

### Display Errors

```tsx
export function AffiliateSection() {
  const { loading, data: readiness, error } = useAffiliateReadiness();

  if (loading) {
    return <Skeleton />;
  }

  if (error) {
    return (
      <Alert variant="error">
        <AlertTitle>Failed to Check Affiliate Availability</AlertTitle>
        <AlertDescription>{error.message}</AlertDescription>
      </Alert>
    );
  }

  // ... rest of component
}
```

---

## 🔄 Refresh on Changes

### Auto-refresh after setup changes

```typescript
// hooks/useAffiliateReadiness.ts (with refresh)
export function useAffiliateReadiness() {
  const [loading, setLoading] = useState(true);
  const [data, setData] = useState<AffiliateReadinessResult | null>(null);
  const [error, setError] = useState<Error | null>(null);

  const refresh = useCallback(async () => {
    // ... same fetch logic
  }, []);

  useEffect(() => {
    refresh();
  }, [refresh]);

  return { loading, data, error, refresh };
}

// Usage after enabling payment method
function PaymentMethodForm() {
  const { refresh: refreshAffiliateReadiness } = useAffiliateReadiness();

  async function handleSave() {
    await savePaymentMethod();
    // Refresh affiliate readiness after enabling payment
    await refreshAffiliateReadiness();
  }
}
```

---

## 📊 Analytics Integration

### Track readiness checks

```typescript
// hooks/useAffiliateReadiness.ts (with analytics)
export function useAffiliateReadiness() {
  // ... existing code

  useEffect(() => {
    async function checkReadiness() {
      try {
        // ... fetch logic
        const result = await response.json();
        setData(result);

        // Track readiness status
        analytics.track('Affiliate Readiness Checked', {
          isReady: result.isReady,
          missingCount: result.missingRequirements.length,
          missing: result.missingRequirements,
        });
      } catch (err) {
        // ... error handling
      }
    }

    checkReadiness();
  }, []);

  return { loading, data, error };
}
```

---

## ✅ Implementation Checklist

### Product Edit Modal
- [ ] Add `useAffiliateReadiness` hook
- [ ] Show warning card when not ready
- [ ] Disable affiliate toggle when not ready
- [ ] Show checklist with action buttons
- [ ] Test all missing requirement combinations

### Affiliate Management Page
- [ ] Add readiness gate at page level
- [ ] Show setup guide when not ready
- [ ] Hide affiliate list when not ready
- [ ] Add navigation buttons to setup pages
- [ ] Test page access when not ready

### Navigation
- [ ] Add badge to affiliate menu item
- [ ] Show "Setup Required" when not ready
- [ ] Remove badge when ready

### Error Handling
- [ ] Handle 401 (redirect to login)
- [ ] Handle 403 (show permission error)
- [ ] Handle network errors
- [ ] Show user-friendly error messages

### Analytics
- [ ] Track readiness checks
- [ ] Track setup completion
- [ ] Track blocked operations

---

## 🎨 Design Tokens

### Colors
```css
--affiliate-warning-bg: #FEF3C7;
--affiliate-warning-border: #F59E0B;
--affiliate-warning-text: #92400E;

--affiliate-success-bg: #D1FAE5;
--affiliate-success-border: #10B981;
--affiliate-success-text: #065F46;

--affiliate-error-bg: #FEE2E2;
--affiliate-error-border: #EF4444;
--affiliate-error-text: #991B1B;
```

### Icons
- ✅ Check: Success state
- ❌ X: Missing requirement
- ⚠️ Warning: Not ready state
- 🔒 Lock: Blocked operation

---

**Frontend Integration Status:** Ready for Implementation  
**UX Patterns:** Documented  
**Error Handling:** Comprehensive
