import { useEffect, useMemo, useState } from 'react'
import ReactMarkdown from 'react-markdown'
import { LoaderCircle, Sparkles, X } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import api from '../../services/api'

const articleLoaders = {
    dashboard: () => import('./helpArticles/dashboard.md?raw'),
    customers: () => import('./helpArticles/customers.md?raw'),
    suppliers: () => import('./helpArticles/suppliers.md?raw'),
    'sales-representatives': () => import('./helpArticles/sales-representatives.md?raw'),
    categories: () => import('./helpArticles/categories.md?raw'),
    units: () => import('./helpArticles/units.md?raw'),
    warehouses: () => import('./helpArticles/warehouses.md?raw'),
    banks: () => import('./helpArticles/banks.md?raw'),
    products: () => import('./helpArticles/products.md?raw'),
    'create-product': () => import('./helpArticles/create-product.md?raw'),
    'edit-product': () => import('./helpArticles/edit-product.md?raw'),
    'product-variants': () => import('./helpArticles/product-variants.md?raw'),
    'product-media': () => import('./helpArticles/product-media.md?raw'),
    'product-templates': () => import('./helpArticles/product-templates.md?raw'),
    'financial-main-accounts': () => import('./helpArticles/financial-main-accounts.md?raw'),
    'financial-sub-accounts': () => import('./helpArticles/financial-sub-accounts.md?raw'),
    'balance-transfer': () => import('./helpArticles/balance-transfer.md?raw'),
    'opening-treasury': () => import('./helpArticles/opening-treasury.md?raw'),
    'opening-customers': () => import('./helpArticles/opening-customers.md?raw'),
    'opening-suppliers': () => import('./helpArticles/opening-suppliers.md?raw'),
    'opening-warehouse-stock': () => import('./helpArticles/opening-warehouse-stock.md?raw'),
    'pos-sales': () => import('./helpArticles/pos-sales.md?raw'),
    'sales-returns': () => import('./helpArticles/sales-returns.md?raw'),
    'purchase-invoice': () => import('./helpArticles/purchase-invoice.md?raw'),
    'purchase-returns': () => import('./helpArticles/purchase-returns.md?raw'),
    'treasury-dashboard': () => import('./helpArticles/treasury-dashboard.md?raw'),
    'treasury-hub': () => import('./helpArticles/treasury-hub.md?raw'),
    'cash-account': () => import('./helpArticles/cash-account.md?raw'),
    'bank-account': () => import('./helpArticles/bank-account.md?raw'),
    'visa-clearing': () => import('./helpArticles/visa-clearing.md?raw'),
    'customer-supplier-payments': () => import('./helpArticles/customer-supplier-payments.md?raw'),
    'customer-supplier-settlement': () => import('./helpArticles/customer-supplier-settlement.md?raw'),
    'outstanding-balances': () => import('./helpArticles/outstanding-balances.md?raw'),
    'inventory-balance': () => import('./helpArticles/inventory-balance.md?raw'),
    'stock-transfers': () => import('./helpArticles/stock-transfers.md?raw'),
    'barcode-printing': () => import('./helpArticles/barcode-printing.md?raw'),
    'expense-categories': () => import('./helpArticles/expense-categories.md?raw'),
    'expenses-movement': () => import('./helpArticles/expenses-movement.md?raw'),
    'expenses-report': () => import('./helpArticles/expenses-report.md?raw'),
    'customer-supplier-statement': () => import('./helpArticles/customer-supplier-statement.md?raw'),
    'bank-statement': () => import('./helpArticles/bank-statement.md?raw'),
    'visa-statement': () => import('./helpArticles/visa-statement.md?raw'),
    'ecommerce-orders': () => import('./helpArticles/ecommerce-orders.md?raw'),
    'blocked-phones': () => import('./helpArticles/blocked-phones.md?raw'),
    'whatsapp-settings': () => import('./helpArticles/whatsapp-settings.md?raw'),
    'cart-recovery': () => import('./helpArticles/cart-recovery.md?raw'),
    'abandoned-checkouts': () => import('./helpArticles/abandoned-checkouts.md?raw'),
    'shipping-setup': () => import('./helpArticles/shipping-setup.md?raw'),
    'online-customers': () => import('./helpArticles/online-customers.md?raw'),
    'auth-providers': () => import('./helpArticles/auth-providers.md?raw'),
    'payment-methods': () => import('./helpArticles/payment-methods.md?raw'),
    'manual-payments': () => import('./helpArticles/manual-payments.md?raw'),
    coupons: () => import('./helpArticles/coupons.md?raw'),
    promotions: () => import('./helpArticles/promotions.md?raw'),
    'smart-offers': () => import('./helpArticles/smart-offers.md?raw'),
    'commerce-ai': () => import('./helpArticles/commerce-ai.md?raw'),
    'pixel-tracking': () => import('./helpArticles/pixel-tracking.md?raw'),
    'conversion-funnels': () => import('./helpArticles/conversion-funnels.md?raw'),
    'marketing-attribution': () => import('./helpArticles/marketing-attribution.md?raw'),
    'campaign-costs': () => import('./helpArticles/campaign-costs.md?raw'),
    'marketing-roi': () => import('./helpArticles/marketing-roi.md?raw'),
    'analytics-settings': () => import('./helpArticles/analytics-settings.md?raw'),
}

