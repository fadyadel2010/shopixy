# Phase 9.7: Visual Admin Storefront Builder - Implementation Summary

## 🎯 Overview

Phase 9.7 implements a production-grade Visual Admin Builder UI for the Headless, Multi-Tenant, MongoDB-first Storefront CMS. This drag-and-drop page builder allows tenants to visually edit their storefront pages without affecting live content until explicitly published.

## ⚠️ Critical Design Principle

**The builder NEVER affects live storefront data unless the user explicitly clicks Publish.**

- All edits modify **DRAFT ONLY**
- Draft is saved to PostgreSQL
- Live is served from MongoDB (via projection)
- Redis cache is only invalidated on Publish

---

## 📁 Project Structure

```
PointOfSale.Admin/
├── src/
│   ├── builder/
│   │   ├── sectionRegistry.ts      # Section Registry Pattern
│   │   └── index.ts
│   ├── components/
│   │   ├── builder/
│   │   │   ├── BuilderTopBar.tsx   # Top navigation with actions
│   │   │   ├── SectionPalette.tsx  # Left sidebar with draggable sections
│   │   │   ├── BuilderCanvas.tsx   # Main drag & drop canvas
│   │   │   ├── SettingsPanel.tsx   # Right sidebar with section editor
│   │   │   ├── PublishModal.tsx    # Publish confirmation dialog
│   │   │   ├── RollbackModal.tsx   # Version history & rollback UI
│   │   │   ├── PreviewModal.tsx    # Draft preview with device toggle
│   │   │   └── index.ts
│   │   └── sections/
│   │       ├── HeroBanner.tsx      # Hero Banner section
│   │       ├── ProductGrid.tsx     # Product Grid section
│   │       ├── CategoryGrid.tsx    # Category Grid section
│   │       ├── TextBlock.tsx       # Text Block section
│   │       ├── ImageBlock.tsx      # Image Block section
│   │       ├── NewsletterSignup.tsx # Newsletter section
│   │       ├── Testimonials.tsx    # Testimonials section
│   │       ├── FeaturedProducts.tsx # Featured Products section
│   │       └── index.ts
│   ├── pages/
│   │   ├── VisualBuilderPage.tsx   # Main builder page
│   │   └── index.ts
│   ├── services/
│   │   ├── api.ts                  # Axios client with interceptors
│   │   ├── storeConfigService.ts   # Store config API service
│   │   └── index.ts
│   ├── store/
│   │   ├── builderStore.ts         # Zustand store with undo/redo
│   │   └── index.ts
│   ├── types/
│   │   ├── builder.ts              # TypeScript types
│   │   └── index.ts
│   ├── styles/
│   │   └── index.css               # Tailwind + custom styles
│   ├── App.tsx                     # Main App with routing
│   └── main.tsx                    # Entry point
├── package.json
├── tsconfig.json
├── vite.config.ts
├── tailwind.config.js
├── postcss.config.js
└── README.md
```

---

## 🧱 Section Registry Pattern

The builder uses a centralized Section Registry for extensibility:

```typescript
type SectionDefinition = {
  type: string;
  displayName: string;
  description: string;
  icon: ReactNode;
  defaultSettings: Record<string, unknown>;
  editor: ComponentType<SectionEditorProps>;
  rendererPreview: ComponentType<SectionRendererProps>;
  category: SectionCategory;
};
```

### Available Sections

| Section | Type Key | Category |
|---------|----------|----------|
| Hero Banner | `hero-banner` | Layout |
| Product Grid | `product-grid` | Products |
| Category Grid | `category-grid` | Products |
| Text Block | `text-block` | Content |
| Image Block | `image-block` | Content |
| Newsletter Signup | `newsletter-signup` | Marketing |
| Testimonials | `testimonials` | Social |
| Featured Products | `featured-products` | Products |

---

## 🎨 UI Layout

