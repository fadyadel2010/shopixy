import { useCallback, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import ProductForm from '../components/ProductForm'
import ProductWorkspaceLayout from '../components/ProductWorkspaceLayout'
import AiProductGeneratorModal from '../components/AiProductGeneratorModal'
import { useToast } from '../components/Toast'

const STOREFRONT_DOMAIN = import.meta.env.VITE_STOREFRONT_DOMAIN || 'localhost:5174'
const PRODUCT_WORKSPACE_FORM_ID = 'product-workspace-form'

function getWorkspaceStatus(meta, t) {
    if (meta.publishStatus === 2) {
        return {
            label: t('products.statusArchived') || 'Archived',
            className: 'bg-gray-100 text-gray-700'
        }
    }

    if (meta.isActive && meta.publishStatus === 1) {
        return {
            label: t('products.active') || 'Active',
            className: 'bg-emerald-100 text-emerald-700'
        }
    }

    return {
        label: t('products.statusDraft') || 'Draft',
        className: 'bg-amber-100 text-amber-700'
    }
}

export default function ProductWorkspacePage() {
    const { productId } = useParams()
    const navigate = useNavigate()
    const { t } = useTranslation()
    const productFormRef = useRef(null)
    const { showError, showSuccess } = useToast()
    const isCreateMode = productId === 'new'
    const resolvedProductId = isCreateMode ? null : productId
    const [isAiModalOpen, setIsAiModalOpen] = useState(false)

    const [workspaceMeta, setWorkspaceMeta] = useState({
        name: '',
        isActive: true,
        publishStatus: 1,
        isOnlineVisible: true,
        slug: '',
        loading: false
    })

    const handleMetaChange = useCallback((nextMeta) => {
        setWorkspaceMeta(prev => ({ ...prev, ...nextMeta }))
    }, [])

    const handleBack = useCallback(() => {
        navigate('/products')
    }, [navigate])

    const handleSave = useCallback((savedProduct) => {
        showSuccess(t('common.updatedSuccessfully') || 'Saved successfully')

        if (isCreateMode && savedProduct?.id) {
            navigate(`/products/${savedProduct.id}/edit`, { replace: true })
        }
    }, [isCreateMode, navigate, showSuccess, t])

    const handleApplyAiProduct = useCallback((generatedProduct) => {
        const applyResult = productFormRef.current?.applyAiGeneratedData?.(generatedProduct)

        if (!applyResult) {
            showError('Unable to apply the generated product data to the form.')
            return false
        }

        if (applyResult.appliedFieldCount > 0) {
            showSuccess(
                applyResult.skippedFieldCount > 0
                    ? 'AI content applied to empty fields. Existing values were left unchanged.'
                    : 'AI content applied to the product form.'
            )
            return true
        }

        showError('All matching product fields already have values, so nothing was changed.')
        return false
    }, [showError, showSuccess])

    const status = useMemo(() => getWorkspaceStatus(workspaceMeta, t), [workspaceMeta, t])
    const title = workspaceMeta.name?.trim()
        || (isCreateMode ? (t('products.addProduct') || 'Add Product') : (t('products.editProduct') || 'Edit Product'))

    const previewHref = workspaceMeta.isOnlineVisible && workspaceMeta.slug
        ? `http://${STOREFRONT_DOMAIN}/products/${workspaceMeta.slug}`
        : null

    const topBar = (
        <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
            <div className="min-w-0">
                <div className="mb-2 flex items-center gap-3">
                    <button
                        type="button"
                        onClick={handleBack}
                        className="inline-flex items-center gap-2 rounded-lg border border-gray-200 px-3 py-2 text-sm font-medium text-gray-600 transition hover:border-gray-300 hover:bg-gray-50 hover:text-gray-900"
                    >
                        <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
                        </svg>
                        {t('products.cancel') || 'Back'}
                    </button>
                    <span className={`inline-flex items-center rounded-full px-3 py-1 text-xs font-semibold ${status.className}`}>
                        {status.label}
                    </span>
                </div>

                <h1 className="truncate text-2xl font-semibold text-gray-900">{title}</h1>
                <p className="mt-1 text-sm text-gray-500">
                    {isCreateMode
                        ? (t('products.addProduct') || 'Create a new product in the workspace')
                        : (t('products.editProduct') || 'Manage product details, media, storefront settings, and advanced controls')}
                </p>
            </div>

            <div className="flex flex-wrap items-center gap-3">
                <button
                    type="button"
                    onClick={() => setIsAiModalOpen(true)}
                    className="inline-flex items-center gap-2 rounded-lg border border-amber-200 bg-amber-50 px-4 py-2 text-sm font-semibold text-amber-900 transition hover:border-amber-300 hover:bg-amber-100"
                >
                    ✨ Create Product with AI
                </button>

                {previewHref && (
                    <a
                        href={previewHref}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="inline-flex items-center gap-2 rounded-lg border border-gray-200 px-4 py-2 text-sm font-medium text-gray-700 transition hover:border-gray-300 hover:bg-gray-50 hover:text-gray-900"
                    >
                        <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 3h6m0 0v6m0-6L10 14" />
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 7v12a2 2 0 002 2h12a2 2 0 002-2v-5" />
                        </svg>
                        {t('products.preview') || 'Preview'}
                    </a>
                )}

                <button
                    type="submit"
                    form={PRODUCT_WORKSPACE_FORM_ID}
                    disabled={workspaceMeta.loading}
                    className="inline-flex items-center gap-2 rounded-lg bg-primary-600 px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:bg-primary-700"
                >
                    <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                    </svg>
                    {workspaceMeta.loading
                        ? (t('products.saving') || 'Saving')
                        : resolvedProductId
                            ? (t('products.update') || 'Save')
                            : (t('products.create') || 'Save')}
                </button>
            </div>
        </div>
    )

    return (
        <ProductWorkspaceLayout topBar={topBar}>
            <ProductForm
                ref={productFormRef}
                productId={resolvedProductId}
                formId={PRODUCT_WORKSPACE_FORM_ID}
                mode="workspace"
                hideActions
                onClose={handleBack}
                onSave={handleSave}
                onMetaChange={handleMetaChange}
            />
            <AiProductGeneratorModal
                isOpen={isAiModalOpen}
                onClose={() => setIsAiModalOpen(false)}
                onApply={handleApplyAiProduct}
            />
        </ProductWorkspaceLayout>
    )
}
