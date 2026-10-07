import { describe, expect, it } from 'vitest';
import { admitDraftPreviewMessage, DRAFT_PREVIEW_PROTOCOL, type DraftPreviewConfig } from './draftPreviewContract';
import { DEFAULT_STORE_CONFIG } from '@/types/storeConfig';
import { resolveActiveTheme } from '@/storefront/themes/ThemeResolver';

describe('authenticated draft runtime contract', () => {
    const parent = {} as Window;
    const origin = 'https://admin.example.test';
    const config = {
        ...DEFAULT_STORE_CONFIG, tenantId: 'tenant-a', storefrontTheme: 'pearl',
        pages: { home: { sections: [] }, product: { sections: [] }, category: { sections: [] } },
        previewChrome: { runtimeUrl: 'https://shop.example.test/draft-preview.html', navigation: { header: { links: [], announcement: { text: 'Draft' } }, footer: { links: [] } }, footerByLanguage: { en: { sections: [] }, ar: { sections: [] } } },
    } as unknown as DraftPreviewConfig;
    const event = { origin, source: parent, data: { protocol: DRAFT_PREVIEW_PROTOCOL, type: 'apply', nonce: 'session-nonce', language: 'ar', config } };
    it('uses selected candidate theme without changing live configuration', () => {
        const candidate = admitDraftPreviewMessage(event, parent, origin, 'tenant-a', 'session-nonce')!;
        expect(resolveActiveTheme(candidate.config.storefrontTheme).id).toBe('pearl');
        expect(DEFAULT_STORE_CONFIG.storefrontTheme).toBe('modern');
        expect(candidate.config.previewChrome.navigation.header.announcement?.text).toBe('Draft');
        expect(candidate.language).toBe('ar');
    });
    it.each([
        ['foreign origin', { ...event, origin: 'https://evil.example.test' }],
        ['foreign frame', { ...event, source: {} as Window }],
        ['foreign tenant', { ...event, data: { ...event.data, config: { ...config, tenantId: 'tenant-b' } } }],
        ['stale session', { ...event, data: { ...event.data, nonce: 'old-nonce' } }],
        ['missing chrome', { ...event, data: { ...event.data, config: DEFAULT_STORE_CONFIG } }],
        ['invalid language', { ...event, data: { ...event.data, language: 'xx' } }],
    ])('rejects %s', (_label, invalid) => {
        expect(admitDraftPreviewMessage(invalid, parent, origin, 'tenant-a', 'session-nonce')).toBeNull();
    });
    it('fails closed without configured Admin origin', () => {
        expect(admitDraftPreviewMessage(event, parent, '', 'tenant-a', 'session-nonce')).toBeNull();
    });
});