```
┌────────────────────────────────────────────┐
│ Top Bar                                    │
│ [Preview] [Publish] [Rollback] [Exit]     │
├──────────────┬─────────────────────────────┤
│ Sections     │ Canvas                      │
│ Palette      │ (Live Drag & Drop Area)     │
│ (260px)      │                             │
├──────────────┴─────────────────────────────┤
│ Settings Panel (320px)                     │
│ Context-aware Section Editor               │
└────────────────────────────────────────────┘
```

---

## 🔄 Data Flow

### Draft Editing (No Live Impact)

1. User drags section onto canvas
2. Builder state updates locally
3. Debounced save triggers after 1.5s
4. `PUT /api/admin/store-config/pages/{pageId}` called
5. PostgreSQL updated (DRAFT only)
6. **No events emitted, no MongoDB writes, no Redis invalidation**

### Publish Flow (Affects Live)

1. User clicks "Publish"
2. Confirmation modal shown
3. User confirms
4. `POST /api/admin/store-config/pages/{pageId}/publish` called
5. Backend:
   - Copies DRAFT → LIVE
   - Creates version snapshot
   - Emits `StoreConfigPublishedEvent`
   - MongoDB projection updates
   - Redis cache invalidated
6. Live storefront now shows new content

### Rollback Flow

1. User clicks "Rollback"
2. Version history shown
3. User selects target version
4. Confirmation with reason required
5. `POST /api/admin/store-config/rollback/{version}` called
6. State restored from snapshot
7. Event emitted, caches invalidated

---

## 🔧 API Endpoints Used

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/admin/store-config` | GET | Get store configuration |
| `/api/admin/store-config/pages/{pageId}` | PUT | Update page layout (DRAFT) |
| `/api/admin/store-config/pages/{pageId}/publish` | POST | Publish page to LIVE |
| `/api/admin/store-config/preview` | GET | Get preview configuration |
| `/api/admin/store-config/rollback/{version}` | POST | Rollback to version |
| `/api/admin/store-config/themes/presets` | GET | Get available theme presets |

---

## 🛡️ Security Considerations

- ✅ Admin UI is authenticated (JWT/session)
- ✅ Tenant context from X-Tenant-Id header
- ✅ Never allow cross-tenant preview
- ✅ Publish requires explicit confirmation
- ✅ Publish blocked if email not verified
- ✅ Publish blocked if storefront disabled

---

## ⚡ Performance Features

- **Local state first** - Fast editing without network blocking
- **Debounced saves** - 1.5s delay prevents excessive API calls
- **Undo/Redo** - Local history (last 50 states)
- **Optimistic UI** - Actions reflect immediately

---

## ⌨️ Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+S` | Save draft |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` | Redo |

---

## 📦 Dependencies

| Package | Purpose |
|---------|---------|
| `react` | UI framework |
| `react-router-dom` | Routing |
| `zustand` | State management |
| `@dnd-kit/core` | Drag and drop |
| `@dnd-kit/sortable` | Sortable lists |
| `lucide-react` | Icons |
| `axios` | API client |
| `tailwindcss` | Styling |

---

## ✅ Definition of Done Checklist

- [x] Tenant can visually build Home/Product/Category pages
- [x] Draft changes never affect live storefront
- [x] Preview shows draft without MongoDB/Redis
- [x] Publish creates a new live version
- [x] Rollback restores previous version safely
- [x] Builder is extensible for future sections
- [x] No tenant isolation violations
- [x] Clean TypeScript build (no errors)

---

## 🚀 Getting Started

### Prerequisites

- Node.js 18+
- Backend API running on `http://localhost:5115`

### Installation

```bash
cd PointOfSale.Admin
npm install
```

### Development

```bash
npm run dev
```

Admin portal available at `http://localhost:5174`

### Production Build

```bash
npm run build
```

---

## 📝 Future Enhancements (Out of Scope)

- Multi-user collaboration
- Real-time co-editing
- A/B testing
- Analytics dashboards
- Custom section upload
- Section templates gallery
