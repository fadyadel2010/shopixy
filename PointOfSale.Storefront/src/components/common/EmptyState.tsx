/**
 * Empty State Component
 * 
 * Displayed when a list/grid has no items.
 */

import React from 'react';

interface EmptyStateProps {
    title?: string;
    message?: string;
    icon?: React.ReactNode;
}

export const EmptyState: React.FC<EmptyStateProps> = ({
    title = 'No items found',
    message = 'There are no items to display.',
    icon,
}) => {
    return (
        <div className="flex flex-col items-center justify-center py-16 text-center">
            {icon || (
                <div className="text-5xl mb-4 opacity-50">📦</div>
            )}
            <h3 className="text-lg font-semibold text-text mb-2">{title}</h3>
            <p className="text-muted">{message}</p>
        </div>
    );
};
