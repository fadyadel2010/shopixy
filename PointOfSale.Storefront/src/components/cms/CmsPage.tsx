import React, { useEffect, useMemo, useState } from 'react';
import { fetchCmsPage } from '@/services/api/storefrontApi';
import { useTranslation } from '@/i18n/useTranslation';
import type { CmsPage, CmsPageBlock, CmsPageContentType } from '@/types';
import { sanitizeRichTextHtml } from '@/utils/htmlSanitizer';

interface CmsPageProps {
    slug: string;
    preview?: boolean;
    previewToken?: string | null;
}

export const CmsPageView: React.FC<CmsPageProps> = ({ slug, preview = false, previewToken = null }) => {
    const { t, language } = useTranslation();
    const [page, setPage] = useState<CmsPage | null>(null);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;

        async function loadPage() {
            setLoading(true);
            setError(null);

            try {
                const data = await fetchCmsPage(slug, preview, previewToken, language);
                if (!cancelled) {
                    setPage(data);
                    applySeo(data, preview);
                }
            } catch (err) {
                if (!cancelled) {
                    console.error('Failed to load CMS page:', err);
                    setError('Page not found');
                }
            } finally {
                if (!cancelled) {
                    setLoading(false);
                }
            }
        }

        loadPage();

        return () => {
            cancelled = true;
        };
    }, [language, preview, previewToken, slug]);

    if (loading) {
        return (
            <div className="mx-auto flex min-h-[320px] max-w-5xl items-center justify-center px-6 py-14">
                <div className="h-10 w-10 animate-spin rounded-full border-4 border-slate-200 border-t-slate-500" />
            </div>
        );
    }

    if (error || !page) {
        return (
            <div className="mx-auto max-w-3xl px-6 py-16 text-center">
                <h1 className="text-3xl font-semibold text-slate-900">{t('cms.pageNotFound')}</h1>
                <p className="mt-3 text-slate-500">{t('cms.pageNotFoundDesc')}</p>
                <a
                    href="/"
                    className="mt-8 inline-flex rounded-full bg-slate-900 px-5 py-3 text-sm font-medium text-white transition hover:bg-slate-800"
                >
                    {t('cms.goHome')}
                </a>
            </div>
        );
    }

    return (
        <div
            lang={page.language}
            dir={page.language === 'ar' ? 'rtl' : 'ltr'}
            className="bg-[radial-gradient(circle_at_top,_rgba(14,165,233,0.12),_transparent_45%)]"
        >
            <div className="mx-auto max-w-5xl px-6 py-14">
                <header className="rounded-[36px] border border-slate-200 bg-white/90 px-8 py-10 shadow-sm backdrop-blur">
                    {preview && (
                        <span className="inline-flex rounded-full bg-amber-100 px-3 py-1 text-xs font-semibold uppercase tracking-[0.18em] text-amber-800">
                            Preview
                        </span>
                    )}
                    <h1 className="mt-4 text-4xl font-semibold tracking-tight text-slate-950">{page.title}</h1>
                    {page.seo.metaDescription && (
                        <p className="mt-4 max-w-3xl text-base leading-7 text-slate-600">
                            {page.seo.metaDescription}
                        </p>
                    )}
                </header>

                <CmsPageRenderer page={page} />
            </div>
        </div>
    );
};

interface CmsPageRendererProps {
    page: CmsPage;
}

export const CmsPageRenderer: React.FC<CmsPageRendererProps> = ({ page }) => {
    const contentType = page.contentType as CmsPageContentType;

    if (contentType === 'RichText') {
        return <RichTextPage html={typeof page.content === 'string' ? page.content : ''} />;
    }

    return <BlocksPage blocks={Array.isArray(page.content) ? page.content : []} />;
};

const RichTextPage: React.FC<{ html: string }> = ({ html }) => {
    const sanitizedHtml = useMemo(() => sanitizeRichTextHtml(html), [html]);

    if (!sanitizedHtml.trim()) {
        return <InvalidContentFallback />;
    }

    return (
        <article className="prose prose-slate mx-auto mt-8 max-w-4xl rounded-[32px] border border-slate-200 bg-white px-8 py-10 shadow-sm">
            <div dangerouslySetInnerHTML={{ __html: sanitizedHtml }} />
        </article>
    );
};

const BlocksPage: React.FC<{ blocks: CmsPageBlock[] }> = ({ blocks }) => {
    if (blocks.length === 0) {
        return <InvalidContentFallback />;
    }

    return (
        <div className="mt-8 space-y-6">
            {blocks.map((block, index) => (
                <BlockRenderer key={`${block.type}-${index}`} block={block} />
            ))}
        </div>
    );
};

