import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import * as Dialog from '@radix-ui/react-dialog'
import { Check, Copy, ExternalLink, Loader2, RefreshCw, ShieldAlert, Trash2, X } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useConfirmDialog } from '../common/confirm/ConfirmDialogProvider'
import { useRTL } from '../hooks/useRTL'
import adminProductFeedApi, { PRODUCT_FEED_CHANNELS } from '../services/adminProductFeedApi'
import { useToast } from './Toast'

const getTokenId = (token) => token?.tokenId || token?.TokenId
const getChannel = (token) => token?.channel || token?.Channel
const getRevokedAtUtc = (token) => token?.revokedAtUtc ?? token?.RevokedAtUtc ?? null
const getCreatedAtUtc = (token) => token?.createdAtUtc || token?.CreatedAtUtc
const getFeedUrl = (token) => token?.feedUrl || token?.FeedUrl

const emptyTokenState = () => PRODUCT_FEED_CHANNELS.reduce((acc, channel) => {
    acc[channel.id] = []
    return acc
}, {})

function getChannelState(tokens) {
    const safeTokens = Array.isArray(tokens) ? tokens : []
    const activeToken = safeTokens.find((token) => !getRevokedAtUtc(token))
    if (activeToken) {
        return { status: 'active', token: activeToken }
    }
    if (safeTokens.length > 0) {
        return { status: 'revoked', token: safeTokens[0] }
    }
    return { status: 'notConfigured', token: null }
}

function isNotFound(error) {
    return error?.response?.status === 404
}

function getSafeErrorKey(error) {
    if (error?.response?.status === 400) return 'productFeed.errors.invalidRequest'
    if (error?.response?.status === 401) return 'productFeed.errors.unauthorized'
    if (error?.response?.status === 403) return 'productFeed.errors.forbidden'
    if (error?.response?.status === 404) return 'productFeed.errors.notFound'
    if (error?.response?.status === 408) return 'productFeed.errors.timeout'
    if (error?.response?.status === 409) return 'productFeed.errors.conflict'
    if (error?.response?.status === 429) return 'productFeed.errors.rateLimited'
    if (error?.response?.status >= 500) return 'productFeed.errors.server'
    if (error?.code === 'ECONNABORTED' || error?.code === 'ETIMEDOUT') return 'productFeed.errors.timeout'
    if (error?.code === 'ERR_NETWORK') return 'productFeed.errors.network'
    return 'productFeed.errors.generic'
}

