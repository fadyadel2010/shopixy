# Phase 8.1 – Tenant Registration UX Design

## Document Metadata

| Property | Value |
|----------|-------|
| **Phase** | 8.1 |
| **Status** | Design Only |
| **Created** | 2025-12-26 |
| **Author** | UX Design Team |
| **Dependencies** | Phase 8.0 (Tenant Registration System Design) |
| **Implements** | New Merchant Onboarding Experience |
| **Quality Target** | Shopify / Stripe / Notion level |

---

## 1. UX Goals & Principles

### 1.1 Primary Goals

| Goal | Description |
|------|-------------|
| **Trust** | User feels confident their business is in safe hands |
| **Clarity** | Every step is obvious, no confusion about what happens next |
| **Speed** | Registration completes in under 60 seconds |
| **Safety** | No accidental public exposure of incomplete stores |
| **Accessibility** | Works for everyone, on any device |

### 1.2 Design Principles

| Principle | Application |
|-----------|-------------|
| **No Jargon** | "Store address" not "subdomain", "Your store" not "Tenant" |
| **Progressive Disclosure** | Show only what's needed now |
| **Immediate Feedback** | Validate as user types, not on submit |
| **Calm Confidence** | Professional tone, no pressure, no urgency tricks |
| **Mobile-First** | Designed for thumb-friendly interaction |
| **Forgiving** | Easy to correct mistakes, clear error recovery |

### 1.3 Emotional Journey

```
START                                                            END
  │                                                               │
  ▼                                                               ▼
Curious → Interested → Engaged → Confident → Excited → Accomplished
  │          │           │           │           │           │
  │          │           │           │           │           │
Landing   Form opens   Typing     All valid   Submitting   Welcome!
  page                  fields                             
```

---

## 2. Entry Points

### 2.1 Primary Entry Points

| Entry Point | Location | CTA Text | Notes |
|-------------|----------|----------|-------|
| **Marketing Homepage** | Hero section | "Start Free" | Most common path |
| **Navigation Header** | Top-right corner | "Get Started" | Persistent across pages |
| **Pricing Page** | After plan comparison | "Create Your Store" | After reading features |
| **Blog/Content** | End of articles | "Try It Free" | Content-driven conversion |

### 2.2 Entry Point Behavior

All entry points lead to the same registration page: `/register`

**URL Pattern:**
```
https://pos.myplatform.com/register
https://pos.myplatform.com/register?ref=homepage
https://pos.myplatform.com/register?plan=free
```

Query parameters are tracked for analytics but do NOT change UX.

### 2.3 Pre-Registration State

If user is already logged in:
- Redirect to POS Dashboard
- Do NOT show registration page

---

## 3. Registration Page Layout

### 3.1 Desktop Layout (≥768px)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                                                                             │
│    ┌─────────────────────────────────────────────────────────────────────┐  │
│    │  [Logo]                                          Already have an    │  │
│    │                                                  account? Sign in   │  │
│    └─────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
│    ┌────────────────────────────────┬──────────────────────────────────┐   │
│    │                                │                                   │   │
│    │      FORM PANEL                │       VALUE PANEL                │   │
│    │      (60% width)               │       (40% width)                │   │
│    │                                │                                   │   │
│    │  ┌──────────────────────────┐  │   ┌─────────────────────────┐    │   │
│    │  │                          │  │   │                         │    │   │
│    │  │  Create your store       │  │   │  ✓ Free to start        │    │   │
│    │  │                          │  │   │                         │    │   │
│    │  │  Start selling online    │  │   │  ✓ No credit card       │    │   │
│    │  │  in minutes.             │  │   │    required             │    │   │
│    │  │                          │  │   │                         │    │   │
│    │  └──────────────────────────┘  │   │  ✓ Set up in 60 seconds │    │   │
│    │                                │   │                         │    │   │
│    │  ┌──────────────────────────┐  │   │  ✓ Your store stays     │    │   │
│    │  │  Business name           │  │   │    private until you're │    │   │
│    │  │  ________________________│  │   │    ready                │    │   │
│    │  └──────────────────────────┘  │   │                         │    │   │
│    │                                │   └─────────────────────────┘    │   │
│    │  ┌──────────────────────────┐  │                                   │   │
│    │  │  Email                   │  │   ┌─────────────────────────┐    │   │
│    │  │  ________________________│  │   │                         │    │   │
│    │  └──────────────────────────┘  │   │  "We switched from      │    │   │
│    │                                │   │   spreadsheets and      │    │   │
│    │  ┌──────────────────────────┐  │   │   never looked back."   │    │   │
│    │  │  Password                │  │   │                         │    │   │
│    │  │  ________________________│  │   │   — Sarah, Shop Owner   │    │   │
│    │  └──────────────────────────┘  │   │                         │    │   │
│    │                                │   └─────────────────────────┘    │   │
│    │  ┌──────────────────────────┐  │                                   │   │
│    │  │  Store address           │  │                                   │   │
│    │  │  ____________.store.com  │  │                                   │   │
│    │  │  ✓ Available             │  │                                   │   │
│    │  └──────────────────────────┘  │                                   │   │
│    │                                │                                   │   │
│    │  ┌──────────────────────────┐  │                                   │   │
│    │  │                          │  │                                   │   │
│    │  │    Create your store     │  │                                   │   │
│    │  │                          │  │                                   │   │
│    │  └──────────────────────────┘  │                                   │   │
│    │                                │                                   │   │
│    │  By creating a store, you      │                                   │   │
│    │  agree to our Terms of         │                                   │   │
│    │  Service and Privacy Policy.   │                                   │   │
│    │                                │                                   │   │
│    └────────────────────────────────┴──────────────────────────────────┘   │
│                                                                             │
│    ┌─────────────────────────────────────────────────────────────────────┐  │
│    │  © 2024 Platform Name  •  Terms  •  Privacy  •  Help                │  │
│    └─────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 3.2 Mobile Layout (<768px)

