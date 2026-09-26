import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const currentDir = dirname(fileURLToPath(import.meta.url))
const builderPagePath = join(currentDir, 'pages', 'StorefrontBuilderPage.jsx')
const themeEditorPagePath = join(currentDir, 'pages', 'ThemeEditorPage.jsx')
const themeMarketplacePagePath = join(currentDir, 'pages', 'ThemeMarketplacePage.jsx')
const themeMarketplaceRegistryPath = join(currentDir, 'themes', 'themeMarketplaceRegistry.js')

function readSource(filePath: string) {
    return readFileSync(filePath, 'utf8')
}

describe('storefront builder workspace authority', () => {
    it('withholds editable workspace rendering until backend admission succeeds', () => {
        const source = readSource(builderPagePath)

        expect(source).toContain('if (!storeConfig)')
        expect(source).toContain('Workspace admission failed')
        expect(source).toContain('Checking the current store and draft with backend authority.')
        expect(source).toContain('<BuilderCanvas activeId={activeId} onLoadDemo={handleLoadDemo} storeConfig={storeConfig} tenantId={tenantId} templateId={templateIdFromUrl} />')
        expect(source).toContain('<SectionPalette pageKey={targetPage} />')
    })

    it('requires backend row-version evidence before workspace mutations', () => {
        const source = readSource(builderPagePath)

        expect(source).toContain('requireWorkspaceAdmission')
        expect(source).toContain('getAcceptedWorkspaceRowVersion')
        expect(source).toContain('buildIfMatchOptions(rowVersion')
        expect(source).toContain('storeConfigService.updatePageLayout(')
        expect(source).toContain('const acceptedConfig = admitWorkspaceConfig(response)')
    })

    it('hydrates section composition from accepted backend DTOs after bootstrap and save', () => {
        const source = readSource(builderPagePath)

        expect(source).toContain('getAcceptedSectionsFromConfig')
        expect(source).toContain('filterSectionsForPage(')
        expect(source).toContain('admitSections(acceptedSections)')
        expect(source).toContain('getAcceptedSectionsFromConfig(acceptedConfig, targetPage, templateIdFromUrl)')
        expect(source).toContain('resetHistory: false')
    })

    it('does not make the initial workspace loader depend on the storeConfig state it writes', () => {
        const source = readSource(builderPagePath)
        const loadPageDataDependencies = source.match(/const loadPageData = useCallback[\s\S]*?\n    }, \[([^\]]*)\]\)/)?.[1]

        expect(source).toContain('const getAcceptedWorkspaceRowVersion = useCallback((config) =>')
        expect(source).toContain('const admitWorkspaceConfig = useCallback((response, runtimeFallback = null) =>')
        expect(loadPageDataDependencies).toBe('admitSections, admitWorkspaceConfig, currentUser, getAcceptedSectionsFromConfig, isRTL')
        expect(loadPageDataDependencies).not.toContain('storeConfig')
        expect(loadPageDataDependencies).not.toContain('templateIdFromUrl')
        expect(source).not.toContain('runtimeFallback = storeConfig')
    })

    it('keeps explicit draft saves and preview backend-authoritative', () => {
        const source = readSource(builderPagePath)

        expect(source).toContain('const handleSave = useCallback(async () =>')
        expect(source).toContain('await persistPageLayout(sections)')
        expect(source).toContain('useBuilderStore.getState().sections !== sectionsToSave')
        expect(source).not.toContain('Debounced auto-save')
        expect(source).toContain('if (!result.saved)')
        expect(source).toContain('storeConfigService.getPreviewConfig')
        expect(source).toContain('Draft preview could not be loaded. No live content was shown.')
    })

    it('keeps builder theme mutations row-versioned and response-admitted', () => {
        const editorSource = readSource(themeEditorPagePath)
        const marketplaceSource = readSource(themeMarketplacePagePath)

        expect(editorSource).toContain('deriveCurrentStoreIdentityPresentation(response, user)')
        expect(editorSource).toContain("throw new Error('Theme response was rejected by backend store authority admission.')")
        expect(editorSource).toContain('buildIfMatchOptions(rowVersion)')
        expect(editorSource).toContain('storeConfigService.updateThemeOverrides({')
        expect(editorSource).toContain('storeConfigService.publishTheme(buildIfMatchOptions(rowVersion))')
        expect(editorSource).toContain('setAcceptedRowVersion(rowVersion)')

        expect(marketplaceSource).toContain('deriveCurrentStoreIdentityPresentation(response, user)')
        expect(marketplaceSource).toContain("throw new Error('Theme activation response was rejected by backend store authority admission.')")
        expect(marketplaceSource).toContain('storeConfigService.activateTheme(')
        expect(marketplaceSource).not.toContain('storeConfigService.updateStoreConfig({')
    })

    it('keeps marketplace themes manifest-backed with duplicate detection', () => {
        const registrySource = readSource(themeMarketplaceRegistryPath)

        expect(registrySource).toContain('version:')
        expect(registrySource).toContain('capabilities:')
        expect(registrySource).toContain('validateThemeMarketplaceRegistry')
        expect(registrySource).toContain('Duplicate or empty theme id')
        expect(registrySource).toContain('Exactly one default theme')
    })
})
