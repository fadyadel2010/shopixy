export const AUTHORING_FOCUSABLE_SELECTOR = [
    'button:not([disabled])',
    'input:not([disabled])',
    'textarea:not([disabled])',
    'select:not([disabled])',
    '[href]',
    '[tabindex]:not([tabindex="-1"])',
].join(', ')

export function getAuthoringFocusableElements(container) {
    return container ? [...container.querySelectorAll(AUTHORING_FOCUSABLE_SELECTOR)] : []
}
