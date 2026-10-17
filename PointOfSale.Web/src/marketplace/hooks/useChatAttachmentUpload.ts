import { useEffect, useRef } from 'react'
import { marketplaceApi } from '../api/marketplaceApi'
import { getMarketplaceAuthorityScope, useMarketplaceAuthorityScope } from '../authority'
import type { ChatIdentityAuthority } from '../chatAuthority'

const binding = (identity: ChatIdentityAuthority | null, scope: string) => identity?.canSendChat && scope !== 'ineligible'
    ? JSON.stringify([scope, identity.requestId, identity.serviceId, identity.buyerUserId, identity.providerUserId, identity.currentUserId, identity.currentUserRole]) : null

// Uploading is not message-send authority. The page still admits the returned
// attachment identity and independently checks current chat/send authority.
export function useChatAttachmentUpload(identity: ChatIdentityAuthority | null) {
    const authorityScope = useMarketplaceAuthorityScope()
    const current = useRef({ identity, authorityScope })
    current.current = { identity, authorityScope }
    const pending = useRef(false)
    const mounted = useRef(true)
    useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])

    return async (requestId: string, file: File): Promise<unknown> => {
        const context = current.current
        const key = binding(context.identity, context.authorityScope)
        if (!mounted.current || pending.current || !key || context.identity?.requestId !== requestId ||
            getMarketplaceAuthorityScope() !== context.authorityScope) throw new Error('Current chat attachment authority is unavailable')
        pending.current = true
        try {
            const payload = await marketplaceApi.uploadChatAttachment(requestId, file)
            if (!mounted.current || binding(current.current.identity, current.current.authorityScope) !== key ||
                getMarketplaceAuthorityScope() !== context.authorityScope) throw new Error('Chat attachment authority changed during upload')
            return payload
        } finally { pending.current = false }
    }
}
