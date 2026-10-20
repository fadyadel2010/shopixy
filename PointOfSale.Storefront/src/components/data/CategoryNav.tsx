/**
 * Category Navigation Component
 * 
 * Horizontal scrollable category navigation.
 * Selected category is highlighted.
 * Mobile-friendly with touch scrolling.
 */

import React, { useRef, useEffect, useState } from 'react';
import type { CategoryTreeDto } from '@/types';

interface CategoryNavProps {
    /** Categories to display */
    categories: CategoryTreeDto[];
    /** Currently selected category ID */
    selectedId?: string;
    /** Selection callback */
    onSelect: (categoryId: string | undefined) => void;
    /** Show "All" option */
    showAll?: boolean;
    /** Loading state */
    isLoading?: boolean;
    /** Additional CSS classes */
    className?: string;
}

/**
 * Category Navigation Component
 */
export const CategoryNav: React.FC<CategoryNavProps> = ({
    categories,
    selectedId,
    onSelect,
    showAll = true,
    isLoading = false,
    className = '',
}) => {
    const scrollRef = useRef<HTMLDivElement>(null);
    const [showLeftGradient, setShowLeftGradient] = useState(false);
    const [showRightGradient, setShowRightGradient] = useState(false);

    // Check for scroll overflow
    useEffect(() => {
        const checkScroll = () => {
            if (scrollRef.current) {
                const { scrollLeft, scrollWidth, clientWidth } = scrollRef.current;
                setShowLeftGradient(scrollLeft > 0);
                setShowRightGradient(scrollLeft + clientWidth < scrollWidth - 1);
            }
        };

        checkScroll();

        const scrollElement = scrollRef.current;
        if (scrollElement) {
            scrollElement.addEventListener('scroll', checkScroll);
            window.addEventListener('resize', checkScroll);
        }

        return () => {
            if (scrollElement) {
                scrollElement.removeEventListener('scroll', checkScroll);
            }
            window.removeEventListener('resize', checkScroll);
        };
    }, [categories]);

    // Loading skeleton
    if (isLoading) {
        return (
            <div className={`relative ${className}`}>
                <div className="flex gap-3 overflow-hidden py-2">
                    {Array.from({ length: 6 }).map((_, i) => (
                        <div
                            key={i}
                            className="h-10 w-24 flex-shrink-0 bg-muted/10 rounded-full skeleton"
                        />
                    ))}
                </div>
            </div>
        );
    }

    // Empty state
    if (categories.length === 0) {
        return null;
    }

    return (
        <nav className={`relative min-w-0 max-w-full ${className}`} aria-label="Category navigation">
            {/* Left gradient fade */}
            {showLeftGradient && (
                <div className="absolute left-0 top-0 bottom-0 w-8 bg-gradient-to-r from-background to-transparent z-10 pointer-events-none" />
            )}

            {/* Scrollable container */}
            <div
                ref={scrollRef}
                className="flex max-w-full gap-2 overflow-x-auto scrollbar-hide py-2"
                style={{ scrollBehavior: 'smooth' }}
            >
                {/* All button */}
                {showAll && (
                    <button
                        onClick={() => onSelect(undefined)}
                        className={`flex-shrink-0 px-4 py-2 rounded-full text-sm font-medium transition-all ${selectedId === undefined
                                ? 'bg-primary text-on-primary shadow-md'
                                : 'bg-muted/10 text-text hover:bg-muted/20'
                            }`}
                    >
                        All Products
                    </button>
                )}

                {/* Category buttons */}
                {categories.map((category) => (
                    <button
                        key={category.id}
                        onClick={() => onSelect(category.id)}
                        className={`flex-shrink-0 px-4 py-2 rounded-full text-sm font-medium transition-all ${selectedId === category.id
                                ? 'bg-primary text-on-primary shadow-md'
                                : 'bg-muted/10 text-text hover:bg-muted/20'
                            }`}
                    >
                        {category.name}
                    </button>
                ))}
            </div>

            {/* Right gradient fade */}
            {showRightGradient && (
                <div className="absolute right-0 top-0 bottom-0 w-8 bg-gradient-to-l from-background to-transparent z-10 pointer-events-none" />
            )}
        </nav>
    );
};
