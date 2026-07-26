/**
 * CMS Page Route Component
 * Phase 9.11: Static CMS Pages Engine
 * 
 * Route wrapper for CMS pages that gets the slug from URL params
 */

import React from 'react';
import { useParams, useSearchParams } from 'react-router-dom';
import { Layout } from '@/components/layout';
import { CmsPageView } from '@/components/cms/CmsPage';

export const CmsPage: React.FC = () => {
    const { slug } = useParams<{ slug: string }>();
    const [searchParams] = useSearchParams();
    const preview = searchParams.get('preview') === 'true';
    const previewToken = searchParams.get('previewToken');

    if (!slug) {
        return (
            <Layout>
                <div className="container mx-auto px-4 py-12 text-center">
                    <h1 className="text-2xl font-bold mb-4">Page Not Found</h1>
                    <p className="text-gray-600 mb-8">The page you're looking for doesn't exist.</p>
                    <a href="/" className="btn btn-primary">Go Home</a>
                </div>
            </Layout>
        );
    }

    return (
        <Layout>
            <CmsPageView slug={slug} preview={preview} previewToken={previewToken} />
        </Layout>
    );
};

export default CmsPage;