const fallbackArticle = `# Help Center

## English

### Overview

Help for this page is not published yet.

### When to use this page

Open this panel whenever you want a quick explanation of the current screen.

### How to use it

Wait for the article for this page, or continue using the page normally.

### Tips

Use the supported pages in this first rollout to see the full help experience.

### Common mistakes

Assuming every page is documented in phase 1.

---

## العربية

### نظرة عامة

لم يتم نشر شرح هذه الصفحة بعد.

### متى تستخدم هذه الصفحة

افتح هذه اللوحة عندما تحتاج شرحًا سريعًا للشاشة الحالية.

### طريقة الاستخدام

انتظر إضافة المقال الخاص بهذه الصفحة، أو تابع العمل بشكل طبيعي.

### نصائح

جرّب الصفحات المدعومة في هذه المرحلة لرؤية تجربة المساعدة الكاملة.

### أخطاء شائعة

الاعتقاد أن جميع الصفحات موثقة في المرحلة الأولى.
`

function escapeRegExp(value) {
    return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

function extractSectionContent(markdown, heading) {
    if (!markdown || !heading) {
        return ''
    }

    const pattern = new RegExp(`${escapeRegExp(heading)}\\s*\\n([\\s\\S]*?)(?=\\n## |\\n### |\\n---|$)`, 'i')
    const match = markdown.match(pattern)
    return match?.[1]?.replace(/\n+/g, ' ').trim() ?? ''
}

function getPageDescription(markdown, isRTL, pathname) {
    const preferred = isRTL
        ? extractSectionContent(markdown, '### نظرة عامة') || extractSectionContent(markdown, '### Overview')
        : extractSectionContent(markdown, '### Overview') || extractSectionContent(markdown, '### نظرة عامة')

    if (preferred) {
        return preferred
    }

    return pathname || 'Current admin page'
}

function getModuleName(articleKey, pathname) {
    if (articleKey) {
        return articleKey
    }

    const segments = (pathname || '').split('/').filter(Boolean)
    return segments[0] || 'dashboard'
}

function detectIntent(question) {
    const normalized = (question || '').trim().toLowerCase()

    if (!normalized) {
        return 'general'
    }

    if (
        normalized.includes('how')
        || normalized.includes('ازاي')
        || normalized.includes('كيفية')
        || normalized.includes('طريقة')
        || normalized.includes('خطوات')
    ) {
        return 'how_to'
    }

    if (
        normalized.includes('ليه')
        || normalized.includes('مش')
        || normalized.includes('error')
        || normalized.includes('not working')
    ) {
        return 'troubleshooting'
    }

    return 'general'
}

function normalizeUiText(value) {
    return (value || '').replace(/\s+/g, ' ').trim()
}

function truncateUiText(value, maxLength = 240) {
    const normalized = normalizeUiText(value)
    if (normalized.length <= maxLength) {
        return normalized
    }

    return `${normalized.slice(0, maxLength - 1)}…`
}

function collectVisibleUiContext() {
    if (typeof document === 'undefined') {
        return null
    }

    const main = document.querySelector('main')
    if (!main) {
        return null
    }

    const headingElements = Array.from(main.querySelectorAll('h1, h2, h3'))
    const headings = headingElements
        .map((element) => truncateUiText(element.textContent, 120))
        .filter(Boolean)
        .slice(0, 8)

    const fields = Array.from(main.querySelectorAll('input, select, textarea'))
        .map((element) => {
            const label = truncateUiText(
                element.getAttribute('aria-label')
                || element.getAttribute('name')
                || element.getAttribute('id')
                || element.getAttribute('placeholder')
                || element.labels?.[0]?.textContent
                || element.closest('label')?.textContent,
                80
            )

            if (!label) {
                return null
            }

            let value = ''
            if (element instanceof HTMLInputElement) {
                if (element.type === 'password' || element.type === 'hidden') {
                    return null
                }

                if (element.type === 'checkbox' || element.type === 'radio') {
                    value = element.checked ? 'true' : 'false'
                } else {
                    value = element.value
                }
            } else if (element instanceof HTMLSelectElement || element instanceof HTMLTextAreaElement) {
                value = element.value
            }

            value = truncateUiText(value, 120)
            if (!value) {
                return null
            }

            return { label, value }
        })
        .filter(Boolean)
        .slice(0, 12)

    const buttons = Array.from(main.querySelectorAll('button'))
        .map((button) => truncateUiText(button.textContent, 80))
        .filter(Boolean)
        .slice(0, 10)

    const visibleTextPreview = truncateUiText(main.innerText, 2500)

    return {
        headings,
        fields,
        buttons,
        visibleTextPreview,
    }
}

function buildUiContext({ articleKey, pathname, pageDescription, hasArticle, question }) {
    const visibleUi = collectVisibleUiContext()

    return JSON.stringify(
        {
            currentRoute: pathname || '/',
            currentHelpArticleKey: articleKey || null,
            helpArticleLoaded: hasArticle,
            currentPageSummary: pageDescription || null,
            questionLength: (question || '').trim().length,
            helpPanelOpen: true,
            visibleUi,
        },
        null,
        2
    )
}

export default function HelpDrawer({ isOpen, onClose, articleKey, pathname }) {
    const { t, i18n } = useTranslation()
    const [content, setContent] = useState('')
    const [isLoading, setIsLoading] = useState(false)
    const [error, setError] = useState('')
    const [assistantQuestion, setAssistantQuestion] = useState('')
    const [assistantAnswer, setAssistantAnswer] = useState('')
    const [assistantError, setAssistantError] = useState('')
    const [isAskingAssistant, setIsAskingAssistant] = useState(false)
    const isRTL = i18n.language === 'ar'

    const resolvedTitle = useMemo(() => {
        if (articleKey) {
            return t('common.help', 'Help')
        }

        return t('common.helpCenter', 'Help Center')
    }, [articleKey, t])

    const pageDescription = useMemo(
        () => getPageDescription(content, isRTL, pathname),
        [content, isRTL, pathname]
    )

    const moduleName = useMemo(
        () => getModuleName(articleKey, pathname),
        [articleKey, pathname]
    )

    useEffect(() => {
        if (!isOpen) {
            return undefined
        }

        const handleKeydown = (event) => {
            if (event.key === 'Escape') {
                onClose()
            }
        }

        window.addEventListener('keydown', handleKeydown)
        return () => window.removeEventListener('keydown', handleKeydown)
    }, [isOpen, onClose])

    useEffect(() => {
        let isCancelled = false

        if (!isOpen) {
            return () => {
                isCancelled = true
            }
        }

        const loadArticle = async () => {
            const loader = articleKey ? articleLoaders[articleKey] : null

            if (!loader) {
                setContent(fallbackArticle)
                setError('')
                setIsLoading(false)
                return
            }

            setIsLoading(true)
            setError('')

            try {
                const module = await loader()
                if (!isCancelled) {
                    setContent(module.default)
                }
            } catch (loadError) {
                console.error('Failed to load help article:', loadError)
                if (!isCancelled) {
                    setError(t('common.helpLoadFailed', 'Failed to load help article.'))
                    setContent(fallbackArticle)
                }
            } finally {
                if (!isCancelled) {
                    setIsLoading(false)
                }
            }
        }

        loadArticle()

        return () => {
            isCancelled = true
        }
    }, [articleKey, isOpen, pathname, t])

    useEffect(() => {
        setAssistantQuestion('')
        setAssistantAnswer('')
        setAssistantError('')
    }, [isOpen, articleKey, pathname])

    const handleAskAssistant = async (event) => {
        event.preventDefault()

        if (!assistantQuestion.trim() || isAskingAssistant) {
            return
        }

        setIsAskingAssistant(true)
        setAssistantError('')
        setAssistantAnswer('')

        try {
            const intent = detectIntent(assistantQuestion)
            const response = await api.post('/admin/ai/help-assistant/ask', {
                question: assistantQuestion.trim(),
                helpArticleContent: (content || fallbackArticle).trim(),
                route: pathname || '/',
                module: moduleName,
                pageDescription,
                uiContext: buildUiContext({
                    articleKey,
                    pathname,
                    pageDescription,
                    hasArticle: Boolean(articleKey),
                    question: assistantQuestion,
                }),
                intent,
            })

            setAssistantAnswer(response.data?.answer || '')
        } catch (requestError) {
            console.error('Failed to ask contextual help assistant:', requestError)
            setAssistantError(
                requestError?.response?.data?.message
                || (isRTL
                    ? 'تعذر الحصول على رد الآن. حاول مرة أخرى بعد قليل.'
                    : 'Unable to get a response right now. Please try again shortly.')
            )
        } finally {
            setIsAskingAssistant(false)
        }
    }

    return (
        <>
            <div
                className={`fixed inset-0 z-40 bg-slate-950/40 transition-opacity duration-300 ${isOpen ? 'opacity-100 pointer-events-auto' : 'pointer-events-none opacity-0'}`}
                onClick={onClose}
            />

            <aside
                className={`fixed right-0 top-0 z-50 flex h-full w-full max-w-2xl transform flex-col border-l border-slate-200 bg-white shadow-2xl transition-transform duration-300 ${isOpen ? 'translate-x-0' : 'translate-x-full'}`}
                aria-hidden={!isOpen}
                style={{ direction: isRTL ? 'rtl' : 'ltr' }}
            >
                <div className="flex items-start justify-between gap-4 border-b border-slate-200 px-6 py-5">
                    <div className="min-w-0">
                        <p className="text-xs font-semibold uppercase tracking-[0.24em] text-indigo-500">
                            {t('common.helpCenter', 'Help Center')}
                        </p>
                        <h2 className="mt-1 text-xl font-bold text-slate-900">{resolvedTitle}</h2>
                        <p className="mt-2 text-sm text-slate-500">
                            {pathname}
                        </p>
                    </div>

                    <button
                        type="button"
                        onClick={onClose}
                        className="rounded-xl p-2 text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-800"
                        title={t('common.close', 'Close')}
                    >
                        <X className="h-5 w-5" />
                    </button>
                </div>

                <div className="flex-1 overflow-y-auto px-6 py-6">
                    {isLoading ? (
                        <div className="flex h-full min-h-48 flex-col items-center justify-center gap-3 text-slate-500">
                            <LoaderCircle className="h-6 w-6 animate-spin" />
                            <p className="text-sm">{t('common.loading', 'Loading...')}</p>
                        </div>
                    ) : (
                        <div className="space-y-4">
                            {error ? (
                                <div className="rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800">
                                    {error}
                                </div>
                            ) : null}

                            <div className="space-y-4 text-sm leading-7 text-slate-700">
                                <ReactMarkdown
                                    components={{
                                        h1: ({ children }) => <h1 className="text-2xl font-bold text-slate-900">{children}</h1>,
                                        h2: ({ children }) => <h2 className="pt-3 text-xl font-semibold text-slate-900">{children}</h2>,
                                        h3: ({ children }) => <h3 className="pt-2 text-base font-semibold text-slate-900">{children}</h3>,
                                        p: ({ children }) => <p className="text-sm leading-7 text-slate-700">{children}</p>,
                                        ul: ({ children }) => <ul className="list-disc space-y-2 ps-5">{children}</ul>,
                                        ol: ({ children }) => <ol className="list-decimal space-y-2 ps-5">{children}</ol>,
                                        li: ({ children }) => <li className="text-sm leading-7 text-slate-700">{children}</li>,
                                        hr: () => <hr className="border-slate-200" />,
                                        strong: ({ children }) => <strong className="font-semibold text-slate-900">{children}</strong>,
                                        code: ({ children }) => <code className="rounded bg-slate-100 px-1 py-0.5 text-[13px] text-slate-900">{children}</code>,
                                    }}
                                >
                                    {content}
                                </ReactMarkdown>
                            </div>

                            <section className="rounded-3xl border border-indigo-100 bg-gradient-to-br from-indigo-50 via-white to-slate-50 p-5 shadow-sm">
                                <div className="flex items-start gap-3">
                                    <div className="rounded-2xl bg-indigo-600 p-2 text-white shadow-sm">
                                        <Sparkles className="h-4 w-4" />
                                    </div>
                                    <div className="min-w-0 flex-1">
                                        <h3 className="text-base font-semibold text-slate-900">
                                            {isRTL ? 'اسأل المساعد' : 'Ask the assistant'}
                                        </h3>
                                        <p className="mt-1 text-sm text-slate-600">
                                            {isRTL
                                                ? 'الرد يعتمد فقط على المقال الحالي ومسار الصفحة المفتوحة.'
                                                : 'Answers use only the current help article and page context.'}
                                        </p>
                                    </div>
                                </div>

                                <form className="mt-4 space-y-3" onSubmit={handleAskAssistant}>
                                    <textarea
                                        value={assistantQuestion}
                                        onChange={(event) => setAssistantQuestion(event.target.value)}
                                        rows={3}
                                        maxLength={2000}
                                        className="w-full rounded-2xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-800 shadow-sm outline-none transition focus:border-indigo-400 focus:ring-2 focus:ring-indigo-100"
                                        placeholder={isRTL
                                            ? 'اكتب سؤالك عن هذه الصفحة أو المشكلة التي تواجهها'
                                            : 'Ask about this page or describe the issue you are facing'}
                                    />

                                    <div className="flex items-center justify-between gap-3">
                                        <p className="text-xs text-slate-500">
                                            {isRTL
                                                ? 'مثال: لماذا لا يظهر المساعد في المتجر؟'
                                                : 'Example: Why is the assistant not showing on the storefront?'}
                                        </p>

                                        <button
                                            type="submit"
                                            disabled={isAskingAssistant || !assistantQuestion.trim()}
                                            className="inline-flex items-center gap-2 rounded-xl bg-indigo-600 px-4 py-2 text-sm font-semibold text-white transition hover:bg-indigo-700 disabled:cursor-not-allowed disabled:opacity-60"
                                        >
                                            {isAskingAssistant ? <LoaderCircle className="h-4 w-4 animate-spin" /> : null}
                                            <span>{isRTL ? 'اسأل' : 'Ask'}</span>
                                        </button>
                                    </div>
                                </form>

                                {assistantError ? (
                                    <div className="mt-4 rounded-2xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-700">
                                        {assistantError}
                                    </div>
                                ) : null}

                                {assistantAnswer ? (
                                    <div className="mt-4 rounded-2xl border border-slate-200 bg-white px-4 py-4 shadow-sm">
                                        <p className="mb-2 text-xs font-semibold uppercase tracking-[0.2em] text-slate-400">
                                            {isRTL ? 'الرد' : 'Answer'}
                                        </p>
                                        <div className="whitespace-pre-wrap text-sm leading-7 text-slate-700">
                                            {assistantAnswer}
                                        </div>
                                    </div>
                                ) : null}
                            </section>
                        </div>
                    )}
                </div>
            </aside>
        </>
    )
}
