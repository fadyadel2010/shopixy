/**
 * Scroll Utilities
 * 
 * Smooth scroll and scroll position utilities.
 */

/**
 * Scroll to top of the page with smooth animation
 */
export function scrollToTop(behavior: ScrollBehavior = 'smooth'): void {
    window.scrollTo({
        top: 0,
        left: 0,
        behavior,
    });
}

/**
 * Scroll to a specific element
 */
export function scrollToElement(
    element: HTMLElement | null,
    options?: ScrollIntoViewOptions
): void {
    if (element) {
        element.scrollIntoView({
            behavior: 'smooth',
            block: 'start',
            ...options,
        });
    }
}

/**
 * Scroll to element by ID
 */
export function scrollToId(
    id: string,
    options?: ScrollIntoViewOptions
): void {
    const element = document.getElementById(id);
    scrollToElement(element, options);
}
