# Phase 10.1: Multi-Language Storefront Implementation

## 🎯 Objective

Implement full multi-language support (Arabic / English) across the entire Storefront that is:
- **Search-aware** (Elasticsearch-first)
- **SEO-ready**
- **Tenant-safe**
- **Backward-compatible**
- **CQRS-compliant**

---

## ✅ Implementation Progress

### Phase 10.1.1: Core Language Infrastructure (Frontend) - **COMPLETED**

- [x] Created `src/i18n/` directory structure
- [x] Implemented Zustand language store with persistence
- [x] Created EN and AR translation files
- [x] Implemented `useTranslation()` hook
- [x] Created `LanguageSwitcher` component
- [x] Added comprehensive RTL CSS support
- [x] Integrated language initialization in `main.tsx`
- [x] Added LanguageSwitcher to Header

### Phase 10.1.2: Elasticsearch Multi-Language Search - **COMPLETED**

- [x] Updated `IUnifiedSearchIndexService` interface with language parameter
- [x] Updated `UnifiedSearchIndexService` with language-aware field boosting
- [x] Arabic queries boost `nameAr^4`, English queries boost `name^4`
- [x] Fallback search continues to work

### Phase 10.1.3: Backend API Language Support - **COMPLETED**

- [x] Added `lang` query parameter to `StorefrontSearchController`
- [x] Language normalization (ar/arabic → ar, default → en)
- [x] Logging includes language context

### Phase 10.1.4: Frontend Language Integration - **COMPLETED**

- [x] Updated `searchApi.ts` with lang parameter
- [x] Updated `SearchAutocomplete` to use translations
- [x] All hardcoded strings replaced with i18n keys

### Phase 10.1.5: UI Component Updates - **COMPLETED**

- [x] Updated `ProductCardDefault` with localized name display
- [x] Updated `ProductCardCompact` with localized name display
- [x] Updated `ProductCardDetailed` with localized name and alternate language display
- [x] Updated `Footer` with translations and localized navigation labels
- [x] Updated `CartPage` with all translations
- [x] Created `SeoMeta` component for language-specific meta tags
- [x] Created `HrefLangLinks` component for SEO
- [x] Added new cart translation keys to EN/AR JSON files

### Phase 10.1.6: Language-Based URL Routing - **COMPLETED**

- [x] Updated `router.tsx` with language-prefixed routes (`/ar/...`, `/en/...`)
- [x] Created `LanguageWrapper` component for URL language detection
- [x] Added `getLocalizedUrl()` helper function
- [x] Added `switchUrlLanguage()` helper function
- [x] Both prefixed and non-prefixed routes work correctly

### Phase 10.1.7: Language-Aware Sitemap - **COMPLETED**

- [x] Created `src/utils/sitemap.ts` with sitemap generation utilities
- [x] Supports `hreflang` tags for multi-language SEO
- [x] Static pages configuration
- [x] Dynamic content entry generators (products, categories, CMS pages)
- [x] Sitemap index generation for large sitemaps

### Phase 10.1.8: Additional Page Updates - **COMPLETED**

- [x] Updated `CheckoutPage` with translations and RTL-aware breadcrumbs
- [x] Updated `SearchPage` with all translations (replacing isRtl checks)
- [x] Updated `ProductDetailsPage` with localized name display and translations
- [x] Added `page` translation key to common section

---

## 📋 Implementation Phases

### Phase 10.1.1: Core Language Infrastructure (Frontend)

1. **Create i18n System**
   - Create `src/i18n/` directory with translation files
   - Create language context/store with Zustand
   - Implement language detection (URL, cookie, header)
   - Create `useTranslation()` hook

2. **Create Translation Files**
   - `src/i18n/locales/en.json` - English translations
   - `src/i18n/locales/ar.json` - Arabic translations

3. **Language Switcher Component**
   - Create `LanguageSwitcher.tsx` component
   - Add to Header component

4. **RTL Support**
   - Update `index.html` with dynamic `dir` and `lang` attributes
   - Add RTL CSS utilities and overrides

### Phase 10.1.2: Elasticsearch Multi-Language Search

1. **Update Search Document Models**
   - `ProductSearchDocument.cs` - Add `SearchText.En`, `SearchText.Ar` fields
   - `CategorySearchDocument.cs` - Add `SearchText.En`, `SearchText.Ar` fields
   - `CmsPageSearchDocument.cs` - Add `SearchText.En`, `SearchText.Ar` fields

2. **Update Elasticsearch Mappings**
   - Create multi-field mappings with language-specific analyzers
   - English analyzer for `searchText.en`
   - Arabic analyzer for `searchText.ar`

3. **Update Search Service**
   - Accept `language` parameter
   - Query appropriate language field based on context
   - Fallback to English if Arabic not available