```
┌─────────────────────────────────┐
│                                 │
│  [Logo]                         │
│                                 │
│  Already have an account?       │
│  Sign in                        │
│                                 │
├─────────────────────────────────┤
│                                 │
│  Create your store              │
│                                 │
│  Start selling online           │
│  in minutes.                    │
│                                 │
│  ┌───────────────────────────┐  │
│  │  Business name            │  │
│  │  _________________________│  │
│  └───────────────────────────┘  │
│                                 │
│  ┌───────────────────────────┐  │
│  │  Email                    │  │
│  │  _________________________│  │
│  └───────────────────────────┘  │
│                                 │
│  ┌───────────────────────────┐  │
│  │  Password                 │  │
│  │  ________________________ │  │
│  └───────────────────────────┘  │
│                                 │
│  ┌───────────────────────────┐  │
│  │  Store address            │  │
│  │  __________.store.com     │  │
│  │  ✓ Available              │  │
│  └───────────────────────────┘  │
│                                 │
│  ┌───────────────────────────┐  │
│  │                           │  │
│  │   Create your store       │  │
│  │                           │  │
│  └───────────────────────────┘  │
│                                 │
│  By creating a store, you       │
│  agree to our Terms and         │
│  Privacy Policy.                │
│                                 │
├─────────────────────────────────┤
│                                 │
│  ✓ Free to start                │
│  ✓ No credit card required      │
│  ✓ Your store stays private     │
│                                 │
└─────────────────────────────────┘
```

### 3.3 Visual Hierarchy

| Element | Size | Weight | Color |
|---------|------|--------|-------|
| Headline "Create your store" | 28px / 32px desktop | Bold | Primary text |
| Subheadline | 16px / 18px desktop | Regular | Muted text |
| Field labels | 14px | Medium | Primary text |
| Input text | 16px | Regular | Primary text |
| Helper text | 13px | Regular | Muted text |
| Error text | 13px | Medium | Error red |
| CTA Button | 16px | Semibold | White on primary |
| Legal text | 12px | Regular | Muted text |

---

## 4. Form Fields UX

### 4.1 Field Order

Fields are ordered by cognitive simplicity (easy → complex):

1. **Business Name** – Personal, easy to type
2. **Email** – Familiar pattern
3. **Password** – Routine
4. **Store Address** – Most complex, last (user is committed by now)

### 4.2 Business Name Field

```
┌─────────────────────────────────────────────────────────────┐
│  Business name                                              │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Acme Electronics                                     │  │
│  └───────────────────────────────────────────────────────┘  │
│  This is how your store will appear to customers.          │
└─────────────────────────────────────────────────────────────┘
```

| Property | Value |
|----------|-------|
| **Label** | "Business name" |
| **Placeholder** | None (blank) |
| **Helper text** | "This is how your store will appear to customers." |
| **Max length** | 100 characters |
| **Required** | Yes |
| **Validation** | Minimum 2 characters, maximum 100 |
| **Auto-suggest store address** | Yes (generates slug suggestion) |

