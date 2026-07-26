/**
 * CMS Store
 * Phase 9.11: CMS Pages & Navigation Management
 * 
 * CRITICAL: All edits modify DRAFT ONLY until explicit Publish
 */

import { create } from 'zustand'
import { devtools } from 'zustand/middleware'

/**
 * Generate unique section ID
 */
export const generateSectionId = () => {
    return `section_${Date.now()}_${Math.random().toString(36).substring(2, 9)}`
}

/**
 * Initial state
 */
const initialState = {
    // CMS Pages
    pages: [],
    currentPage: null,
    isLoadingPages: false,

    // Current page editing
    editingPage: null,
    isDirty: false,
    isSaving: false,
    isPublishing: false,

    // Navigation
    navigation: null,
    isLoadingNavigation: false,
    isNavigationDirty: false,
    isNavigationSaving: false,
    isNavigationPublishing: false,

    // Errors
    error: null,
}

/**
 * CMS Store using Zustand
 */
export const useCmsStore = create(
    devtools(
        (set, get) => ({
            ...initialState,

            // ===================================
            // CMS PAGES
            // ===================================

            setPages: (pages) => set({ pages }),
            setCurrentPage: (currentPage) => set({ currentPage }),
            setLoadingPages: (isLoadingPages) => set({ isLoadingPages }),

            // Editing page
            setEditingPage: (editingPage) => set({
                editingPage,
                isDirty: false
            }),

            updateEditingPage: (updates) => {
                const state = get()
                if (!state.editingPage) return

                set({
                    editingPage: {
                        ...state.editingPage,
                        draft: {
                            ...state.editingPage.draft,
                            ...updates,
                        },
                    },
                    isDirty: true,
                })
            },

            updateEditingPageSeo: (seoUpdates) => {
                const state = get()
                if (!state.editingPage) return

                set({
                    editingPage: {
                        ...state.editingPage,
                        draft: {
                            ...state.editingPage.draft,
                            seo: {
                                ...state.editingPage.draft.seo,
                                ...seoUpdates,
                            },
                        },
                    },
                    isDirty: true,
                })
            },

            // Sections
            addSection: (sectionType) => {
                const state = get()
                if (!state.editingPage) return

                const sections = state.editingPage.draft.sections || []
                const newSection = {
                    id: generateSectionId(),
                    type: sectionType,
                    enabled: true,
                    order: sections.length,
                    settings: getDefaultSettings(sectionType),
                }

                set({
                    editingPage: {
                        ...state.editingPage,
                        draft: {
                            ...state.editingPage.draft,
                            sections: [...sections, newSection],
                        },
                    },
                    isDirty: true,
                })
            },

            updateSection: (sectionId, updates) => {
                const state = get()
                if (!state.editingPage) return

                const sections = state.editingPage.draft.sections || []
                const updatedSections = sections.map((s) =>
                    s.id === sectionId ? { ...s, ...updates } : s
                )

                set({
                    editingPage: {
                        ...state.editingPage,
                        draft: {
                            ...state.editingPage.draft,
                            sections: updatedSections,
                        },
                    },
                    isDirty: true,
                })
            },

            updateSectionSettings: (sectionId, settings) => {
                const state = get()
                if (!state.editingPage) return

                const sections = state.editingPage.draft.sections || []
                const updatedSections = sections.map((s) =>
                    s.id === sectionId
                        ? { ...s, settings: { ...s.settings, ...settings } }
                        : s
                )

                set({
                    editingPage: {
                        ...state.editingPage,
                        draft: {
                            ...state.editingPage.draft,
                            sections: updatedSections,
                        },
                    },
                    isDirty: true,
                })
            },

            removeSection: (sectionId) => {
                const state = get()
                if (!state.editingPage) return

                const sections = state.editingPage.draft.sections || []
                const updatedSections = sections
                    .filter((s) => s.id !== sectionId)
                    .map((s, i) => ({ ...s, order: i }))

                set({
                    editingPage: {
                        ...state.editingPage,
                        draft: {
                            ...state.editingPage.draft,
                            sections: updatedSections,
                        },
                    },
                    isDirty: true,
                })
            },

            reorderSections: (oldIndex, newIndex) => {
                const state = get()
                if (!state.editingPage) return

                const sections = [...(state.editingPage.draft.sections || [])]
                const [removed] = sections.splice(oldIndex, 1)
                sections.splice(newIndex, 0, removed)

                const reorderedSections = sections.map((s, i) => ({ ...s, order: i }))

                set({
                    editingPage: {
                        ...state.editingPage,
                        draft: {
                            ...state.editingPage.draft,
                            sections: reorderedSections,
                        },
                    },
                    isDirty: true,
                })
            },

            // State flags
            markDirty: () => set({ isDirty: true }),
            markClean: () => set({ isDirty: false }),
            setSaving: (isSaving) => set({ isSaving }),
            setPublishing: (isPublishing) => set({ isPublishing }),

            // ===================================
            // NAVIGATION
            // ===================================

            setNavigation: (navigation) => set({ navigation }),
            setLoadingNavigation: (isLoadingNavigation) => set({ isLoadingNavigation }),

            updateNavigationHeader: (links) => {
                const state = get()
                set({
                    navigation: {
                        ...state.navigation,
                        draft: {
                            ...state.navigation?.draft,
                            header: links,
                        },
                    },
                    isNavigationDirty: true,
                })
            },

            updateNavigationFooter: (links) => {
                const state = get()
                set({
                    navigation: {
                        ...state.navigation,
                        draft: {
                            ...state.navigation?.draft,
                            footer: links,
                        },
                    },
                    isNavigationDirty: true,
                })
            },

            addNavigationLink: (location, link) => {
                const state = get()
                const nav = state.navigation?.draft || { header: [], footer: [] }
                const links = location === 'header' ? [...(nav.header || [])] : [...(nav.footer || [])]

                const newLink = {
                    ...link,
                    order: links.length,
                    enabled: true,
                }
                links.push(newLink)

                if (location === 'header') {
                    set({
                        navigation: {
                            ...state.navigation,
                            draft: { ...nav, header: links },
                        },
                        isNavigationDirty: true,
                    })
                } else {
                    set({
                        navigation: {
                            ...state.navigation,
                            draft: { ...nav, footer: links },
                        },
                        isNavigationDirty: true,
                    })
                }
            },

            removeNavigationLink: (location, index) => {
                const state = get()
                const nav = state.navigation?.draft || { header: [], footer: [] }
                const links = location === 'header'
                    ? nav.header?.filter((_, i) => i !== index) || []
                    : nav.footer?.filter((_, i) => i !== index) || []

                if (location === 'header') {
                    set({
                        navigation: {
                            ...state.navigation,
                            draft: { ...nav, header: links.map((l, i) => ({ ...l, order: i })) },
                        },
                        isNavigationDirty: true,
                    })
                } else {
                    set({
                        navigation: {
                            ...state.navigation,
                            draft: { ...nav, footer: links.map((l, i) => ({ ...l, order: i })) },
                        },
                        isNavigationDirty: true,
                    })
                }
            },

            reorderNavigationLinks: (location, oldIndex, newIndex) => {
                const state = get()
                const nav = state.navigation?.draft || { header: [], footer: [] }
                const links = location === 'header' ? [...(nav.header || [])] : [...(nav.footer || [])]

                const [removed] = links.splice(oldIndex, 1)
                links.splice(newIndex, 0, removed)
                const reordered = links.map((l, i) => ({ ...l, order: i }))

                if (location === 'header') {
                    set({
                        navigation: {
                            ...state.navigation,
                            draft: { ...nav, header: reordered },
                        },
                        isNavigationDirty: true,
                    })
                } else {
                    set({
                        navigation: {
                            ...state.navigation,
                            draft: { ...nav, footer: reordered },
                        },
                        isNavigationDirty: true,
                    })
                }
            },

            setNavigationDirty: (isNavigationDirty) => set({ isNavigationDirty }),
            setNavigationSaving: (isNavigationSaving) => set({ isNavigationSaving }),
            setNavigationPublishing: (isNavigationPublishing) => set({ isNavigationPublishing }),

            // ===================================
            // GENERAL
            // ===================================

            setError: (error) => set({ error }),
            clearError: () => set({ error: null }),

            // Reset
            reset: () => set(initialState),
        }),
        { name: 'cms-store' }
    )
)

/**
 * Get default settings for a section type
 */
function getDefaultSettings(type) {
    switch (type) {
        case 'heading':
            return { text: '', level: 2 }
        case 'text-block':
            return { text: '', alignment: 'left' }
        case 'rich-text':
            return { html: '' }
        case 'contact-form':
            return { showPhone: true, showMessage: true }
        case 'image':
            return { src: '', alt: '', caption: '' }
        default:
            return {}
    }
}

// Selectors
export const selectPages = (state) => state.pages
export const selectCurrentPage = (state) => state.currentPage
export const selectEditingPage = (state) => state.editingPage
export const selectIsDirty = (state) => state.isDirty
export const selectIsSaving = (state) => state.isSaving
export const selectIsPublishing = (state) => state.isPublishing
export const selectNavigation = (state) => state.navigation
export const selectIsNavigationDirty = (state) => state.isNavigationDirty

export default useCmsStore
