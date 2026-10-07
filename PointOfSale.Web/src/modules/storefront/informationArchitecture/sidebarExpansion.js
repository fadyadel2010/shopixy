// Expansion is presentation state, never permission/capability authority.
// A fresh actor gets the navigation model's discoverable initial hierarchy;
// explicit choices are restored only for that same authenticated actor scope.
export const sidebarActorScope = (user) => user?.tenantId && user?.id
    ? `${user.tenantId}:${user.id}`
    : null

export const initialSidebarExpansion = (navigation) => {
    const expanded = {}
    const visit = (node, expandSubtree = false) => {
        if (!node.children) return
        const subtree = expandSubtree || node.initialExpansion === 'subtree'
        expanded[node.key] = subtree || node.defaultExpanded === true
        node.children.forEach(child => visit(child, subtree))
    }
    visit(navigation)
    return expanded
}

export const readSidebarExpansion = (scope, navigation, storedValues) => {
    const defaults = initialSidebarExpansion(navigation)
    if (!scope) return defaults
    for (const value of storedValues) {
        try {
            const cached = JSON.parse(value)
            if (cached?.version !== 2 || cached.scope !== scope || !cached.expanded || typeof cached.expanded !== 'object' || Array.isArray(cached.expanded)) continue
            for (const [key, expanded] of Object.entries(cached.expanded)) {
                if (typeof expanded === 'boolean') defaults[key] = expanded
            }
        } catch {
            // Corrupt or inaccessible storage cannot hide the initial hierarchy.
        }
    }
    return defaults
}

export const serializeSidebarExpansion = (scope, expanded) => JSON.stringify({ version: 2, scope, expanded })
