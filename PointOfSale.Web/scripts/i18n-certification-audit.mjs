import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const ROOT_DIR = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..')
const LOCALES_DIR = path.join(ROOT_DIR, 'src', 'i18n', 'locales')

const AUDITED_FILES = [
  'src/components/ReportTruthBadge.jsx',
  'src/components/Sidebar.jsx',
  'src/components/LanguageSwitcher.jsx',
  'src/components/OpenCloseCashboxModal.jsx',
  'src/components/ProductForm.jsx',
  'src/components/ProductMediaManager.jsx',
  'src/components/ProductPixelOverrideTab.jsx',
  'src/components/ProductVariantsTab.jsx',
  'src/pages/Adjustments.jsx',
  'src/pages/CommerceHubSettings.jsx',
  'src/pages/Products.jsx',
  'src/pages/ProductWorkspacePage.jsx',
  'src/pages/SupplierCardFundingInstruments.jsx',
  'src/pages/treasury/TreasuryAccountViewer.jsx',
  'src/pages/treasury/TreasuryBalanceSheet.jsx',
  'src/pages/treasury/TreasuryCashFlow.jsx',
  'src/pages/treasury/TreasuryClearingAging.jsx',
  'src/pages/treasury/TreasuryDashboard.jsx',
  'src/pages/treasury/TreasuryGeneralLedger.jsx',
  'src/pages/treasury/TreasuryHub.jsx',
  'src/pages/treasury/TreasuryProfitLoss.jsx',
  'src/pages/treasury/TreasuryTransfers.jsx',
  'src/pages/treasury/TreasuryTrialBalance.jsx',
  'src/marketplace/components/ChatMessage.tsx',
  'src/marketplace/components/Filters.tsx',
  'src/marketplace/components/ImageUpload.tsx',
  'src/marketplace/components/RequestCard.tsx',
  'src/marketplace/components/ReviewModal.tsx',
  'src/marketplace/components/SearchBar.tsx',
  'src/marketplace/components/ProfileFormModal.tsx',
  'src/marketplace/components/ServiceCard.tsx',
  'src/marketplace/components/ServiceFormModal.tsx',
  'src/marketplace/pages/ChatPage.tsx',
  'src/marketplace/pages/MarketplaceShell.tsx',
  'src/marketplace/pages/ServiceDetailsPage.tsx',
  'src/marketplace/pages/tabs/DiscoverTab.tsx',
  'src/marketplace/pages/tabs/IncomingTab.tsx',
  'src/marketplace/pages/tabs/MyRequestsTab.tsx',
  'src/marketplace/pages/tabs/MyServicesTab.tsx',
  'src/marketplace/pages/tabs/ProfileTab.tsx',
  'src/marketplace-cms/MarketplaceCmsPublicPage.jsx',
  'src/marketplace-cms/MarketplaceHeader.jsx',
  'src/marketplace-cms/MarketplaceLanguageSwitcher.jsx',
  'src/marketplace-cms/MarketplaceProductPublicPage.jsx',
  'src/marketplace-cms/MarketplaceProductsPublicPage.jsx',
]

// Manual keys scoped to specific file patterns (only applied to matching files)
const MANUAL_KEYS_BY_FILE = {
  // Services marketplace pages and components
  'src/marketplace/': [
    'servicesMarketplace.categories.Marketing',
    'servicesMarketplace.categories.Design',
    'servicesMarketplace.categories.Development',
    'servicesMarketplace.categories.Video',
    'servicesMarketplace.categories.Writing',
    'servicesMarketplace.categories.Music',
    'servicesMarketplace.categories.Business',
    'servicesMarketplace.categories.Consulting',
    'servicesMarketplace.categories.Other',
    'servicesMarketplace.pricingType.Fixed',
    'servicesMarketplace.pricingType.Hourly',
    'servicesMarketplace.pricingType.Custom',
    'servicesMarketplace.pricingType.Package',
    'servicesMarketplace.currency.EGP',
    'servicesMarketplace.currency.USD',
    'servicesMarketplace.currency.EUR',
    'servicesMarketplace.currency.GBP',
    'servicesMarketplace.platforms.Facebook',
    'servicesMarketplace.platforms.Instagram',
    'servicesMarketplace.platforms.Google',
    'servicesMarketplace.platforms.TikTok',
    'servicesMarketplace.platforms.Twitter',
    'servicesMarketplace.platforms.LinkedIn',
    'servicesMarketplace.platforms.YouTube',
    'servicesMarketplace.platforms.Snapchat',
    'servicesMarketplace.platforms.Email',
    'servicesMarketplace.platforms.WhatsApp',
    'servicesMarketplace.platforms.Other',
    'servicesMarketplace.badges.gold',
    'servicesMarketplace.badges.silver',
    'servicesMarketplace.badges.bronze',
    'servicesMarketplace.badges.verified',
    'servicesMarketplace.badges.new',
    'servicesMarketplace.badges.rising',
    'servicesMarketplace.badges.trusted',
    'servicesMarketplace.badges.topRated',
    'servicesMarketplace.status.pending',
    'servicesMarketplace.status.accepted',
    'servicesMarketplace.status.completed',
    'servicesMarketplace.status.completedByProvider',
    'servicesMarketplace.status.completedByBuyer',
    'servicesMarketplace.status.cancelled',
    'servicesMarketplace.status.confirmed',
  ],
}

