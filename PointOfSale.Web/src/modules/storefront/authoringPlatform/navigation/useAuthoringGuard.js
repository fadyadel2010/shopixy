import { useCallback, useEffect, useMemo, useRef, useState } from 'react'

import { useAuthoringShortcuts } from '../keyboard/useAuthoringShortcuts'
import { useAuthoringPlatform } from '../providers/AuthoringPlatformProvider'

const isUnmodifiedPrimaryClick = (event) => event.button === 0 && !event.metaKey && !event.ctrlKey && !event.shiftKey && !event.altKey

/** One guard for browser unload, breadcrumbs, sidebar links, in-editor navigation, and shortcuts. */
export function useAuthoringGuard({ isDirty, isBusy = false, onSave, onPreview }) {
    const { registerNavigationGuard } = useAuthoringPlatform()
    const navigate = useCallback((path) => {
        window.history.pushState({}, '', path)
        window.dispatchEvent(new PopStateEvent('popstate'))
    }, [])
    const [pendingPath, setPendingPath] = useState(null)
    const bypassRef = useRef(false)
    const shouldBlock = isDirty || isBusy

    const requestNavigation = useCallback((path) => {
        if (!shouldBlock || bypassRef.current) {
            navigate(path)
            return true
        }
        setPendingPath(path)
        return false
    }, [navigate, shouldBlock])

    const cancelNavigation = useCallback(() => setPendingPath(null), [])
    const discardAndLeave = useCallback(() => {
        if (!pendingPath || isBusy) return
        const path = pendingPath
        setPendingPath(null)
        bypassRef.current = true
        navigate(path)
        window.setTimeout(() => { bypassRef.current = false }, 0)
    }, [isBusy, navigate, pendingPath])

    useEffect(() => registerNavigationGuard(requestNavigation), [registerNavigationGuard, requestNavigation])

    useEffect(() => {
        if (!shouldBlock) return undefined
        const handleBeforeUnload = (event) => {
            event.preventDefault()
            event.returnValue = ''
        }
        window.addEventListener('beforeunload', handleBeforeUnload)
        return () => window.removeEventListener('beforeunload', handleBeforeUnload)
    }, [shouldBlock])

    useEffect(() => {
        if (!shouldBlock) return undefined
        const handleDocumentClick = (event) => {
            if (bypassRef.current || event.defaultPrevented || !isUnmodifiedPrimaryClick(event)) return
            const anchor = event.target.closest?.('a[href]')
            if (!anchor || anchor.target === '_blank' || anchor.hasAttribute('download')) return
            const url = new URL(anchor.href, window.location.href)
            if (url.origin !== window.location.origin || url.href === window.location.href) return
            event.preventDefault()
            event.stopPropagation()
            setPendingPath(`${url.pathname}${url.search}${url.hash}`)
        }
        document.addEventListener('click', handleDocumentClick, true)
        return () => document.removeEventListener('click', handleDocumentClick, true)
    }, [shouldBlock])

    const shortcuts = useMemo(() => [
        onSave ? {
            key: 's',
            ctrlOrMeta: true,
            disabled: isBusy || !isDirty,
            onInvoke: onSave,
        } : null,
        onPreview ? {
            key: 'p',
            ctrlOrMeta: true,
            shiftKey: true,
            disabled: isBusy,
            onInvoke: onPreview,
        } : null,
    ].filter(Boolean), [isBusy, isDirty, onPreview, onSave])
    useAuthoringShortcuts(shortcuts)

    return {
        requestNavigation,
        unsavedDialogProps: {
            open: pendingPath != null,
            busy: isBusy,
            onDiscard: discardAndLeave,
            onClose: cancelNavigation,
        },
    }
}