**Behavior:**
- On blur, if store address field is empty, auto-generate suggestion
- Generate slug: "Acme Electronics" → "acme-electronics"
- User can override the suggestion

**Validation:**
| State | Display |
|-------|---------|
| Empty on blur | Show error: "Please enter your business name" |
| Too short (1 char) | Show error: "Business name is too short" |
| Valid | No indicator needed |

### 4.3 Email Field

```
┌─────────────────────────────────────────────────────────────┐
│  Email                                                      │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  sarah@acme.com                                       │  │
│  └───────────────────────────────────────────────────────┘  │
│  You'll use this to sign in.                                │
└─────────────────────────────────────────────────────────────┘
```

| Property | Value |
|----------|-------|
| **Label** | "Email" |
| **Placeholder** | None |
| **Helper text** | "You'll use this to sign in." |
| **Input type** | `email` |
| **Required** | Yes |
| **Autocomplete** | `email` |

**Validation:**
| State | Display |
|-------|---------|
| Empty on blur | Show error: "Please enter your email" |
| Invalid format | Show error: "Please enter a valid email address" |
| Already registered | Show error: "This email is already registered. Sign in instead?" (with link) |
| Valid | No indicator needed |

**Email existence check:**
- Check on blur (debounced 300ms after typing stops)
- If email exists, show inline error with "Sign in" link
- Do NOT prevent form submission (server will re-validate)

### 4.4 Password Field

```
┌─────────────────────────────────────────────────────────────┐
│  Password                                                   │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  ••••••••••••                              [👁 Show]  │  │
│  └───────────────────────────────────────────────────────┘  │
│  At least 8 characters                                      │
└─────────────────────────────────────────────────────────────┘
```

| Property | Value |
|----------|-------|
| **Label** | "Password" |
| **Placeholder** | None |
| **Helper text** | "At least 8 characters" |
| **Input type** | `password` (with toggle) |
| **Required** | Yes |
| **Autocomplete** | `new-password` |
| **Show/Hide toggle** | Yes (eye icon) |

**Validation:**
| State | Display |
|-------|---------|
| Empty on blur | Show error: "Please create a password" |
| Too short (<8 chars) | Show error: "Password must be at least 8 characters" |
| Valid | No indicator needed |

**Password requirements:**
- Minimum 8 characters
- No complexity requirements (modern security practice)
- No password strength meter (reduces friction)

**Show/Hide behavior:**
- Default: Hidden (password masked)
- Toggle text: "Show" / "Hide"
- Toggle is a button, not a checkbox
- Focus stays in password field after toggle

### 4.5 Store Address Field (Subdomain)

```
┌─────────────────────────────────────────────────────────────┐
│  Store address                                              │
│  ┌─────────────────────────────────┬─────────────────────┐  │
│  │  acme-electronics               │  .myplatform.com    │  │
│  └─────────────────────────────────┴─────────────────────┘  │
│  ✓ This address is available                                │
└─────────────────────────────────────────────────────────────┘
```

**This is the most complex field. See Section 5 for full details.**

| Property | Value |
|----------|-------|
| **Label** | "Store address" |
| **Input** | Left side (editable) |
| **Suffix** | Right side (read-only, e.g., ".myplatform.com") |
| **Helper text** | Dynamic based on availability |
| **Required** | Yes |

---

## 5. Subdomain UX (Critical Section)

### 5.1 Input Design

