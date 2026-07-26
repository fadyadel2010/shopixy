/**
 * VariantSelector Component
 * 
 * PHASE 11.3 — STOREFRONT VARIANT SELECTION
 * 
 * Shopify-grade variant selector UI that displays:
 * - Attribute groups (Color, Size, etc.)
 * - Selectable options with visual feedback
 * - Disabled state for unavailable combinations
 */

import React from 'react';
import type { AttributeGroup, SelectedAttributes } from '@/hooks/useVariantSelector';
import './VariantSelector.css';

interface VariantSelectorProps {
    /** Attribute groups to display */
    attributeGroups: AttributeGroup[];
    /** Currently selected attributes */
    selectedAttributes: SelectedAttributes;
    /** Callback when an attribute is selected */
    onSelect: (attributeName: string, value: string) => void;
    /** Check if a value is available */
    isValueAvailable?: (attributeName: string, value: string) => boolean;
}

/**
 * VariantSelector displays attribute selectors for product variants.
 * 
 * Example:
 * ```
 * Color: [Red] [Blue] [Green]
 * Size:  [S] [M] [L] [XL]
 * ```
 */
export const VariantSelector: React.FC<VariantSelectorProps> = ({
    attributeGroups,
    selectedAttributes,
    onSelect,
    isValueAvailable = () => true,
}) => {
    if (!attributeGroups || attributeGroups.length === 0) {
        return null;
    }

    return (
        <div className="variant-selector">
            {attributeGroups.map((group) => (
                <div key={group.name} className="variant-group">
                    <label className="variant-label">
                        {group.name}
                        <span className="variant-selected-value">
                            : {selectedAttributes[group.name] || '—'}
                        </span>
                    </label>
                    <div className="variant-options">
                        {group.values.map((value) => {
                            const isSelected = selectedAttributes[group.name] === value;
                            const isAvailable = isValueAvailable(group.name, value);

                            return (
                                <button
                                    key={value}
                                    type="button"
                                    className={`variant-option ${isSelected ? 'selected' : ''} ${!isAvailable ? 'unavailable' : ''}`}
                                    onClick={() => onSelect(group.name, value)}
                                    disabled={!isAvailable}
                                    aria-pressed={isSelected}
                                    aria-disabled={!isAvailable}
                                    title={!isAvailable ? 'This option is currently unavailable' : undefined}
                                >
                                    {value}
                                    {!isAvailable && (
                                        <span className="unavailable-indicator" />
                                    )}
                                </button>
                            );
                        })}
                    </div>
                </div>
            ))}
        </div>
    );
};

export default VariantSelector;
