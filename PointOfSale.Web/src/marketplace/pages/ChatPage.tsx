import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useRequestDetails } from '../hooks/useRequestDetails'
import { useChat } from '../hooks/useChat'
import { ChatMessage } from '../components/ChatMessage'
import { useAuthStore } from '../../store/authStore'
import { useFormatting } from '../../hooks/useFormatting'
import { deriveServiceRequestDetailsLifecyclePresentation } from '../status'
import { deriveServiceRequestContractPresentation } from '../contractPresentation'
import { deriveCompletionTimelineAuditPresentation } from '../completionTimelineAuthority'
import { deriveChatIdentityAuthority } from '../chatAuthority'
import { deriveChatAttachmentUploadAuthority } from '../attachmentAuthority'
import { deriveServiceRequestCompletionStatePresentation } from '../completionStateAuthority'
import { deriveServiceRequestRatingStatePresentation } from '../ratingStateAuthority'
import { RatingStateBadge } from '../components/RatingStateBadge'
import type { ChatMessage as ChatMessageType } from '../types'
import { useChatAttachmentUpload } from '../hooks/useChatAttachmentUpload'

export function ChatPage() {
    const { t } = useTranslation()
    const { formatCurrency, formatDate, formatNumber, formatTime, dir, isRTL } = useFormatting()
    const { requestId } = useParams<{ requestId: string }>()
    const navigate = useNavigate()
    const { user } = useAuthStore()
    const {
        data: request,
        isLoading: isLoadingRequest,
        isFetching: isFetchingRequest,
        isError: requestAuthorityFailed,
    } = useRequestDetails(requestId!)
    const currentUserId = user?.id ?? user?.userId
    const lifecycleCandidate = request ? deriveServiceRequestDetailsLifecyclePresentation(request) : null
    const completionState = request
        ? deriveServiceRequestCompletionStatePresentation(request, lifecycleCandidate, currentUserId)
        : null
    const lifecycle = completionState?.lifecycle ?? null
    const ratingState = request
        ? deriveServiceRequestRatingStatePresentation(request, lifecycle, currentUserId)
        : null
    const hasCurrentRequestAuthority = !!request
        && !isLoadingRequest
        && !isFetchingRequest
        && !requestAuthorityFailed
    const chatIdentity = hasCurrentRequestAuthority && request
        ? deriveChatIdentityAuthority(request, requestId, currentUserId, lifecycle)
        : null
    const contract = request ? deriveServiceRequestContractPresentation(request, lifecycle) : null
    const auditTimeline = hasCurrentRequestAuthority && request
        ? deriveCompletionTimelineAuditPresentation(request, completionState, currentUserId)
        : null
    const completionEvidence = hasCurrentRequestAuthority
        ? completionState?.completionEvidence ?? null
        : null
    const canViewChatHistory = chatIdentity?.canViewChatHistory ?? false
    const canSendChat = chatIdentity?.canSendChat ?? false
    const uploadChatAttachment = useChatAttachmentUpload(chatIdentity)
    const {
        messages,
        isLoading: isLoadingMessages,
        isLoadingOlder,
        hasOlderMessages,
        authorityFailed: chatAuthorityFailed,
        isConnected,
        canSend,
        loadOlderMessages,
        sendMessage,
    } = useChat(chatIdentity)

    const [messageBody, setMessageBody] = useState('')
    const [isSending, setIsSending] = useState(false)
    const [selectedFile, setSelectedFile] = useState<File | null>(null)
    const [previewUrl, setPreviewUrl] = useState<string | null>(null)
    const messagesEndRef = useRef<HTMLDivElement>(null)
    const messagesContainerRef = useRef<HTMLDivElement>(null)
    const newestMessageIdRef = useRef<string | null>(null)
    const fileInputRef = useRef<HTMLInputElement>(null)
    const sendInFlightRef = useRef(false)
    const sendOperationIdRef = useRef(0)
    const attachmentIntentScopeRef = useRef('')

    useEffect(() => {
        const newestMessageId = messages[messages.length - 1]?.id ?? null
        const previousNewestMessageId = newestMessageIdRef.current
        if (newestMessageId !== previousNewestMessageId) {
            newestMessageIdRef.current = newestMessageId
            messagesEndRef.current?.scrollIntoView({
                behavior: previousNewestMessageId === null ? 'auto' : 'smooth',
            })
        }
    }, [messages])

    useLayoutEffect(() => {
        const nextScope = chatIdentity?.canSendChat
            ? `${chatIdentity.requestId}:${chatIdentity.currentUserId}`
            : ''

        if (attachmentIntentScopeRef.current !== nextScope) {
            attachmentIntentScopeRef.current = nextScope
            sendOperationIdRef.current += 1
            sendInFlightRef.current = false
            setIsSending(false)
            setMessageBody('')
            setSelectedFile(null)
            setPreviewUrl(null)
            if (fileInputRef.current) {
                fileInputRef.current.value = ''
            }
        }
    }, [chatIdentity?.requestId, chatIdentity?.currentUserId, chatIdentity?.canSendChat])

    useEffect(() => {
        if (!selectedFile) {
            setPreviewUrl(null)
            return
        }

        if (selectedFile.type.startsWith('image/')) {
            const objectUrl = URL.createObjectURL(selectedFile)
            setPreviewUrl(objectUrl)

            return () => {
                URL.revokeObjectURL(objectUrl)
            }
        } else {
            setPreviewUrl(null)
        }
    }, [selectedFile])

    const handleFileSelect = (event: React.ChangeEvent<HTMLInputElement>) => {
        const file = event.target.files?.[0]
        if (file) {
            if (file.size > 10 * 1024 * 1024) {
                window.alert(t('servicesMarketplace.chat.fileSizeError'))
                return
            }
            setSelectedFile(file)
        }
    }

    const handleRemoveFile = () => {
        setSelectedFile(null)
        setPreviewUrl(null)
        if (fileInputRef.current) {
            fileInputRef.current.value = ''
        }
    }

    const handleLoadOlder = async () => {
        const container = messagesContainerRef.current
        if (!container || isLoadingOlder || !hasOlderMessages) return

        const previousScrollHeight = container.scrollHeight
        const previousScrollTop = container.scrollTop
        await loadOlderMessages()

        window.requestAnimationFrame(() => {
            const currentContainer = messagesContainerRef.current
            if (!currentContainer) return
            currentContainer.scrollTop = previousScrollTop
                + (currentContainer.scrollHeight - previousScrollHeight)
        })
    }

    const handleSend = async (event: React.FormEvent) => {
        event.preventDefault()

        if (
            sendInFlightRef.current
            || (!messageBody.trim() && !selectedFile)
            || !canSend
        ) return

        sendInFlightRef.current = true
        const sendOperationId = ++sendOperationIdRef.current
        setIsSending(true)
        try {
            let attachmentObjectKey: string | undefined
            const sendIdentity = chatIdentity

            if (!sendIdentity?.canSendChat) {
                throw new Error('Current chat attachment authority is unavailable')
            }

            if (selectedFile) {
                const payload = await uploadChatAttachment(sendIdentity.requestId, selectedFile)
                const upload = deriveChatAttachmentUploadAuthority(payload, sendIdentity)
                if (!upload) {
                    throw new Error(t('servicesMarketplace.chat.uploadFailed'))
                }

                attachmentObjectKey = upload.objectKey
            }

            await sendMessage(messageBody, attachmentObjectKey)
            if (sendOperationIdRef.current === sendOperationId) {
                setMessageBody('')
                handleRemoveFile()
            }
        } catch (error) {
            console.error('Failed to send message:', error)
            if (sendOperationIdRef.current === sendOperationId) {
                window.alert(t('servicesMarketplace.chat.sendFailed'))
            }
        } finally {
            if (sendOperationIdRef.current === sendOperationId) {
                sendInFlightRef.current = false
                setIsSending(false)
            }
        }
    }

    if (isLoadingRequest || isFetchingRequest) {
        return (
            <div>
                <div className="h-8 bg-gray-200 rounded w-1/3 mb-4 animate-pulse" />
                <div className="card animate-pulse">
                    <div className="h-4 bg-gray-200 rounded w-full mb-3" />
                    <div className="h-4 bg-gray-200 rounded w-2/3" />
                </div>
            </div>
        )
    }

    if (!request || !lifecycle || requestAuthorityFailed) {
        return (
            <div className="card text-center py-12" dir={isRTL ? 'rtl' : 'ltr'}>
                <h3 className="text-lg font-semibold text-gray-700 mb-2">{t('servicesMarketplace.chat.requestNotFound')}</h3>
                <button onClick={() => navigate('/marketplace?tab=my-requests')} className="btn-primary mt-4">
                    {t('servicesMarketplace.chat.backToRequests')}
                </button>
            </div>
        )
    }

    if (!canViewChatHistory || !chatIdentity) {
        return (
            <div dir={isRTL ? 'rtl' : 'ltr'}>
                <button
                    onClick={() => navigate(-1)}
                    className="flex items-center gap-1 text-sm text-gray-500 hover:text-gray-700 mb-4 transition-colors"
                >
                    {t('servicesMarketplace.chat.back')}
                </button>
                <div className="card text-center py-12">
                    <h3 className="text-lg font-semibold text-gray-700 mb-2">{t('servicesMarketplace.chat.unavailableTitle')}</h3>
                    <p className="text-gray-500">{t('servicesMarketplace.chat.unavailableDescription')}</p>
                </div>
            </div>
        )
    }

    return (
        <div className="flex flex-col" dir={isRTL ? 'rtl' : 'ltr'} style={{ height: 'calc(100vh - 140px)' }}>
            <div className="card mb-4 flex-shrink-0">
                <div className="flex items-center justify-between">
                    <div className={`flex items-center gap-3 ${isRTL ? 'flex-row-reverse' : ''}`}>
                        <button
                            onClick={() => navigate(-1)}
                            className="text-gray-400 hover:text-gray-600 transition-colors"
                        >
                            <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                    strokeWidth={2}
                                    d={isRTL ? 'M9 5l7 7-7 7' : 'M15 19l-7-7 7-7'}
                                />
                            </svg>
                        </button>
                        <div>
                            <h2 className="text-lg font-semibold text-gray-900">{request.serviceTitle}</h2>
                            <div className="flex items-center gap-2 mt-0.5">
                                <span className={`px-2 py-0.5 text-xs font-semibold rounded-full ${lifecycle.statusClass}`}>
                                    {t(`servicesMarketplace.status.${lifecycle.statusKey}`, { defaultValue: lifecycle.statusLabelFallback })}
                                </span>
                                {canSendChat && isConnected ? (
                                    <span className="flex items-center gap-1 text-xs text-green-600">
                                        <span className="w-2 h-2 bg-green-500 rounded-full animate-pulse" />
                                        {t('servicesMarketplace.chat.connected')}
                                    </span>
                                ) : canSendChat ? (
                                    <span className="flex items-center gap-1 text-xs text-gray-400">
                                        <span className="w-2 h-2 bg-gray-300 rounded-full" />
                                        {t('servicesMarketplace.chat.connecting')}
                                    </span>
                                ) : null}
                            </div>
                            {contract ? (
                                <div className="mt-2 text-sm text-gray-600">
                                    {contract.price !== null ? (
                                        <span className="font-semibold text-gray-900">
                                            {formatCurrency(contract.price, contract.currency || undefined)}
                                            {contract.pricingType === 'Hourly' ? <span className="font-normal text-gray-500"> / hour</span> : null}
                                        </span>
                                    ) : null}
                                    {contract.category ? <span className="ml-2 text-gray-500">{contract.category}</span> : null}
                                </div>
                            ) : null}
                            {ratingState ? (
                                <div className="mt-2">
                                    <RatingStateBadge ratingState={ratingState} />
                                </div>
                            ) : null}
                        </div>
                    </div>
                </div>
                {auditTimeline ? (
                    <div className="mt-4 border-t border-gray-100 pt-3">
                        <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-gray-500">
                            {t('servicesMarketplace.timeline.title', { defaultValue: 'Request history' })}
                        </div>
                        <ol className="space-y-2">
                            {auditTimeline.map((event) => (
                                <li key={event.eventId} className="flex items-start gap-2 text-xs text-gray-600">
                                    <span className="mt-1 h-2 w-2 rounded-full bg-indigo-500" />
                                    <span>
                                        <span className="font-medium text-gray-800">
                                            {t(`servicesMarketplace.timeline.${event.eventType}`, { defaultValue: event.eventType })}
                                        </span>
                                        <span className="text-gray-400"> · {formatDate(event.occurredAt)}</span>
                                        {event.actorRole ? (
                                            <span className="text-gray-400">
                                                {' · '}{t(`servicesMarketplace.chat.role${event.actorRole}`, { defaultValue: event.actorRole })}
                                            </span>
                                        ) : null}
                                    </span>
                                </li>
                            ))}
                        </ol>
                    </div>
                ) : null}
                {completionEvidence ? (
                    <div className="mt-4 border-t border-gray-100 pt-3">
                        <div className="mb-2 text-xs font-semibold uppercase tracking-wide text-gray-500">
                            {t('servicesMarketplace.completionEvidence.title', { defaultValue: 'Completion evidence' })}
                        </div>
                        <div className="space-y-2 text-sm text-gray-700">
                            <div className="rounded-lg border border-gray-100 bg-gray-50 p-3">
                                <div className="font-medium text-gray-900">
                                    {t('servicesMarketplace.completionEvidence.providerCompletion', { defaultValue: 'Provider completion' })}
                                </div>
                                <div className="mt-1 text-xs text-gray-500">
                                    {formatDate(completionEvidence.providerCompletion.occurredAt)}
                                </div>
                            </div>
                            {completionEvidence.buyerConfirmation ? (
                                <div className="rounded-lg border border-gray-100 bg-gray-50 p-3">
                                    <div className="font-medium text-gray-900">
                                        {t('servicesMarketplace.completionEvidence.buyerConfirmation', { defaultValue: 'Buyer confirmation' })}
                                    </div>
                                    <div className="mt-1 text-xs text-gray-500">
                                        {formatDate(completionEvidence.buyerConfirmation.occurredAt)}
                                    </div>
                                </div>
                            ) : null}
                        </div>
                    </div>
                ) : null}
            </div>

            <div
                ref={messagesContainerRef}
                className="card flex-1 overflow-y-auto mb-4 bg-gray-50"
                style={{ minHeight: 0 }}
            >
                {isLoadingMessages ? (
                    <div className="flex items-center justify-center py-12">
                        <div className="flex flex-col items-center gap-3">
                            <div className="w-8 h-8 border-4 border-indigo-600 border-t-transparent rounded-full animate-spin" />
                            <p className="text-sm text-gray-500">{t('servicesMarketplace.chat.loadingMessages')}</p>
                        </div>
                    </div>
                ) : chatAuthorityFailed ? (
                    <div className="flex items-center justify-center h-full">
                        <div className="text-center py-12">
                            <h3 className="text-lg font-semibold text-gray-700 mb-2">
                                {t('servicesMarketplace.chat.unavailableTitle')}
                            </h3>
                            <p className="text-gray-500">{t('servicesMarketplace.chat.unavailableDescription')}</p>
                        </div>
                    </div>
                ) : messages.length === 0 ? (
                    <div className="flex items-center justify-center h-full">
                        <div className="text-center py-12">
                            <h3 className="text-lg font-semibold text-gray-700 mb-2">{t('servicesMarketplace.chat.emptyTitle')}</h3>
                            <p className="text-gray-500">{t('servicesMarketplace.chat.emptyDescription')}</p>
                        </div>
                    </div>
                ) : (
                    <div className="p-4">
                        {hasOlderMessages ? (
                            <div className="mb-4 flex justify-center">
                                <button
                                    type="button"
                                    onClick={() => void handleLoadOlder()}
                                    disabled={isLoadingOlder}
                                    className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:cursor-not-allowed disabled:opacity-50"
                                >
                                    {isLoadingOlder
                                        ? t('servicesMarketplace.chat.loadingMessages')
                                        : t('servicesMarketplace.chat.loadOlder', { defaultValue: 'Load older messages' })}
                                </button>
                            </div>
                        ) : null}
                        {messages.map((message: ChatMessageType) => (
                            <ChatMessage
                                dir={dir}
                                formatTime={formatTime}
                                key={message.id}
                                message={message}
                                isOwnMessage={message.senderUserId === chatIdentity.currentUserId}
                            />
                        ))}
                        <div ref={messagesEndRef} />
                    </div>
                )}
            </div>

            {canSendChat ? <form onSubmit={handleSend} className="card flex-shrink-0 shadow-lg">
                {selectedFile && (
                    <div className="mb-3 p-3 bg-gray-50 rounded-lg border border-gray-200">
                        <div className="flex items-start gap-3">
                            {previewUrl ? (
                                <img
                                    src={previewUrl}
                                    alt={t('servicesMarketplace.chat.previewAlt')}
                                    className="w-20 h-20 object-cover rounded-lg"
                                />
                            ) : (
                                <div className="w-20 h-20 bg-gray-200 rounded-lg flex items-center justify-center">
                                    <svg className="w-10 h-10 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                                    </svg>
                                </div>
                            )}
                            <div className="flex-1 min-w-0">
                                <p className="text-sm font-medium text-gray-900 truncate">{selectedFile.name}</p>
                                <p className="text-xs text-gray-500 mt-0.5">
                                    {t('servicesMarketplace.chat.fileSizeKilobytes', {
                                        size: formatNumber(selectedFile.size / 1024, {
                                            minimumFractionDigits: 1,
                                            maximumFractionDigits: 1,
                                        }),
                                    })}
                                </p>
                            </div>
                            <button
                                type="button"
                                onClick={handleRemoveFile}
                                className="text-gray-400 hover:text-red-600 transition-colors"
                            >
                                <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                                </svg>
                            </button>
                        </div>
                    </div>
                )}

                <div className={`flex gap-2 ${isRTL ? 'flex-row-reverse' : ''}`}>
                    <input
                        ref={fileInputRef}
                        type="file"
                        onChange={handleFileSelect}
                        className="hidden"
                        accept="image/*,.pdf,.doc,.docx,.txt"
                        disabled={!canSend || isSending}
                    />
                    <button
                        type="button"
                        onClick={() => fileInputRef.current?.click()}
                        disabled={!canSend || isSending}
                        className="p-2.5 text-gray-500 hover:text-indigo-600 hover:bg-indigo-50 rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
                        title={t('servicesMarketplace.chat.attachFile')}
                    >
                        <svg className="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.172 7l-6.586 6.586a2 2 0 102.828 2.828l6.414-6.586a4 4 0 00-5.656-5.656l-6.415 6.585a6 6 0 108.486 8.486L20.5 13" />
                        </svg>
                    </button>

                    <input
                        type="text"
                        value={messageBody}
                        onChange={(event) => setMessageBody(event.target.value)}
                        placeholder={canSend ? t('servicesMarketplace.chat.messagePlaceholder') : t('servicesMarketplace.chat.disabledPlaceholder')}
                        className="input flex-1"
                        disabled={!canSend || isSending}
                        onKeyDown={(event) => {
                            if (event.key === 'Enter' && !event.shiftKey) {
                                event.preventDefault()
                                void handleSend(event)
                            }
                        }}
                    />

                    <button
                        type="submit"
                        disabled={!canSend || isSending || (!messageBody.trim() && !selectedFile)}
                        className="px-5 py-2.5 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2 font-medium"
                    >
                        {isSending ? t('servicesMarketplace.chat.sending') : t('servicesMarketplace.chat.send')}
                    </button>
                </div>
            </form> : null}
        </div>
    )
}
