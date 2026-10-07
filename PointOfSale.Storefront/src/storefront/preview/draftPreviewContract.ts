import type { StoreConfig, Navigation, StorefrontFooter } from '@/types';

export interface DraftPreviewConfig extends StoreConfig {
    previewChrome: {
        runtimeUrl: string;
        navigation: Navigation;
        footerByLanguage: Record<'en' | 'ar', StorefrontFooter>;
    };
}

export const DRAFT_PREVIEW_PROTOCOL = 'shopixy.admin-draft-preview.v1';

export function admitDraftPreviewMessage(
    event: Pick<MessageEvent, 'origin' | 'source' | 'data'>,
    parent: Window,
    origin: string,
    tenantId: string,
    nonce: string,
): { config: DraftPreviewConfig; language: 'en' | 'ar' } | null {
    if (!origin || !tenantId || !nonce || event.source !== parent || event.origin !== origin) return null;
    const data = event.data;
    if (data?.protocol !== DRAFT_PREVIEW_PROTOCOL || data.type !== 'apply' || data.nonce !== nonce) return null;
    const config = data.config as DraftPreviewConfig | undefined;
    if (config?.tenantId !== tenantId || !config.theme || !config.pages?.home || !config.previewChrome?.navigation
        || !config.previewChrome.footerByLanguage?.en || !config.previewChrome.footerByLanguage?.ar) return null;
    if (data.language !== 'en' && data.language !== 'ar') return null;
    return { config, language: data.language };
}