```
ANATOMY OF STORE ADDRESS FIELD
                                                    
┌─────────────────────────────────────────────────────────────────────┐
│                                                                     │
│  Store address                                        Field Label   │
│                                                                     │
│  ┌─────────────────────────────────┬─────────────────────────────┐  │
│  │                                 │                             │  │
│  │   acme-electronics              │   .myplatform.com           │  │
│  │                                 │                             │  │
│  │   ↑ Editable Input              │   ↑ Read-only Suffix        │  │
│  │     (user types here)           │     (always visible)        │  │
│  │                                 │                             │  │
│  └─────────────────────────────────┴─────────────────────────────┘  │
│                                                                     │
│  ✓ This address is available                      Availability      │
│     ↑ Status indicator (dynamic)                  Feedback          │
│                                                                     │
│  This will be your store's web address. You can   Helper Text       │
│  add a custom domain later.                       (context)         │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### 5.2 Auto-Generation from Business Name

When user types in Business Name field:

| Business Name | Generated Store Address |
|---------------|-------------------------|
| "Acme Electronics" | `acme-electronics` |
| "Sarah's Boutique" | `sarahs-boutique` |
| "Café 123" | `cafe-123` |
| "北京商店" | (no generation - manual entry required) |
| "My Store!!" | `my-store` |

**Rules:**
- Lowercase all letters
- Replace spaces with hyphens
- Remove special characters except hyphens
- Collapse multiple hyphens to single
- Trim leading/trailing hyphens
- If result is < 3 chars or invalid, leave field empty

**Timing:**
- Generate on business name blur
- Only if store address is currently empty or matches previous auto-generation
- User can always override

### 5.3 Validation Rules (Human-Friendly)

| Technical Rule | User-Facing Message |
|----------------|---------------------|
| Min 3 characters | "Store address must be at least 3 characters" |
| Max 63 characters | "Store address is too long" |
| Must start with letter or number | "Store address must start with a letter or number" |
| Must end with letter or number | "Store address must end with a letter or number" |
| Only lowercase letters, numbers, hyphens | "Only letters, numbers, and hyphens are allowed" |
| No consecutive hyphens | "Remove extra hyphens" |
| Reserved name | "This address is not available" |
| Already taken | "This address is already taken" |

### 5.4 Real-Time Availability Check

**Timing:**
- Start check after user stops typing (400ms debounce)
- Show loading state during check
- Cache results for current session

**States:**

```
CHECKING STATE
┌─────────────────────────────────┬─────────────────────────────┐
│   acme-electronics              │   .myplatform.com           │
└─────────────────────────────────┴─────────────────────────────┘
  ⟳ Checking availability...

AVAILABLE STATE
┌─────────────────────────────────┬─────────────────────────────┐
│   acme-electronics              │   .myplatform.com           │
└─────────────────────────────────┴─────────────────────────────┘
  ✓ This address is available
  
  Your store: acme-electronics.myplatform.com

TAKEN STATE
┌─────────────────────────────────┬─────────────────────────────┐
│   acme-store                    │   .myplatform.com           │
└─────────────────────────────────┴─────────────────────────────┘
  ✗ This address is already taken
  
  Try one of these:
  [acme-store-1] [acme-electronics] [acme-shop]

INVALID STATE
┌─────────────────────────────────┬─────────────────────────────┐
│   acme--store                   │   .myplatform.com           │
└─────────────────────────────────┴─────────────────────────────┘
  ✗ Remove extra hyphens

RESERVED STATE
┌─────────────────────────────────┬─────────────────────────────┐
│   admin                         │   .myplatform.com           │
└─────────────────────────────────┴─────────────────────────────┘
  ✗ This address is not available
  
  Try a more specific name for your store.
```

### 5.5 Suggestions UX

When address is taken, show up to 3 clickable suggestions:

```
Try one of these:
┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐
│  acme-store-1    │  │  acme-shop       │  │  acmestore       │
└──────────────────┘  └──────────────────┘  └──────────────────┘
```

**Click behavior:**
- Clicking suggestion fills input immediately
- Re-checks availability (should be instant from cache)
- Focus stays in field

**Suggestion generation:**
1. Original + number suffix (acme-store-1)
2. Original with "shop" instead of "store" 
3. Combined without hyphens
4. Business name variations

### 5.6 Helper Text Variations

| Context | Helper Text |
|---------|-------------|
| Field focused, empty | "This will be your store's web address" |
| Checking | "Checking availability..." |
| Available | "Your store: acme-electronics.myplatform.com" |
| Taken | "Try one of these:" + suggestions |
| Invalid format | Specific error message |
| Reserved | "Try a more specific name for your store" |

### 5.7 Visual Feedback Colors

| State | Border Color | Icon | Text Color |
|-------|--------------|------|------------|
| Neutral | Gray (default) | None | Muted |
| Focused | Primary blue | None | Primary |
| Checking | Primary blue | Spinner | Muted |
| Valid | Green | ✓ Checkmark | Green |
| Error | Red | ✗ X mark | Red |

---

## 6. Submit Flow

### 6.1 Submit Button States

```
DEFAULT STATE (All fields valid)
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│                    Create your store                            │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
Color: Primary blue, Cursor: Pointer

DISABLED STATE (Form incomplete or invalid)
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│                    Create your store                            │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
Color: Gray/muted, Cursor: Not-allowed