const BlockRenderer: React.FC<{ block: CmsPageBlock }> = ({ block }) => {
    switch (block.type) {
        case 'text':
            return <TextBlock block={block} />;
        case 'image':
            return <ImageBlock block={block} />;
        case 'faq':
            return <FaqBlock block={block} />;
        default:
            return (
                <section className="rounded-[28px] border border-dashed border-slate-300 bg-white px-6 py-8 text-sm text-slate-500">
                    Unsupported block type: {block.type}
                </section>
            );
    }
};

const TextBlock: React.FC<{ block: CmsPageBlock }> = ({ block }) => {
    const alignment = block.data?.alignment === 'center'
        ? 'text-center'
        : block.data?.alignment === 'right'
            ? 'text-right'
            : 'text-left';

    return (
        <section className="rounded-[28px] border border-slate-200 bg-white px-8 py-8 shadow-sm">
            <div className={`mx-auto max-w-3xl whitespace-pre-line text-base leading-8 text-slate-700 ${alignment}`}>
                {block.data?.text || ''}
            </div>
        </section>
    );
};

const ImageBlock: React.FC<{ block: CmsPageBlock }> = ({ block }) => {
    const imageUrl = block.data?.imageUrl || '';
    if (!imageUrl) {
        return <InvalidContentFallback />;
    }

    return (
        <section className="overflow-hidden rounded-[32px] border border-slate-200 bg-white shadow-sm">
            <img src={imageUrl} alt={block.data?.alt || 'CMS image'} className="h-auto w-full object-cover" loading="lazy" />
            {block.data?.caption && (
                <div className="border-t border-slate-200 px-6 py-4 text-sm text-slate-500">
                    {block.data.caption}
                </div>
            )}
        </section>
    );
};

const FaqBlock: React.FC<{ block: CmsPageBlock }> = ({ block }) => {
    const items = Array.isArray(block.data?.items) ? block.data.items : [];
    if (items.length === 0) {
        return <InvalidContentFallback />;
    }

    return (
        <section className="rounded-[28px] border border-slate-200 bg-white px-8 py-8 shadow-sm">
            <div className="space-y-4">
                {items.map((item, index) => (
                    <details key={index} className="group rounded-2xl border border-slate-200 bg-slate-50 px-5 py-4">
                        <summary className="cursor-pointer list-none text-base font-medium text-slate-900">
                            {item.question}
                        </summary>
                        <p className="mt-3 whitespace-pre-line text-sm leading-7 text-slate-600">
                            {item.answer}
                        </p>
                    </details>
                ))}
            </div>
        </section>
    );
};

const InvalidContentFallback: React.FC = () => (
    <section className="mt-8 rounded-[28px] border border-dashed border-slate-300 bg-white px-8 py-12 text-center text-sm text-slate-500">
        This page contains invalid or empty content.
    </section>
);

function applySeo(page: CmsPage, preview: boolean): void {
    document.title = page.seo.metaTitle || page.title;
    setMeta('description', page.seo.metaDescription || '');
    setMeta('keywords', page.seo.metaKeywords || '');
    setMeta('robots', preview ? 'noindex, nofollow' : 'index, follow');
    setMetaProperty('og:title', page.seo.metaTitle || page.title);
    setMetaProperty('og:description', page.seo.metaDescription || '');
    setMetaProperty('og:image', page.seo.openGraphImageUrl || '');
    setLink('canonical', absoluteUrl(page.canonicalPath));
    document.querySelectorAll('link[data-cms-hreflang]').forEach((element) => element.remove());
    Object.entries(page.alternatePaths || {}).forEach(([hrefLang, path]) => {
        const link = document.createElement('link');
        link.rel = 'alternate';
        link.hreflang = hrefLang;
        link.href = absoluteUrl(path);
        link.dataset.cmsHreflang = 'true';
        document.head.appendChild(link);
    });
}

function absoluteUrl(path: string): string {
    return new URL(path || '/', window.location.origin).toString();
}

function setLink(rel: string, href: string): void {
    let element = document.querySelector<HTMLLinkElement>(`link[rel="${rel}"]`);
    if (!element) {
        element = document.createElement('link');
        element.rel = rel;
        document.head.appendChild(element);
    }
    element.href = href;
}

function setMeta(name: string, content: string): void {
    let element = document.querySelector(`meta[name="${name}"]`);
    if (!element) {
        element = document.createElement('meta');
        element.setAttribute('name', name);
        document.head.appendChild(element);
    }

    element.setAttribute('content', content);
}

function setMetaProperty(property: string, content: string): void {
    let element = document.querySelector(`meta[property="${property}"]`);
    if (!element) {
        element = document.createElement('meta');
        element.setAttribute('property', property);
        document.head.appendChild(element);
    }

    element.setAttribute('content', content);
}

export default CmsPageView;
