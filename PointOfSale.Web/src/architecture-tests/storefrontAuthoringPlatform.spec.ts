import { describe, expect, it } from 'vitest'
import * as fs from 'node:fs'
import * as path from 'node:path'

const SRC_DIR = path.join(__dirname, '..')
const PLATFORM_DIR = path.join(SRC_DIR, 'modules', 'storefront', 'authoringPlatform')
const LEGACY_PLATFORM_DIR = path.join(SRC_DIR, 'modules', 'storefront', 'authoringExperience')

const featureFiles = [
    'modules/storefront/pages/StorefrontBuilderPage.jsx',
    'modules/storefront/components/builder/BuilderTopBar.jsx',
    'modules/storefront/components/builder/PreviewModal.jsx',
    'modules/storefront/cms/pages/CmsPagesList.jsx',
    'modules/storefront/cms/pages/CmsPageEditor.jsx',
    'modules/storefront/cms/pages/NavigationEditor.jsx',
    'shared/content/FooterConfigScreen.jsx',
    'modules/storefront/pages/ThemeMarketplacePage.jsx',
    'modules/storefront/pages/ThemeEditorPage.jsx',
    'pages/StorefrontSettings.jsx',
    'modules/storefront/pages/VersionHistoryPage.jsx',
].map((relativePath) => path.join(SRC_DIR, relativePath))

function source(filePath: string) {
    return fs.readFileSync(filePath, 'utf8')
}

function sourceFiles(directory: string): string[] {
    return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
        const entryPath = path.join(directory, entry.name)
        if (entry.isDirectory()) return sourceFiles(entryPath)
        return /\.(js|jsx|ts|tsx)$/.test(entry.name) && !entry.name.includes('.test.') ? [entryPath] : []
    })
}

describe('Wave 3 Storefront Authoring Platform architecture', () => {
    it('has one public platform and no legacy authoring framework', () => {
        expect(fs.existsSync(PLATFORM_DIR)).toBe(true)
        expect(fs.existsSync(path.join(PLATFORM_DIR, 'index.js'))).toBe(true)
        const legacySources = fs.existsSync(LEGACY_PLATFORM_DIR) ? sourceFiles(LEGACY_PLATFORM_DIR) : []
        expect(legacySources).toEqual([])

        const requiredAreas = ['accessibility', 'components', 'contracts', 'dialogs', 'feedback', 'history', 'keyboard', 'navigation', 'providers', 'shell', 'status', 'testing', 'validation']
        requiredAreas.forEach((area) => expect(fs.existsSync(path.join(PLATFORM_DIR, area)), `Missing platform area: ${area}`).toBe(true))
    })

    it('requires every Storefront authoring feature to use the stable public contract', () => {
        featureFiles.forEach((filePath) => {
            const contents = source(filePath)
            expect(contents, `${filePath} does not depend on the Authoring Platform`).toContain('authoringPlatform')
            expect(contents, `${filePath} deep-imports Authoring Platform internals`).not.toMatch(/authoringPlatform\/(components|contracts|dialogs|feedback|history|keyboard|navigation|providers|shell|status|validation)/)
        })
    })

    it('prevents feature-owned dialogs, focus traps, dirty guards, keyboard infrastructure, and native prompts', () => {
        featureFiles.forEach((filePath) => {
            const contents = source(filePath)
            expect(contents, `${filePath} owns dialog semantics`).not.toMatch(/role=["']dialog["']|aria-modal=/)
            expect(contents, `${filePath} owns navigation or keyboard event infrastructure`).not.toMatch(/addEventListener\s*\(\s*["'](?:beforeunload|keydown)["']/)
            expect(contents, `${filePath} uses a native authoring prompt`).not.toMatch(/window\.(?:confirm|alert|prompt)\s*\(/)
        })
    })

    it('prevents feature-owned authoring framework, validation presentation, and feedback infrastructure', () => {
        const nonPlatformStorefrontFiles = sourceFiles(path.join(SRC_DIR, 'modules', 'storefront'))
            .filter((filePath) => !filePath.startsWith(PLATFORM_DIR))

        nonPlatformStorefrontFiles.forEach((filePath) => {
            const contents = source(filePath)
            expect(contents, `${filePath} declares Authoring Platform infrastructure`).not.toMatch(/(?:function|class|const)\s+(?:Authoring(?:ActionBar|Button|Dialog|Feedback|LoadingState|EmptyState|PermissionState|StatusBadge|ValidationSummary)|UnsavedChangesDialog|useAuthoringGuard|useAuthoringShortcuts)\b/)
        })
        expect(fs.existsSync(path.join(SRC_DIR, 'shared', 'content', 'authoringErrorFeedback.js'))).toBe(false)
    })

    it('keeps the platform infrastructure-only and the shell/provider canonical', () => {
        const platformSource = sourceFiles(PLATFORM_DIR).map(source).join('\n')
        expect(platformSource).not.toMatch(/storeConfigService|cmsService|footerAuthoringService|builderStore|useBuilderStore|fetch\s*\(|axios/)

        const frame = source(path.join(SRC_DIR, 'modules', 'storefront', 'informationArchitecture', 'StorefrontAdminFrame.jsx'))
        expect(frame).toContain('AuthoringPlatformProvider')
        expect(frame).toContain('AuthoringShell')
        expect(frame).toContain('guardNavigation')
    })

    it('preserves exact feature lifecycle labels and ownership exclusions', () => {
        const combined = featureFiles.map(source).join('\n')
        const englishLocale = JSON.parse(source(path.join(SRC_DIR, 'i18n', 'locales', 'en.json')))
        expect(englishLocale.storefront.homepageEditor.publishHomepage).toBe('Publish Homepage')
        expect(englishLocale.storefront.homepageEditor.publishTemplate).toBe('Publish Product Template')
        ;[
            'Publish Page',
            'Publish Header and Footer Navigation',
            'Save live Footer',
            'Activate theme now',
            'Publish Colors & Typography',
            'Save live Status',
            'CMS Page History and Theme catalog revisions are excluded',
        ].forEach((contractText) => expect(combined).toContain(contractText))
    })
})