LOADING STATE (Submitting)
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│                ⟳  Creating your store...                        │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
Color: Primary blue (muted), Cursor: Wait, Disabled: true
```

### 6.2 Button Enable/Disable Logic

Button is **enabled** when ALL of:
- Business name: ≥ 2 characters
- Email: Valid format
- Password: ≥ 8 characters
- Store address: Available (green checkmark visible)

Button is **disabled** when ANY of:
- Any field is empty
- Any field has validation error
- Store address availability is unchecked or failed
- Form is currently submitting

### 6.3 Loading State

**Duration expectation:** 1-3 seconds

**UI During Loading:**
1. Button shows spinner + "Creating your store..."
2. All form fields become read-only (not hidden)
3. No other loading indicators needed
4. Keyboard: Enter key does nothing
5. Back button: Browser warn on navigation (unsaved form)

### 6.4 Success State

**Transition:**
- After successful creation, redirect to POS Dashboard
- Show brief success toast/message on dashboard

**Success Page (Dashboard first load):**
```
┌─────────────────────────────────────────────────────────────────────────────┐
│                                                                             │
│  [Logo]                                              [Profile] ▼            │
│                                                                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │                                                                       │  │
│  │  🎉 Welcome to your new store!                                        │  │
│  │                                                                       │  │
│  │  You're all set. Here's what to do next:                              │  │
│  │                                                                       │  │
│  │  [ ] Add your first product                                           │  │
│  │  [ ] Set up shipping                                                  │  │
│  │  [ ] Enable your online store when you're ready                       │  │
│  │                                                                       │  │
│  │                                        [Get Started →]                │  │
│  │                                                                       │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
│  Dashboard content...                                                       │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Key point:** Storefront is NOT yet enabled. User must explicitly enable it later.

### 6.5 Failure States

**Network error (no connection):**
```
┌─────────────────────────────────────────────────────────────────────────────┐
│                                                                             │
│  ⚠️  Unable to create your store                                            │
│                                                                             │
│  Please check your internet connection and try again.                       │
│                                                                             │
│                              [Try Again]                                    │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```
- Show as inline alert above submit button
- Button reverts to enabled state
- Form remains editable
- User can retry immediately

**Server error (unexpected):**
```
┌─────────────────────────────────────────────────────────────────────────────┐
│                                                                             │
│  ⚠️  Something went wrong                                                    │
│                                                                             │
│  We couldn't create your store right now. Please try again in a moment.    │
│                                                                             │
│                              [Try Again]                                    │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```
- Same behavior as network error

**Validation error (server-side):**
- Scroll to first error field
- Display inline error under that field
- Example: Email already registered, subdomain just taken

---

## 7. Error States Table

### 7.1 Field-Level Errors

| Field | Error Condition | User Message | Recovery Action |
|-------|----------------|--------------|-----------------|
| Business Name | Empty | "Please enter your business name" | Type name |
| Business Name | Too short | "Business name is too short" | Add more characters |
| Email | Empty | "Please enter your email" | Type email |
| Email | Invalid format | "Please enter a valid email address" | Fix format |
| Email | Already registered | "This email is already registered. [Sign in]" | Click "Sign in" link |
| Password | Empty | "Please create a password" | Type password |
| Password | Too short | "Password must be at least 8 characters" | Add more characters |
| Store Address | Empty | "Please choose a store address" | Type address |
| Store Address | Too short | "Store address must be at least 3 characters" | Add more characters |
| Store Address | Invalid format | Specific message (see Section 5.3) | Fix format |
| Store Address | Taken | "This address is already taken" | Choose suggestion or type new |
| Store Address | Reserved | "This address is not available" | Type different address |

### 7.2 Form-Level Errors

| Error Condition | User Message | Display Location | Recovery Action |
|----------------|--------------|------------------|-----------------|
| Network failure | "Unable to create your store. Check your connection." | Alert above button | Click "Try Again" |
| Server error | "Something went wrong. Please try again." | Alert above button | Click "Try Again" |
| Rate limited | "Too many attempts. Please wait a moment." | Alert above button | Wait, then retry |
| Unknown error | "We encountered an issue. Please try again." | Alert above button | Click "Try Again" |

### 7.3 Error Display Timing

| Error Type | When to Show | When to Hide |
|------------|--------------|--------------|
| Field empty | On blur (if user visited field) | When valid input entered |
| Field invalid | On blur or during typing | When valid input entered |
| Server validation | After submit attempt | When user changes field |
| Network/Server error | After failed submit | On next submit attempt |

---

## 8. Mobile UX Rules

### 8.1 Touch Targets

| Element | Minimum Size | Spacing |
|---------|--------------|---------|
| Input fields | 48px height | 16px between fields |
| Buttons | 48px height | 24px margin top |
| Links | 44px tap area | Inline is OK |
| Show/Hide toggle | 44x44px | Within field |

