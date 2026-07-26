import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const currentDir = dirname(fileURLToPath(import.meta.url))
const builderPagePath = join(currentDir, 'pages', 'StorefrontBuilderPage.jsx')
const versionHistoryPagePath = join(currentDir, 'pages', 'VersionHistoryPage.jsx')

function readSource(filePath: string) {
    return readFileSync(filePath, 'utf8')
}

describe('storefront version history source of truth', () => {
    it('does not ship mock version arrays in admin pages', () => {
        const builderSource = readSource(builderPagePath)
        const historySource = readSource(versionHistoryPagePath)

        expect(builderSource).not.toContain('Mock data for now')
        expect(builderSource).not.toContain('publishedBy: \'Admin\'')
        expect(historySource).not.toContain('const mockVersions =')
        expect(historySource).not.toContain('publishedBy: \'Admin\'')
    })

    it('loads rollback choices from the real backend versions endpoint', () => {
        const builderSource = readSource(builderPagePath)
        const historySource = readSource(versionHistoryPagePath)

        expect(builderSource).toContain('storeConfigService.getVersionHistory()')
        expect(historySource).toContain('storeConfigService.getVersionHistory()')
    })

    it('submits rollback intent only with admitted backend RowVersion evidence', () => {
        const builderSource = readSource(builderPagePath)
        const historySource = readSource(versionHistoryPagePath)

        expect(builderSource).toContain('buildIfMatchOptions(rowVersion)')
        expect(builderSource).toContain('storeConfigService.rollback(')
        expect(historySource).toContain('deriveCurrentStoreIdentityPresentation(')
        expect(historySource).toContain('await storeConfigService.getStoreConfig()')
        expect(historySource).toContain('Rollback requires current backend RowVersion authority.')
        expect(historySource).toContain('Rollback response authority was not admitted.')
        expect(historySource).toContain("'If-Match': `\"${rowVersion}\"`")
    })
})
