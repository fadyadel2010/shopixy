/**
 * Category Page
 * 
 * Displays all categories in a browsable grid with main categories and subcategories.
 * Design is controlled via the Storefront Builder (page=category).
 * 
 * Phase 9.11: Category Browsing Experience
 * 
 * The page supports both:
 * 1. Builder-defined sections (from config.pages.category.sections)
 * 2. Default category grid when no builder sections are configured
 */

import React, { useEffect, useState } from 'react';
import { Layout } from '@/components/layout';
import { SectionRenderer } from '@/components/sections/SectionRenderer';
import { useStoreConfigStore, selectConfig } from '@/store';
import { fetchCategories } from '@/services/api/storefrontApi';
import type { CategoryTreeDto } from '@/types/category';
import './CategoryPage.css';

// ============================================================================
// Category Card Component
// ============================================================================

interface CategoryCardProps {
    category: CategoryTreeDto;
    isExpanded?: boolean;
    onToggle?: () => void;
}

const CategoryCard: React.FC<CategoryCardProps> = ({ category, isExpanded, onToggle }) => {
    const hasChildren = category.children && category.children.length > 0;

    return (
        <div className={`category-card ${isExpanded ? 'category-card--expanded' : ''}`}>
            <a
                href={`/category/${category.slug}`}
                className="category-card__link"
            >
                <div className="category-card__content">
                    {/* Category Icon/Image placeholder */}
                    <div className="category-card__icon">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                            <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 7.125C2.25 6.504 2.754 6 3.375 6h6c.621 0 1.125.504 1.125 1.125v3.75c0 .621-.504 1.125-1.125 1.125h-6a1.125 1.125 0 01-1.125-1.125v-3.75zM14.25 8.625c0-.621.504-1.125 1.125-1.125h5.25c.621 0 1.125.504 1.125 1.125v8.25c0 .621-.504 1.125-1.125 1.125h-5.25a1.125 1.125 0 01-1.125-1.125v-8.25zM3.75 16.125c0-.621.504-1.125 1.125-1.125h5.25c.621 0 1.125.504 1.125 1.125v2.25c0 .621-.504 1.125-1.125 1.125h-5.25a1.125 1.125 0 01-1.125-1.125v-2.25z" />
                        </svg>
                    </div>

                    <div className="category-card__info">
                        <h3 className="category-card__name">{category.name}</h3>
                        {category.nameAr && (
                            <span className="category-card__name-ar">{category.nameAr}</span>
                        )}
                        {hasChildren && (
                            <span className="category-card__count">
                                {category.children.length} subcategories
                            </span>
                        )}
                    </div>

                    <div className="category-card__arrow">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                            <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 4.5L21 12m0 0l-7.5 7.5M21 12H3" />
                        </svg>
                    </div>
                </div>
            </a>

            {/* Subcategories toggle button */}
            {hasChildren && (
                <button
                    className="category-card__toggle"
                    onClick={(e) => {
                        e.preventDefault();
                        onToggle?.();
                    }}
                    aria-expanded={isExpanded}
                    aria-label={`${isExpanded ? 'Hide' : 'Show'} subcategories`}
                >
                    <svg
                        viewBox="0 0 24 24"
                        fill="none"
                        stroke="currentColor"
                        strokeWidth="2"
                        className={`category-card__toggle-icon ${isExpanded ? 'rotate' : ''}`}
                    >
                        <path strokeLinecap="round" strokeLinejoin="round" d="M19 9l-7 7-7-7" />
                    </svg>
                </button>
            )}

            {/* Subcategories list */}
            {hasChildren && isExpanded && (
                <div className="category-card__subcategories">
                    {category.children.map((sub) => (
                        <a
                            key={sub.id}
                            href={`/category/${sub.slug}`}
                            className="category-card__subcategory"
                        >
                            <span className="category-card__subcategory-bullet" />
                            <span>{sub.name}</span>
                            {sub.children && sub.children.length > 0 && (
                                <span className="category-card__subcategory-count">
                                    +{sub.children.length}
                                </span>
                            )}
                        </a>
                    ))}
                </div>
            )}
        </div>
    );
};

// ============================================================================
// Main Category Page
// ============================================================================

