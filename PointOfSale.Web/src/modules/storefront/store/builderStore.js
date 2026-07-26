/**
 * Builder Store
 * Phase 9.8: POS Integration - Unified Admin Experience
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
    currentPage: 'home',
    sections: [],
    selectedSectionId: null,
    isDirty: false,
    isSaving: false,
    isPublishing: false,
    isPreviewMode: false,
    undoStack: [],
    redoStack: [],
}

/**
 * Builder Store using Zustand
 */
export const useBuilderStore = create(
    devtools(
        (set, get) => ({
            ...initialState,

            // Page selection
            setCurrentPage: (page) => set({ currentPage: page }),

            // Sections
            setSections: (sections) => {
                const state = get()
                set({
                    sections,
                    isDirty: true,
                    undoStack: [...state.undoStack, state.sections].slice(-50),
                    redoStack: [],
                })
            },

            admitSections: (sections, options = {}) => {
                const state = get()
                const resetHistory = options.resetHistory !== false
                const acceptedSectionIds = new Set(sections.map((section) => section.id))
                const selectedSectionId = options.preserveSelection === true && acceptedSectionIds.has(state.selectedSectionId)
                    ? state.selectedSectionId
                    : null

                set({
                    sections,
                    selectedSectionId,
                    isDirty: false,
                    undoStack: resetHistory ? [] : state.undoStack,
                    redoStack: resetHistory ? [] : state.redoStack,
                })
            },

            selectSection: (sectionId) => set({ selectedSectionId: sectionId }),

            addSection: (sectionType, insertAtIndex, registry) => {
                const state = get()
                const definition = registry?.get(sectionType)

                if (!definition) {
                    console.warn(`Unknown section type: ${sectionType}`)
                    return
                }

                const newSection = {
                    id: generateSectionId(),
                    type: sectionType,
                    order: insertAtIndex ?? state.sections.length,
                    enabled: true,
                    settings: { ...definition.defaultSettings },
                }

                let newSections
                if (insertAtIndex !== undefined && insertAtIndex >= 0) {
                    newSections = [
                        ...state.sections.slice(0, insertAtIndex),
                        newSection,
                        ...state.sections.slice(insertAtIndex),
                    ]
                } else {
                    newSections = [...state.sections, newSection]
                }

                // Reorder
                newSections = newSections.map((s, i) => ({ ...s, order: i }))

                set({
                    sections: newSections,
                    selectedSectionId: newSection.id,
                    isDirty: true,
                    undoStack: [...state.undoStack, state.sections].slice(-50),
                    redoStack: [],
                })
            },

            removeSection: (sectionId) => {
                const state = get()
                const newSections = state.sections
                    .filter((s) => s.id !== sectionId)
                    .map((s, i) => ({ ...s, order: i }))

                set({
                    sections: newSections,
                    selectedSectionId: state.selectedSectionId === sectionId ? null : state.selectedSectionId,
                    isDirty: true,
                    undoStack: [...state.undoStack, state.sections].slice(-50),
                    redoStack: [],
                })
            },

            duplicateSection: (sectionId) => {
                const state = get()
                const sectionIndex = state.sections.findIndex((s) => s.id === sectionId)

                if (sectionIndex === -1) return

                const section = state.sections[sectionIndex]
                const newSection = {
                    ...section,
                    id: generateSectionId(),
                    settings: { ...section.settings },
                }

                const newSections = [
                    ...state.sections.slice(0, sectionIndex + 1),
                    newSection,
                    ...state.sections.slice(sectionIndex + 1),
                ].map((s, i) => ({ ...s, order: i }))

                set({
                    sections: newSections,
                    selectedSectionId: newSection.id,
                    isDirty: true,
                    undoStack: [...state.undoStack, state.sections].slice(-50),
                    redoStack: [],
                })
            },

            reorderSections: (oldIndex, newIndex) => {
                const state = get()
                const sections = [...state.sections]
                const [removed] = sections.splice(oldIndex, 1)
                sections.splice(newIndex, 0, removed)

                const newSections = sections.map((s, i) => ({ ...s, order: i }))

                set({
                    sections: newSections,
                    isDirty: true,
                    undoStack: [...state.undoStack, state.sections].slice(-50),
                    redoStack: [],
                })
            },

            updateSectionSettings: (sectionId, newSettings) => {
                const state = get()
                const newSections = state.sections.map((s) =>
                    s.id === sectionId
                        ? { ...s, settings: { ...s.settings, ...newSettings } }
                        : s
                )

                set({
                    sections: newSections,
                    isDirty: true,
                    undoStack: [...state.undoStack, state.sections].slice(-50),
                    redoStack: [],
                })
            },

            toggleSection: (sectionId) => {
                const state = get()
                const newSections = state.sections.map((s) =>
                    s.id === sectionId ? { ...s, enabled: !s.enabled } : s
                )

                set({
                    sections: newSections,
                    isDirty: true,
                    undoStack: [...state.undoStack, state.sections].slice(-50),
                    redoStack: [],
                })
            },

            // State flags
            markDirty: () => set({ isDirty: true }),
            markClean: () => set({ isDirty: false }),
            setSaving: (isSaving) => set({ isSaving }),
            setPublishing: (isPublishing) => set({ isPublishing }),
            setPreviewMode: (isPreviewMode) => set({ isPreviewMode }),

            // Undo/Redo
            undo: () => {
                const state = get()
                if (state.undoStack.length === 0) return

                const previousSections = state.undoStack[state.undoStack.length - 1]
                const newUndoStack = state.undoStack.slice(0, -1)

                set({
                    sections: previousSections,
                    undoStack: newUndoStack,
                    redoStack: [...state.redoStack, state.sections],
                    isDirty: true,
                })
            },

            redo: () => {
                const state = get()
                if (state.redoStack.length === 0) return

                const nextSections = state.redoStack[state.redoStack.length - 1]
                const newRedoStack = state.redoStack.slice(0, -1)

                set({
                    sections: nextSections,
                    undoStack: [...state.undoStack, state.sections],
                    redoStack: newRedoStack,
                    isDirty: true,
                })
            },

            canUndo: () => get().undoStack.length > 0,
            canRedo: () => get().redoStack.length > 0,

            // Reset
            reset: () => set(initialState),
        }),
        { name: 'builder-store' }
    )
)

// Selectors
export const selectSections = (state) => state.sections
export const selectCurrentPage = (state) => state.currentPage
export const selectSelectedSectionId = (state) => state.selectedSectionId
export const selectSelectedSection = (state) =>
    state.sections.find((s) => s.id === state.selectedSectionId) || null
export const selectIsDirty = (state) => state.isDirty
export const selectIsSaving = (state) => state.isSaving
export const selectIsPublishing = (state) => state.isPublishing

export default useBuilderStore