### 8.2 Keyboard Behavior

| Field | Keyboard Type | Next Action |
|-------|---------------|-------------|
| Business Name | Default text | Focus Email |
| Email | Email keyboard (`@` visible) | Focus Password |
| Password | Default with visible toggle | Focus Store Address |
| Store Address | URL keyboard (lowercase, hyphens) | Submit (if valid) |

### 8.3 Mobile-Specific Adjustments

| Desktop | Mobile |
|---------|--------|
| 2-column layout | Single column |
| Value panel beside form | Value points below form |
| Testimonial visible | Hidden or collapsed |
| Full legal text | Abbreviated legal |
| Inline suggestions | Stacked suggestions |

### 8.4 Viewport Behavior

- Form should never require horizontal scrolling
- On focus, input should scroll into view above keyboard
- Virtual keyboard should not cover active field
- Submit button should remain visible during form completion

### 8.5 Mobile Performance

- Form should be fully interactive within 2 seconds on 3G
- No blocking scripts before form loads
- Availability check timeout: 5 seconds (show manual message)

---

## 9. Accessibility Requirements

### 9.1 Keyboard Navigation

| Key | Action |
|-----|--------|
| Tab | Move to next interactive element |
| Shift+Tab | Move to previous interactive element |
| Enter | Submit form (when on button) / Toggle password visibility (when on toggle) |
| Space | Activate buttons, select suggestions |
| Escape | Close any popovers, clear current field focus |

**Tab order:**
1. Business Name
2. Email
3. Password
4. Show/Hide toggle
5. Store Address
6. Suggestions (if visible)
7. Create Store button
8. Sign In link
9. Terms link
10. Privacy link

### 9.2 Screen Reader Support

**Field labeling:**
```html
<!-- Proper label association -->
<label for="businessName">Business name</label>
<input id="businessName" aria-describedby="businessName-helper businessName-error" />
<span id="businessName-helper">This is how your store will appear to customers.</span>
<span id="businessName-error" role="alert">Please enter your business name</span>
```

**Live regions for dynamic content:**
- Availability status: `aria-live="polite"`
- Error messages: `role="alert"` (announces immediately)
- Loading states: `aria-busy="true"` on form

**Button states:**
```html
<!-- Loading state -->
<button aria-busy="true" aria-label="Creating your store, please wait">
  <span aria-hidden="true">⟳</span> Creating your store...
</button>

<!-- Disabled state -->
<button disabled aria-disabled="true">Create your store</button>
```

### 9.3 Focus Management

| Action | Focus Behavior |
|--------|----------------|
| Page load | Focus on first field (Business Name) |
| Tab through form | Natural order through all fields |
| Error on submit | Focus on first error field |
| Suggestion clicked | Stay on Store Address field |
| Show/Hide password | Stay on Password field |
| Success | Focus on dashboard welcome message |

### 9.4 Color & Contrast

| Element | Foreground | Background | Ratio (min) |
|---------|------------|------------|-------------|
| Regular text | #1f2937 | #ffffff | 7:1 (AAA) |
| Muted text | #6b7280 | #ffffff | 4.5:1 (AA) |
| Error text | #dc2626 | #ffffff | 4.5:1 (AA) |
| Success text | #16a34a | #ffffff | 4.5:1 (AA) |
| Placeholder | #9ca3af | #ffffff | 3:1 (minimum) |
| Button text | #ffffff | #2563eb | 4.5:1 (AA) |

### 9.5 Motion & Animation

- All animations respect `prefers-reduced-motion`
- Loading spinner: Can be replaced with static indicator
- Transitions: Maximum 200ms duration
- No auto-playing content

---

## 10. Security & Safety UX Notes

### 10.1 Password Safety

| Do | Don't |
|----|-------|
| ✓ Show/hide toggle for password | ✗ Mask password without toggle |
| ✓ Allow paste into password field | ✗ Block paste |
| ✓ Accept password managers (autofill) | ✗ Disable autofill |
| ✓ Minimum 8 characters only | ✗ Complex rules that frustrate users |

### 10.2 Privacy Considerations

| Data | Handling |
|------|----------|
| Email | Only checked for existence (leak-safe error message) |
| Password | Never logged, never sent in URL |
| Store address | Publicly checkable (expected) |
| IP Address | Rate limiting only, not stored with account |

### 10.3 Storefront Safety

**Key UX decision:** After registration, storefront is DISABLED by default.