const CategoryPage: React.FC = () => {
    // Get category page sections from builder config
    const config = useStoreConfigStore(selectConfig);
    const categorySections = config?.pages?.category?.sections ?? [];

    const [categories, setCategories] = useState<CategoryTreeDto[]>([]);
    const [loading, setLoading] = useState(true);
    const [error, setError] = useState<string | null>(null);
    const [expandedIds, setExpandedIds] = useState<Set<string>>(new Set());

    useEffect(() => {
        const loadCategories = async () => {
            try {
                setLoading(true);
                const data = await fetchCategories();
                setCategories(data);
                setError(null);
            } catch (err) {
                console.error('Failed to load categories:', err);
                setError('Failed to load categories. Please try again.');
            } finally {
                setLoading(false);
            }
        };

        loadCategories();
    }, []);

    const toggleExpanded = (id: string) => {
        setExpandedIds(prev => {
            const next = new Set(prev);
            if (next.has(id)) {
                next.delete(id);
            } else {
                next.add(id);
            }
            return next;
        });
    };

    const expandAll = () => {
        setExpandedIds(new Set(categories.map(c => c.id)));
    };

    const collapseAll = () => {
        setExpandedIds(new Set());
    };

    // Count total categories and subcategories
    const countAllCategories = (cats: CategoryTreeDto[]): number => {
        return cats.reduce((acc, cat) => {
            return acc + 1 + (cat.children ? countAllCategories(cat.children) : 0);
        }, 0);
    };

    const totalCount = countAllCategories(categories);
    const hasSubcategories = categories.some(c => c.children && c.children.length > 0);

    // Loading state
    if (loading) {
        return (
            <Layout>
                <div className="category-page category-page--loading">
                    <div className="container">
                        <div className="category-page__skeleton">
                            <div className="skeleton skeleton--title" />
                            <div className="category-page__skeleton-grid">
                                {[1, 2, 3, 4, 5, 6].map((i) => (
                                    <div key={i} className="skeleton skeleton--card" />
                                ))}
                            </div>
                        </div>
                    </div>
                </div>
            </Layout>
        );
    }

    // Error state
    if (error) {
        return (
            <Layout>
                <div className="category-page category-page--error">
                    <div className="container">
                        <div className="category-page__error">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
                            </svg>
                            <h2>Oops! Something went wrong</h2>
                            <p>{error}</p>
                            <button
                                onClick={() => window.location.reload()}
                                className="category-page__retry-btn"
                            >
                                Try Again
                            </button>
                        </div>
                    </div>
                </div>
            </Layout>
        );
    }

    // Empty state
    if (categories.length === 0) {
        return (
            <Layout>
                <div className="category-page category-page--empty">
                    <div className="container">
                        <div className="category-page__empty">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                                <path strokeLinecap="round" strokeLinejoin="round" d="M2.25 7.125C2.25 6.504 2.754 6 3.375 6h6c.621 0 1.125.504 1.125 1.125v3.75c0 .621-.504 1.125-1.125 1.125h-6a1.125 1.125 0 01-1.125-1.125v-3.75zM14.25 8.625c0-.621.504-1.125 1.125-1.125h5.25c.621 0 1.125.504 1.125 1.125v8.25c0 .621-.504 1.125-1.125 1.125h-5.25a1.125 1.125 0 01-1.125-1.125v-8.25zM3.75 16.125c0-.621.504-1.125 1.125-1.125h5.25c.621 0 1.125.504 1.125 1.125v2.25c0 .621-.504 1.125-1.125 1.125h-5.25a1.125 1.125 0 01-1.125-1.125v-2.25z" />
                            </svg>
                            <h2>No Categories Yet</h2>
                            <p>Check back soon for our product categories.</p>
                            <a href="/products" className="category-page__browse-btn">
                                Browse All Products
                            </a>
                        </div>
                    </div>
                </div>
            </Layout>
        );
    }

    return (
        <Layout>
            {/* If builder has sections, render them; otherwise show default category grid */}
            {categorySections.length > 0 ? (
                <div className="category-page">
                    {/* Builder-defined sections */}
                    {categorySections
                        .filter(s => s.enabled)
                        .sort((a, b) => a.order - b.order)
                        .map((section) => (
                            <SectionRenderer
                                key={section.id}
                                section={section}
                            />
                        ))}
                </div>
            ) : (
                <div className="category-page">
                    {/* Default Category Page Layout */}
                    {/* Hero Section */}
                    <div className="category-page__hero">
                        <div className="container">
                            <div className="category-page__hero-content">
                                <h1 className="category-page__title">Shop by Category</h1>
                                <p className="category-page__subtitle">
                                    Explore our {totalCount} categories to find exactly what you're looking for
                                </p>
                            </div>
                        </div>
                        <div className="category-page__hero-bg" />
                    </div>

                    {/* Main Content */}
                    <div className="container">
                        {/* Actions bar */}
                        {hasSubcategories && (
                            <div className="category-page__actions">
                                <span className="category-page__count">
                                    {categories.length} main categories
                                </span>
                                <div className="category-page__action-buttons">
                                    <button
                                        onClick={expandAll}
                                        className="category-page__action-btn"
                                    >
                                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                            <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 3.75v4.5m0-4.5h4.5m-4.5 0L9 9M3.75 20.25v-4.5m0 4.5h4.5m-4.5 0L9 15M20.25 3.75h-4.5m4.5 0v4.5m0-4.5L15 9m5.25 11.25h-4.5m4.5 0v-4.5m0 4.5L15 15" />
                                        </svg>
                                        Expand All
                                    </button>
                                    <button
                                        onClick={collapseAll}
                                        className="category-page__action-btn"
                                    >
                                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                            <path strokeLinecap="round" strokeLinejoin="round" d="M9 9V4.5M9 9H4.5M9 9L3.75 3.75M9 15v4.5M9 15H4.5M9 15l-5.25 5.25M15 9h4.5M15 9V4.5M15 9l5.25-5.25M15 15h4.5M15 15v4.5m0-4.5l5.25 5.25" />
                                        </svg>
                                        Collapse All
                                    </button>
                                </div>
                            </div>
                        )}

                        {/* Categories Grid */}
                        <div className="category-page__grid">
                            {categories.map((category) => (
                                <CategoryCard
                                    key={category.id}
                                    category={category}
                                    isExpanded={expandedIds.has(category.id)}
                                    onToggle={() => toggleExpanded(category.id)}
                                />
                            ))}
                        </div>

                        {/* Browse all CTA */}
                        <div className="category-page__cta">
                            <p>Can't find what you're looking for?</p>
                            <a href="/products" className="category-page__cta-btn">
                                Browse All Products
                                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                                    <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 4.5L21 12m0 0l-7.5 7.5M21 12H3" />
                                </svg>
                            </a>
                        </div>
                    </div>
                </div>
            )}
        </Layout>
    );
};

export default CategoryPage;