export default function ProductFeedDialog({
    open,
    onOpenChange,
    sessionFeedUrls = {},
    onSessionFeedUrlChange = () => {},
    restoreFocusRef = null,
}) {
    const { t } = useTranslation()
    const { dir } = useRTL()
    const { confirm } = useConfirmDialog()
    const { showError, showSuccess } = useToast()
    const [tokensByChannel, setTokensByChannel] = useState(emptyTokenState)
    const [selectedChannel, setSelectedChannel] = useState(PRODUCT_FEED_CHANNELS[0].id)
    const [loading, setLoading] = useState(false)
    const [loadErrorKey, setLoadErrorKey] = useState(null)
    const [operation, setOperation] = useState(null)
    const [confirming, setConfirming] = useState(false)
    const [oneTimeResult, setOneTimeResult] = useState(null)
    const requestIdRef = useRef(0)
    const operationLockRef = useRef(false)
    const confirmationLockRef = useRef(false)
    const channelTabRefs = useRef({})

    const channelStates = useMemo(() => PRODUCT_FEED_CHANNELS.reduce((acc, channel) => {
        acc[channel.id] = getChannelState(tokensByChannel[channel.id])
        return acc
    }, {}), [tokensByChannel])

    const selectedConfig = PRODUCT_FEED_CHANNELS.find((channel) => channel.id === selectedChannel) || PRODUCT_FEED_CHANNELS[0]
    const selectedState = channelStates[selectedChannel] || { status: 'notConfigured', token: null }
    const selectedSessionFeedUrl = sessionFeedUrls[selectedChannel]
    const showingOneTimeResult = oneTimeResult?.channel === selectedChannel
    const busy = Boolean(operation) || confirming

    const clearSecret = useCallback(() => {
        setOneTimeResult(null)
    }, [])

    const loadTokens = useCallback(async () => {
        const requestId = ++requestIdRef.current
        setLoading(true)
        setLoadErrorKey(null)
        clearSecret()

        try {
            const results = await Promise.all(
                PRODUCT_FEED_CHANNELS.map(async (channel) => [
                    channel.id,
                    await adminProductFeedApi.listTokens(channel.id),
                ]),
            )

            if (requestId !== requestIdRef.current) return false

            setTokensByChannel(results.reduce((acc, [channel, tokens]) => {
                acc[channel] = tokens
                return acc
            }, emptyTokenState()))
            return true
        } catch (error) {
            if (requestId === requestIdRef.current) {
                const safeErrorKey = getSafeErrorKey(error)
                setLoadErrorKey(safeErrorKey)
                showError(t(safeErrorKey))
            }
            return false
        } finally {
            if (requestId === requestIdRef.current) {
                setLoading(false)
            }
        }
    }, [clearSecret, showError, t])

    useEffect(() => {
        if (open) {
            void loadTokens()
            return
        }

        requestIdRef.current += 1
        setLoading(false)
        setOperation(null)
        setConfirming(false)
        operationLockRef.current = false
        confirmationLockRef.current = false
        clearSecret()
    }, [clearSecret, loadTokens, open])

    useEffect(() => () => {
        requestIdRef.current += 1
        operationLockRef.current = false
        confirmationLockRef.current = false
        clearSecret()
    }, [clearSecret])

    const beginOperation = (nextOperation) => {
        if (operationLockRef.current) {
            return false
        }

        operationLockRef.current = true
        setOperation(nextOperation)
        return true
    }

    const endOperation = () => {
        operationLockRef.current = false
        setOperation(null)
    }

    const refreshAfterMissingToken = async () => {
        const refreshed = await loadTokens()
        if (refreshed) {
            showError(t('productFeed.errors.notFoundRefresh'))
        }
    }

    const handleOpenChange = (nextOpen) => {
        if (!nextOpen && (busy || confirmationLockRef.current)) return
        if (!nextOpen) {
            clearSecret()
        }
        onOpenChange(nextOpen)
    }

    const revealOneTimeUrl = (channel, result, action) => {
        const feedUrl = getFeedUrl(result)
        if (!feedUrl) {
            showError(t('productFeed.errors.missingFeedUrl'))
            return
        }

        onSessionFeedUrlChange(channel, feedUrl)

        setOneTimeResult({
            action,
            channel,
            feedUrl,
            tokenId: getTokenId(result),
        })
    }

    const selectChannel = useCallback((channel) => {
        clearSecret()
        setSelectedChannel(channel)
    }, [clearSecret])

    const handleChannelKeyDown = (event, channelIndex) => {
        const lastIndex = PRODUCT_FEED_CHANNELS.length - 1
        let nextIndex = channelIndex

        if (event.key === 'Home') nextIndex = 0
        else if (event.key === 'End') nextIndex = lastIndex
        else if (event.key === 'ArrowRight' || event.key === 'ArrowDown') nextIndex = channelIndex === lastIndex ? 0 : channelIndex + 1
        else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') nextIndex = channelIndex === 0 ? lastIndex : channelIndex - 1
        else return

        event.preventDefault()
        const nextChannel = PRODUCT_FEED_CHANNELS[nextIndex].id
        selectChannel(nextChannel)
        channelTabRefs.current[nextChannel]?.focus()
    }

    const handleCreate = async () => {
        if (!beginOperation({ type: 'create', channel: selectedChannel })) return
        clearSecret()

        try {
            const result = await adminProductFeedApi.createToken(selectedChannel)
            setTokensByChannel((current) => ({
                ...current,
                [selectedChannel]: [{
                    tokenId: getTokenId(result),
                    channel: getChannel(result) || selectedChannel,
                    createdAtUtc: getCreatedAtUtc(result),
                    revokedAtUtc: null,
                }],
            }))
            // A successful create is authoritative even if its response is
            // malformed. Remove any stale URL before accepting the one-time
            // replacement so Copy/Open can never target an older credential.
            onSessionFeedUrlChange(selectedChannel, null)
            revealOneTimeUrl(selectedChannel, result, 'create')
            showSuccess(t('productFeed.feedCreated'))
        } catch (error) {
            showError(t(getSafeErrorKey(error)))
        } finally {
            endOperation()
        }
    }

    const handleRotate = async () => {
        if (operationLockRef.current || confirmationLockRef.current || !selectedState.token) return

        confirmationLockRef.current = true
        setConfirming(true)
        const confirmed = await confirm({
            title: t('productFeed.rotateTitle', { channel: selectedConfig.name }),
            description: t('productFeed.rotateDescription'),
            confirmLabel: t('productFeed.rotateUrl'),
            confirmTone: 'warning',
        }).finally(() => {
            confirmationLockRef.current = false
            setConfirming(false)
        })
        if (!confirmed) return

        if (!beginOperation({ type: 'rotate', channel: selectedChannel })) return
        clearSecret()

        try {
            const result = await adminProductFeedApi.rotateToken(selectedChannel, getTokenId(selectedState.token))
            setTokensByChannel((current) => ({
                ...current,
                [selectedChannel]: [{
                    tokenId: getTokenId(result),
                    channel: getChannel(result) || selectedChannel,
                    createdAtUtc: getCreatedAtUtc(result),
                    revokedAtUtc: null,
                }],
            }))
            // Rotation has already invalidated the previous credential. Clear
            // it before processing the response so a missing feedUrl cannot
            // leave stale Copy/Open actions wired to the old URL.
            onSessionFeedUrlChange(selectedChannel, null)
            revealOneTimeUrl(selectedChannel, result, 'rotate')
            showSuccess(t('productFeed.feedRotated'))
        } catch (error) {
            if (isNotFound(error)) {
                onSessionFeedUrlChange(selectedChannel, null)
                await refreshAfterMissingToken()
            } else {
                showError(t(getSafeErrorKey(error)))
            }
        } finally {
            endOperation()
        }
    }

    const handleRevoke = async () => {
        if (operationLockRef.current || confirmationLockRef.current || !selectedState.token) return

        confirmationLockRef.current = true
        setConfirming(true)
        const confirmed = await confirm({
            title: t('productFeed.revokeTitle', { channel: selectedConfig.name }),
            description: t('productFeed.revokeDescription'),
            confirmLabel: t('productFeed.revokeFeed'),
            confirmTone: 'danger',
        }).finally(() => {
            confirmationLockRef.current = false
            setConfirming(false)
        })
        if (!confirmed) return

        if (!beginOperation({ type: 'revoke', channel: selectedChannel })) return
        clearSecret()

        try {
            await adminProductFeedApi.revokeToken(selectedChannel, getTokenId(selectedState.token))
            onSessionFeedUrlChange(selectedChannel, null)
            await loadTokens()
            showSuccess(t('productFeed.feedRevoked'))
        } catch (error) {
            if (isNotFound(error)) {
                onSessionFeedUrlChange(selectedChannel, null)
                await refreshAfterMissingToken()
            } else {
                showError(t(getSafeErrorKey(error)))
            }
        } finally {
            endOperation()
        }
    }

    const copyFeedUrl = async (feedUrl) => {
        if (!feedUrl) return

        try {
            await navigator.clipboard.writeText(feedUrl)
            showSuccess(t('productFeed.copied'))
        } catch {
            showError(t('productFeed.copyFailed'))
        }
    }

    const handleCopyOneTimeUrl = async () => {
        await copyFeedUrl(oneTimeResult?.feedUrl)
    }

    const handleCopySessionUrl = async () => {
        await copyFeedUrl(sessionFeedUrls[selectedChannel])
    }

    const openFeedUrl = (feedUrl) => {
        if (!feedUrl) return
        window.open(feedUrl, '_blank', 'noopener,noreferrer')
    }

    const handleOpenOneTimeUrl = () => {
        openFeedUrl(oneTimeResult?.feedUrl)
    }

    const handleOpenSessionUrl = () => {
        openFeedUrl(sessionFeedUrls[selectedChannel])
    }

    const statusLabelKey = {
        active: 'productFeed.status.active',
        notConfigured: 'productFeed.status.notConfigured',
        revoked: 'productFeed.status.revoked',
    }[selectedState.status]

    return (
        <Dialog.Root open={open} onOpenChange={handleOpenChange}>
            <Dialog.Portal>
                <Dialog.Overlay className="fixed inset-0 z-50 bg-slate-950/50" />
                <Dialog.Content
                    dir={dir}
                    onCloseAutoFocus={(event) => {
                        if (!restoreFocusRef?.current) return
                        event.preventDefault()
                        restoreFocusRef.current.focus()
                    }}
                    className="fixed inset-0 z-50 m-auto flex h-[min(760px,calc(100vh-2rem))] w-[min(960px,calc(100vw-2rem))] flex-col overflow-hidden rounded-lg bg-white shadow-2xl focus:outline-none"
                >
                    <div className="flex items-start justify-between gap-4 border-b border-gray-200 px-5 py-4">
                        <div>
                            <Dialog.Title className="text-xl font-semibold text-gray-900">
                                {t('productFeed.title')}
                            </Dialog.Title>
                            <Dialog.Description className="mt-1 text-sm text-gray-600">
                                {t('productFeed.description')}
                            </Dialog.Description>
                        </div>
                        <Dialog.Close
                            className="rounded-md p-2 text-gray-400 transition hover:bg-gray-100 hover:text-gray-700 focus:outline-none focus:ring-2 focus:ring-primary-500"
                            aria-label={t('common.close')}
                            disabled={busy}
                        >
                            <X className="h-5 w-5" aria-hidden="true" />
                        </Dialog.Close>
                    </div>

                    <div className="flex-1 overflow-y-auto px-5 py-5">
                        {loading ? (
                            <div className="space-y-4" role="status" aria-live="polite">
                                <div className="h-20 animate-pulse rounded-lg bg-gray-100" />
                                <div className="h-52 animate-pulse rounded-lg bg-gray-100" />
                                <span className="sr-only">{t('productFeed.loadingChannels')}</span>
                            </div>
                        ) : loadErrorKey ? (
                            <div className="rounded-lg border border-red-200 bg-red-50 p-4" role="alert">
                                <p className="text-sm text-red-800">{t(loadErrorKey)}</p>
                                <button
                                    type="button"
                                    onClick={() => void loadTokens()}
                                    className="mt-3 rounded-lg border border-red-300 px-4 py-2 text-sm font-medium text-red-800 hover:bg-red-100 focus:outline-none focus:ring-2 focus:ring-red-500"
                                >
                                    {t('common.retry')}
                                </button>
                            </div>
                        ) : (
                            <div className="grid gap-5 lg:grid-cols-[280px_minmax(0,1fr)]">
                                <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-1" role="tablist" aria-label={t('productFeed.channelSelectorLabel')}>
                                    {PRODUCT_FEED_CHANNELS.map((channel, channelIndex) => {
                                        const state = channelStates[channel.id]
                                        const isSelected = selectedChannel === channel.id
                                        return (
                                            <button
                                                key={channel.id}
                                                type="button"
                                                role="tab"
                                                id={`product-feed-tab-${channel.id}`}
                                                ref={(element) => {
                                                    channelTabRefs.current[channel.id] = element
                                                }}
                                                aria-selected={isSelected}
                                                aria-controls={`product-feed-panel-${channel.id}`}
                                                tabIndex={isSelected ? 0 : -1}
                                                disabled={busy || loading}
                                                onClick={() => selectChannel(channel.id)}
                                                onKeyDown={(event) => handleChannelKeyDown(event, channelIndex)}
                                                className={`rounded-lg border px-4 py-3 text-start transition focus:outline-none focus:ring-2 focus:ring-primary-500 ${
                                                    isSelected
                                                        ? 'border-primary-500 bg-primary-50'
                                                        : 'border-gray-200 bg-white hover:border-gray-300 hover:bg-gray-50'
                                                } disabled:cursor-not-allowed disabled:opacity-60`}
                                            >
                                                <span className="block text-sm font-semibold text-gray-900">{channel.name}</span>
                                                <span className="mt-2 flex items-center gap-2 text-xs font-medium text-gray-600">
                                                    <span
                                                        aria-hidden="true"
                                                        className={`h-2.5 w-2.5 rounded-full ${
                                                            state.status === 'active'
                                                                ? 'bg-emerald-500'
                                                                : state.status === 'revoked'
                                                                    ? 'bg-red-500'
                                                                    : 'bg-gray-300'
                                                        }`}
                                                    />
                                                    {t({
                                                        active: 'productFeed.status.active',
                                                        notConfigured: 'productFeed.status.notConfigured',
                                                        revoked: 'productFeed.status.revoked',
                                                    }[state.status])}
                                                </span>
                                            </button>
                                        )
                                    })}
                                </div>

                                <section
                                    id={`product-feed-panel-${selectedChannel}`}
                                    role="tabpanel"
                                    aria-labelledby={`product-feed-tab-${selectedChannel}`}
                                    className="min-w-0 rounded-lg border border-gray-200 bg-white p-4"
                                >
                                    <div className="flex flex-wrap items-center justify-between gap-3">
                                        <div>
                                            <h3 className="text-lg font-semibold text-gray-900">{selectedConfig.name}</h3>
                                            <p className="mt-1 text-sm text-gray-600">{t('productFeed.channelHelp')}</p>
                                            <p className="mt-1 text-xs text-gray-500">{t('productFeed.eligibilityNote')}</p>
                                        </div>
                                        <span className="inline-flex items-center gap-2 rounded-full bg-gray-100 px-3 py-1 text-xs font-semibold text-gray-700">
                                            <span
                                                aria-hidden="true"
                                                className={`h-2 w-2 rounded-full ${
                                                    selectedState.status === 'active'
                                                        ? 'bg-emerald-500'
                                                        : selectedState.status === 'revoked'
                                                            ? 'bg-red-500'
                                                            : 'bg-gray-400'
                                                }`}
                                            />
                                            {t(statusLabelKey)}
                                        </span>
                                    </div>

                                    {showingOneTimeResult ? (
                                        <div className="mt-5 rounded-lg border border-amber-200 bg-amber-50 p-4" role="status" aria-live="polite">
                                            <div className="flex items-start gap-3">
                                                <ShieldAlert className="h-5 w-5 shrink-0 text-amber-700" aria-hidden="true" />
                                                <div className="min-w-0">
                                                    <p className="font-semibold text-amber-900">
                                                        {t('productFeed.oneTimeTitle')}
                                                    </p>
                                                    <p className="mt-1 text-sm text-amber-900">
                                                        {t('productFeed.oneTimeDescription')}
                                                    </p>
                                                    <p
                                                        className="mt-3 break-all rounded-md border border-amber-200 bg-white px-3 py-2 font-mono text-sm text-gray-900"
                                                        aria-label={t('productFeed.privateUrlLabel')}
                                                    >
                                                        {oneTimeResult.feedUrl}
                                                    </p>
                                                    <div className="mt-4 flex flex-wrap gap-2">
                                                        <button
                                                            type="button"
                                                            onClick={handleCopyOneTimeUrl}
                                                            className="btn-primary inline-flex items-center gap-2"
                                                            aria-label={t('productFeed.copyUrl')}
                                                        >
                                                            <Copy className="h-4 w-4" aria-hidden="true" />
                                                            {t('productFeed.copyUrl')}
                                                        </button>
                                                        <button
                                                            type="button"
                                                            onClick={handleOpenOneTimeUrl}
                                                            className="inline-flex items-center gap-2 rounded-lg border border-amber-300 px-4 py-2 text-sm font-medium text-amber-900 hover:bg-amber-100 focus:outline-none focus:ring-2 focus:ring-amber-500"
                                                            aria-label={t('productFeed.openFeedAria', { channel: selectedConfig.name })}
                                                        >
                                                            <ExternalLink className="h-4 w-4" aria-hidden="true" />
                                                            {t('productFeed.openFeed')}
                                                        </button>
                                                        <button
                                                            type="button"
                                                            onClick={clearSecret}
                                                            className="rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-primary-500"
                                                        >
                                                            {t('productFeed.done')}
                                                        </button>
                                                    </div>
                                                </div>
                                            </div>
                                        </div>
                                    ) : null}

                                    {selectedState.status === 'active' ? (
                                        <div className="mt-5 space-y-4">
                                            <div>
                                                <div className="text-sm font-medium text-gray-700">{t('productFeed.feedUrl')}</div>
                                                <div className="mt-2 flex min-w-0 items-center gap-2 rounded-lg border border-gray-200 bg-gray-50 px-3 py-3">
                                                    <ExternalLink className="h-4 w-4 shrink-0 text-gray-400" aria-hidden="true" />
                                                    <code className="min-w-0 break-all text-sm text-gray-700">
                                                        {t('productFeed.maskedUrl', { channel: selectedChannel })}
                                                    </code>
                                                </div>
                                                {selectedSessionFeedUrl && !showingOneTimeResult ? (
                                                    <div className="mt-3 flex flex-wrap gap-2">
                                                        <button
                                                            type="button"
                                                            onClick={handleCopySessionUrl}
                                                            disabled={busy}
                                                            className="inline-flex items-center gap-2 rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-60 focus:outline-none focus:ring-2 focus:ring-primary-500"
                                                            aria-label={t('productFeed.copyUrl')}
                                                        >
                                                            <Copy className="h-4 w-4" aria-hidden="true" />
                                                            {t('productFeed.copyUrl')}
                                                        </button>
                                                        <button
                                                            type="button"
                                                            onClick={handleOpenSessionUrl}
                                                            disabled={busy}
                                                            className="inline-flex items-center gap-2 rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-60 focus:outline-none focus:ring-2 focus:ring-primary-500"
                                                            aria-label={t('productFeed.openFeedAria', { channel: selectedConfig.name })}
                                                        >
                                                            <ExternalLink className="h-4 w-4" aria-hidden="true" />
                                                            {t('productFeed.openFeed')}
                                                        </button>
                                                    </div>
                                                ) : showingOneTimeResult ? (
                                                    <p className="mt-2 text-xs text-gray-500">{t('productFeed.maskedUrlHelp')}</p>
                                                ) : (
                                                    <p className="mt-2 text-xs text-gray-500">{t('productFeed.sessionUrlUnavailable')}</p>
                                                )}
                                            </div>

                                            <div className="flex flex-wrap gap-2">
                                                <button
                                                    type="button"
                                                    onClick={handleRotate}
                                                    disabled={busy}
                                                    className="inline-flex items-center gap-2 rounded-lg border border-amber-300 px-4 py-2 text-sm font-medium text-amber-800 hover:bg-amber-50 disabled:cursor-not-allowed disabled:opacity-60 focus:outline-none focus:ring-2 focus:ring-amber-500"
                                                    aria-label={t('productFeed.rotateAria', { channel: selectedConfig.name })}
                                                >
                                                    {operation?.type === 'rotate' ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <RefreshCw className="h-4 w-4" aria-hidden="true" />}
                                                    {operation?.type === 'rotate' ? t('productFeed.rotating') : t('productFeed.rotateUrl')}
                                                </button>
                                                <button
                                                    type="button"
                                                    onClick={handleRevoke}
                                                    disabled={busy}
                                                    className="inline-flex items-center gap-2 rounded-lg border border-red-300 px-4 py-2 text-sm font-medium text-red-700 hover:bg-red-50 disabled:cursor-not-allowed disabled:opacity-60 focus:outline-none focus:ring-2 focus:ring-red-500"
                                                    aria-label={t('productFeed.revokeAria', { channel: selectedConfig.name })}
                                                >
                                                    {operation?.type === 'revoke' ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <Trash2 className="h-4 w-4" aria-hidden="true" />}
                                                    {operation?.type === 'revoke' ? t('productFeed.revoking') : t('productFeed.revokeFeed')}
                                                </button>
                                            </div>
                                        </div>
                                    ) : (
                                        <div className="mt-5 rounded-lg border border-dashed border-gray-300 p-5">
                                            <p className="text-sm text-gray-600">{t('productFeed.createHelp', { channel: selectedConfig.name })}</p>
                                            <button
                                                type="button"
                                                onClick={handleCreate}
                                                disabled={busy}
                                                className="btn-primary mt-4 inline-flex items-center gap-2"
                                                aria-label={t('productFeed.createAria', { channel: selectedConfig.name })}
                                            >
                                                {operation?.type === 'create' ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : <Check className="h-4 w-4" aria-hidden="true" />}
                                                {operation?.type === 'create' ? t('productFeed.creating') : t('productFeed.createFeed')}
                                            </button>
                                        </div>
                                    )}
                                </section>
                            </div>
                        )}
                    </div>
                </Dialog.Content>
            </Dialog.Portal>
        </Dialog.Root>
    )
}