4. **Update Consumers (Indexing)**
   - Build language-specific search text during indexing
   - Keep English and Arabic content separate

### Phase 10.1.3: Backend API Language Support

1. **Add Accept-Language Header Support**
   - Parse `Accept-Language` header in controllers
   - Inject language context

2. **Update Search Controller**
   - Accept `lang` query parameter
   - Pass language to search service

3. **Update CMS Pages API**
   - Return language-aware content

### Phase 10.1.4: Frontend Language Integration

1. **Update Components**
   - Header with language switcher
   - Search components use current language
   - CMS pages use translated content
   - Product cards show language-aware names

2. **RTL Layout Adjustments**
   - Navigation direction
   - Text alignment
   - Icon positions

### Phase 10.1.5: SEO Implementation

1. **Dynamic HTML Attributes**
   - Update `<html lang="..." dir="...">` based on current language

2. **Meta Tags**
   - Language-aware titles
   - Language-aware descriptions

3. **Canonical URLs**
   - Consider language in canonical URLs

---

## 🔧 File Changes

### New Files (Frontend)

```
src/i18n/
├── index.ts                 # Main i18n export
├── languageStore.ts         # Zustand language store
├── useTranslation.ts        # Translation hook
├── locales/
│   ├── en.json              # English translations
│   └── ar.json              # Arabic translations
└── types.ts                 # i18n types

src/components/common/
└── LanguageSwitcher.tsx     # Language toggle component
```

### Modified Files (Frontend)

```
src/app/App.tsx              # Add language provider
src/main.tsx                 # Initialize language
index.html                   # Dynamic lang/dir
src/components/layout/Header.tsx  # Add language switcher
src/components/search/SearchAutocomplete.tsx  # Use translations
src/services/api/searchApi.ts    # Add language parameter
src/services/api/httpClient.ts   # Add Accept-Language header
```

### Modified Files (Backend)

```
PointOfSale.Infrastructure/Elasticsearch/
├── Documents/
│   ├── ProductSearchDocument.cs   # Add multi-language fields
│   ├── CategorySearchDocument.cs  # Add multi-language fields
│   └── CmsPageSearchDocument.cs   # Add multi-language fields
├── Services/
│   ├── IUnifiedSearchIndexService.cs  # Add language parameter
│   └── UnifiedSearchIndexService.cs   # Implement language-aware search
└── ElasticsearchIndexInitializer.cs   # Update mappings

PointOfSale.API/Controllers/
└── StorefrontSearchController.cs  # Add language support
```

---

## 📊 Elasticsearch Mapping Strategy

### Current Schema (Single Language)
```json
{
  "searchText": {
    "type": "text",
    "analyzer": "standard"
  }
}
```

### New Schema (Multi-Language)
```json
{
  "searchText": {
    "properties": {
      "en": {
        "type": "text",
        "analyzer": "english"
      },
      "ar": {
        "type": "text",
        "analyzer": "arabic"
      }
    }
  }
}
```

---

## ✅ Verification Checklist

- [ ] Switching language updates UI text
- [ ] Switching language updates CMS content
- [ ] Arabic search queries return Arabic-relevant results
- [ ] English search queries return English-relevant results
- [ ] Autocomplete respects language
- [ ] No duplicate Elasticsearch documents
- [ ] No SQL reads added to Storefront services
- [ ] Tenant A never sees Tenant B content (any language)
- [ ] Redis keys remain tenant-namespaced
- [ ] Fallback to English works if Arabic missing
- [ ] RTL layout works correctly for Arabic

---

## 🚀 Implementation Order

1. **Frontend i18n infrastructure** - Safe, no backend changes
2. **Language store + UI components** - Visible progress
3. **Elasticsearch schema update** - Requires index recreation
4. **Backend API language support** - Search service changes
5. **Frontend integration** - Connect all pieces
6. **SEO implementation** - Final polish

---

## ⚠️ Migration Notes

### Elasticsearch Index Recreation
The Elasticsearch index needs to be recreated with new mappings. This involves:
1. Delete existing indices
2. Restart API to create new indices with proper mappings
3. Re-sync all products/categories/CMS pages

### Breaking Changes
None - all changes are additive. Existing functionality continues to work.

---

## 📝 Design Decisions

1. **Language Storage**: Cookie + URL query param (not URL path segment)
   - Simpler routing
   - Works with existing architecture
   - Easy to switch languages

2. **i18n Library**: Custom lightweight implementation
   - No external dependencies
   - Zustand-based for consistency
   - Simple JSON translation files

3. **Fallback Strategy**: English is the primary language
   - If Arabic content missing, use English
   - Never show empty fields

4. **Search Strategy**: Query appropriate language field
   - `lang=ar` → Query `searchText.ar` with Arabic analyzer
   - `lang=en` → Query `searchText.en` with English analyzer