function getManualKeysForFile(filePath) {
  const result = []
  for (const [pattern, keys] of Object.entries(MANUAL_KEYS_BY_FILE)) {
    if (filePath.includes(pattern)) {
      result.push(...keys)
    }
  }
  return result
}

export function readLocaleResources() {
  return {
    en: JSON.parse(fs.readFileSync(path.join(LOCALES_DIR, 'en.json'), 'utf8')),
    ar: JSON.parse(fs.readFileSync(path.join(LOCALES_DIR, 'ar.json'), 'utf8')),
  }
}

function flattenObject(value, prefix = '', output = {}) {
  if (Array.isArray(value)) {
    output[prefix] = value
    return output
  }
  if (value && typeof value === 'object') {
    Object.entries(value).forEach(([key, child]) => {
      flattenObject(child, prefix ? `${prefix}.${key}` : key, output)
    })
    return output
  }
  output[prefix] = value
  return output
}

function extractPlaceholders(value) {
  const matches = String(value).match(/\{\{[^}]+\}\}/g) || []
  return [...new Set(matches)].sort()
}

// Arabic CLDR plural forms
const ARABIC_PLURAL_SUFFIXES = ['_zero', '_one', '_two', '_few', '_many', '_other']

function extractPluralBase(key) {
  for (const suffix of ARABIC_PLURAL_SUFFIXES) {
    if (key.endsWith(suffix)) {
      return key.slice(0, -suffix.length)
    }
  }
  return null
}

function validatePluralForms(flatEn, flatAr) {
  const pluralBases = new Map()
  const allKeys = new Set([...Object.keys(flatEn), ...Object.keys(flatAr)])

  // Group keys by their plural base
  for (const key of allKeys) {
    const base = extractPluralBase(key)
    if (base) {
      if (!pluralBases.has(base)) {
        pluralBases.set(base, { enForms: [], arForms: [] })
      }
      const entry = pluralBases.get(base)
      if (key in flatEn) entry.enForms.push(key.slice(base.length))
      if (key in flatAr) entry.arForms.push(key.slice(base.length))
    }
  }

  // Check for missing Arabic plural forms
  const mismatches = []
  for (const [base, { arForms }] of pluralBases) {
    if (arForms.length === 0) continue // No Arabic forms at all, caught by missingInAr
    const missing = ARABIC_PLURAL_SUFFIXES.filter((suffix) => !arForms.includes(suffix))
    if (missing.length > 0) {
      mismatches.push({ base, missing: missing.sort() })
    }
  }

  return mismatches.sort((a, b) => a.base.localeCompare(b.base))
}

function extractTranslationKeys(filePath) {
  const source = fs.readFileSync(path.join(ROOT_DIR, filePath), 'utf8')
  const keys = new Set()
  const matcher = /\bt\(\s*['"`]([^'"`]+)['"`]/g
  let match
  while ((match = matcher.exec(source))) {
    if (!match[1].includes('${')) {
      keys.add(match[1])
    }
  }
  return keys
}

function buildAuditedPages(flatEn, flatAr) {
  return AUDITED_FILES.map((filePath) => {
    const manualKeys = getManualKeysForFile(filePath)
    const keys = new Set([...extractTranslationKeys(filePath), ...manualKeys])
    const orderedKeys = [...keys].sort()
    return {
      page: filePath,
      keys: orderedKeys,
      missingInEn: orderedKeys.filter((key) => !(key in flatEn)),
      missingInAr: orderedKeys.filter((key) => !(key in flatAr)),
    }
  })
}

export function buildI18nCertificationAudit() {
  const { en, ar } = readLocaleResources()
  const flatEn = flattenObject(en)
  const flatAr = flattenObject(ar)
  const enKeys = Object.keys(flatEn)
  const arKeys = Object.keys(flatAr)
  const sharedKeys = enKeys.filter((key) => key in flatAr)

  return {
    structural: {
      missingInEn: arKeys.filter((key) => !(key in flatEn)).sort(),
      missingInAr: enKeys.filter((key) => !(key in flatAr)).sort(),
      placeholderMismatches: sharedKeys
        .filter((key) => JSON.stringify(extractPlaceholders(flatEn[key])) !== JSON.stringify(extractPlaceholders(flatAr[key])))
        .map((key) => ({
          key,
          en: extractPlaceholders(flatEn[key]),
          ar: extractPlaceholders(flatAr[key]),
        })),
      suspiciousEntries: sharedKeys
        .filter((key) => {
          const values = [flatEn[key], flatAr[key]].map((value) => String(value ?? ''))
          return values.some((value) => value.includes('???') || value.includes('�'))
        })
        .sort(),
      pluralFormMismatches: validatePluralForms(flatEn, flatAr),
    },
    auditedPages: buildAuditedPages(flatEn, flatAr),
  }
}
