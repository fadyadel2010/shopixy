/**
 * Pagination Component
 * 
 * Accessible pagination with page numbers, prev/next buttons.
 * Mobile-friendly with responsive layout.
 */

import React from 'react';

interface PaginationProps {
    /** Current page (1-indexed) */
    currentPage: number;
    /** Total number of pages */
    totalPages: number;
    /** Total item count */
    totalCount?: number;
    /** Current page size */
    pageSize?: number;
    /** Page change callback */
    onPageChange: (page: number) => void;
    /** Additional CSS classes */
    className?: string;
}

/**
 * Generate page numbers to display
 */
function generatePageNumbers(current: number, total: number): (number | 'ellipsis')[] {
    if (total <= 7) {
        return Array.from({ length: total }, (_, i) => i + 1);
    }

    const pages: (number | 'ellipsis')[] = [];

    // Always show first page
    pages.push(1);

    if (current > 3) {
        pages.push('ellipsis');
    }

    // Pages around current
    const start = Math.max(2, current - 1);
    const end = Math.min(total - 1, current + 1);

    for (let i = start; i <= end; i++) {
        pages.push(i);
    }

    if (current < total - 2) {
        pages.push('ellipsis');
    }

    // Always show last page
    if (total > 1) {
        pages.push(total);
    }

    return pages;
}

/**
 * Pagination Component
 */
export const Pagination: React.FC<PaginationProps> = ({
    currentPage,
    totalPages,
    totalCount,
    pageSize,
    onPageChange,
    className = '',
}) => {
    if (totalPages <= 1) {
        return null;
    }

    const pageNumbers = generatePageNumbers(currentPage, totalPages);
    const hasPrev = currentPage > 1;
    const hasNext = currentPage < totalPages;

    // Calculate item range for display
    const startItem = totalCount && pageSize ? (currentPage - 1) * pageSize + 1 : null;
    const endItem = totalCount && pageSize ? Math.min(currentPage * pageSize, totalCount) : null;

    return (
        <div className={`flex flex-col sm:flex-row items-center justify-between gap-4 ${className}`}>
            {/* Item count */}
            {totalCount !== undefined && (
                <p className="text-sm text-muted order-2 sm:order-1">
                    {startItem && endItem ? (
                        <>
                            Showing <span className="font-medium text-text">{startItem}-{endItem}</span> of{' '}
                            <span className="font-medium text-text">{totalCount}</span> products
                        </>
                    ) : (
                        <>
                            <span className="font-medium text-text">{totalCount}</span> products
                        </>
                    )}
                </p>
            )}

            {/* Pagination controls */}
            <nav
                className="flex items-center gap-1 order-1 sm:order-2"
                aria-label="Pagination"
            >
                {/* Previous button */}
                <button
                    onClick={() => onPageChange(currentPage - 1)}
                    disabled={!hasPrev}
                    className="inline-flex items-center gap-1 px-3 py-2 text-sm font-medium rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed hover:bg-muted/10 text-text"
                    aria-label="Previous page"
                >
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
                    </svg>
                    <span className="hidden sm:inline">Previous</span>
                </button>

                {/* Page numbers (desktop) */}
                <div className="hidden sm:flex items-center gap-1">
                    {pageNumbers.map((page, index) => (
                        page === 'ellipsis' ? (
                            <span key={`ellipsis-${index}`} className="px-2 text-muted">
                                …
                            </span>
                        ) : (
                            <button
                                key={page}
                                onClick={() => onPageChange(page)}
                                disabled={page === currentPage}
                                className={`w-10 h-10 text-sm font-medium rounded-lg transition-colors ${page === currentPage
                                        ? 'bg-primary text-white'
                                        : 'text-text hover:bg-muted/10'
                                    }`}
                                aria-label={`Page ${page}`}
                                aria-current={page === currentPage ? 'page' : undefined}
                            >
                                {page}
                            </button>
                        )
                    ))}
                </div>

                {/* Mobile page indicator */}
                <span className="sm:hidden px-4 py-2 text-sm text-muted">
                    {currentPage} / {totalPages}
                </span>

                {/* Next button */}
                <button
                    onClick={() => onPageChange(currentPage + 1)}
                    disabled={!hasNext}
                    className="inline-flex items-center gap-1 px-3 py-2 text-sm font-medium rounded-lg transition-colors disabled:opacity-50 disabled:cursor-not-allowed hover:bg-muted/10 text-text"
                    aria-label="Next page"
                >
                    <span className="hidden sm:inline">Next</span>
                    <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                    </svg>
                </button>
            </nav>
        </div>
    );
};