```
┌───────────────────────────────────────────────────────────────┐
│                                                               │
│  ℹ️  Your online store is currently private                   │
│                                                               │
│  Customers cannot see your store yet. When you're ready,      │
│  go to Settings → Online Store to make it live.               │
│                                                               │
└───────────────────────────────────────────────────────────────┘
```

This prevents:
- Accidental exposure of empty/incomplete stores
- SEO issues with placeholder content
- Customer confusion

### 10.4 Terms & Consent

**Legal copy above submit button:**
> By creating a store, you agree to our [Terms of Service] and [Privacy Policy].

- No checkbox required (implied consent via action)
- Links open in new tab
- Text is always visible (not hidden in accordion)

---

## 11. UX Copy & Tone

### 11.1 Voice & Tone Guidelines

| Attribute | Description | Example |
|-----------|-------------|---------|
| **Confident** | We know our product helps | "Start selling online in minutes" |
| **Clear** | No ambiguity, no jargon | "Store address" not "Subdomain" |
| **Calm** | No pressure, no urgency | "Create your store" not "Start NOW!" |
| **Friendly** | Professional but warm | "You'll use this to sign in" |
| **Helpful** | Guides without lecturing | "At least 8 characters" |

### 11.2 Copy Inventory

| Element | Copy |
|---------|------|
| **Page Title** | "Create your store" |
| **Page Subtitle** | "Start selling online in minutes." |
| **Business Name Label** | "Business name" |
| **Business Name Helper** | "This is how your store will appear to customers." |
| **Email Label** | "Email" |
| **Email Helper** | "You'll use this to sign in." |
| **Password Label** | "Password" |
| **Password Helper** | "At least 8 characters" |
| **Store Address Label** | "Store address" |
| **Store Address Available** | "✓ This address is available" |
| **Store Address Taken** | "✗ This address is already taken" |
| **Store Address Preview** | "Your store: {slug}.myplatform.com" |
| **Submit Button** | "Create your store" |
| **Submit Loading** | "Creating your store..." |
| **Legal Text** | "By creating a store, you agree to our Terms of Service and Privacy Policy." |
| **Sign In Link** | "Already have an account? Sign in" |
| **Value Prop 1** | "✓ Free to start" |
| **Value Prop 2** | "✓ No credit card required" |
| **Value Prop 3** | "✓ Set up in 60 seconds" |
| **Value Prop 4** | "✓ Your store stays private until you're ready" |

### 11.3 Error Message Tone

| Bad ❌ | Good ✓ |
|--------|--------|
| "Error: Invalid email format" | "Please enter a valid email address" |
| "Field required" | "Please enter your business name" |
| "Slug unavailable" | "This address is already taken" |
| "Min length: 8" | "Password must be at least 8 characters" |
| "Request failed" | "Something went wrong. Please try again." |

---

## 12. Happy Path Flow Diagram

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          HAPPY PATH FLOW                                    │
└─────────────────────────────────────────────────────────────────────────────┘

     USER                                              SYSTEM
       │                                                  │
       │  1. Clicks "Start Free" on homepage              │
       │ ───────────────────────────────────────────────▶ │
       │                                                  │
       │  2. Registration page loads                      │
       │ ◀─────────────────────────────────────────────── │
       │     Focus on Business Name field                 │
       │                                                  │
       │  3. Types "Acme Electronics"                     │
       │     Tabs to next field                           │
       │ ───────────────────────────────────────────────▶ │
       │                                                  │
       │  4. Store Address auto-fills: "acme-electronics" │
       │     Starts availability check                    │
       │ ◀─────────────────────────────────────────────── │
       │                                                  │
       │  5. Types "sarah@acme.com"                       │
       │     Tabs to next field                           │
       │ ───────────────────────────────────────────────▶ │
       │                                                  │
       │  6. Email validated (format OK)                  │
       │     Email existence check (not registered)       │
       │ ◀─────────────────────────────────────────────── │
       │                                                  │
       │  7. Types password "MySecureP@ss123"             │
       │     Tabs to next field                           │
       │ ───────────────────────────────────────────────▶ │
       │                                                  │
       │  8. Password validated (≥8 chars)                │
       │ ◀─────────────────────────────────────────────── │
       │                                                  │
       │  9. Reviews store address                        │
       │     Sees "✓ This address is available"           │
       │     Sees preview: acme-electronics.myplatform.com│
       │ ◀─────────────────────────────────────────────── │
       │                                                  │
       │  10. Clicks "Create your store"                  │
       │ ───────────────────────────────────────────────▶ │
       │                                                  │
       │  11. Button shows "Creating your store..."       │
       │      Form becomes read-only                      │
       │ ◀─────────────────────────────────────────────── │
       │                                                  │
       │                            [Backend: Atomic      │
       │                             tenant bootstrap]    │
       │                                                  │
       │  12. Redirect to POS Dashboard                   │
       │      Welcome message displayed                   │
       │      Onboarding checklist visible                │
       │ ◀─────────────────────────────────────────────── │
       │                                                  │
       ▼                                                  ▼
    
    SUCCESS: User is now logged into their new store
             Storefront is DISABLED (safe default)
             Ready to start adding products
```

**Total time:** ~45 seconds for experienced user

---

## 13. Explicit Non-Goals

The following are **NOT included** in this UX design:

| Feature | Reason |
|---------|--------|
| Email verification flow | Phase 8.2+ |
| Password reset | Separate flow |
| Social login (Google, Apple) | Phase 8.3+ |
| Multi-step wizard | Contradicts "single page" constraint |
| Team member invitation | Post-registration feature |
| Pricing plan selection | No billing in Phase 8 |
| Custom domain setup | Post-registration feature |
| Storefront live preview | Safety concern (disabled by default) |
| Terms acceptance checkbox | Not required (implied consent) |
| CAPTCHA | Defer unless abuse detected |
| Progress indicator | Not needed (single page) |
| Save draft | Not needed (4 fields only) |
| Analytics dashboard | Post-registration |
| Tutorial video | Separate onboarding content |

---

## 14. Definition of Done

### 14.1 Design Complete Criteria

| Criterion | Status |
|-----------|--------|
| ✅ All fields documented with states | Complete |
| ✅ All error messages defined | Complete |
| ✅ Subdomain UX fully specified | Complete |
| ✅ Mobile layout designed | Complete |
| ✅ Accessibility requirements listed | Complete |
| ✅ Keyboard navigation defined | Complete |
| ✅ Loading/success/error states shown | Complete |
| ✅ Copy and tone documented | Complete |
| ✅ Happy path diagrammed | Complete |
| ✅ Non-goals explicit | Complete |

### 14.2 Ready for Implementation When

- [ ] Product Owner has reviewed and approved
- [ ] Design meets accessibility standards (WCAG 2.1 AA)
- [ ] All edge cases have defined UX
- [ ] No ambiguous copy or behavior
- [ ] Engineering team has no questions

### 14.3 Implementation Notes for Engineers

1. **Form validation library**: Use proven library (e.g., react-hook-form + zod)
2. **Debounce timing**: 300ms for email check, 400ms for subdomain check
3. **Accessibility testing**: Use axe-core or similar
4. **Loading states**: Minimum 300ms display to prevent flashing
5. **Error focus**: Use `scrollIntoView` with `behavior: 'smooth'`
6. **Password toggle**: Must work with password managers
7. **Mobile keyboard**: Set `inputMode` appropriately
8. **Autofill**: Do not disable browser autofill

---

## Appendix A: Visual Design Tokens

For design system implementation:

```css
/* Colors */
--color-primary: #2563eb;
--color-primary-hover: #1d4ed8;
--color-success: #16a34a;
--color-error: #dc2626;
--color-text: #1f2937;
--color-text-muted: #6b7280;
--color-border: #d1d5db;
--color-border-focus: #2563eb;
--color-background: #ffffff;
--color-background-muted: #f9fafb;

/* Typography */
--font-family: 'Inter', system-ui, sans-serif;
--font-size-xs: 12px;
--font-size-sm: 14px;
--font-size-base: 16px;
--font-size-lg: 18px;
--font-size-xl: 24px;
--font-size-2xl: 32px;

/* Spacing */
--space-1: 4px;
--space-2: 8px;
--space-3: 12px;
--space-4: 16px;
--space-5: 20px;
--space-6: 24px;
--space-8: 32px;

/* Borders */
--radius-sm: 4px;
--radius-md: 8px;
--radius-lg: 12px;

/* Shadows */
--shadow-sm: 0 1px 2px rgba(0,0,0,0.05);
--shadow-md: 0 4px 6px rgba(0,0,0,0.1);

/* Transitions */
--transition-fast: 150ms ease;
--transition-normal: 200ms ease;
```

## Appendix B: Related Documents

- `PHASE-8.0-TENANT-REGISTRATION-DESIGN.md` - Technical system design
- `MULTI-TENANCY-FOUNDATION.md` - Tenant architecture
- `LAUNCH-NOTES.md` - Current system state

---

*End of UX Design Document*
