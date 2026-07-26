/**
 * AF-27B §8: Landing Page Templates Module
 * 
 * Barrel export + template registration.
 * Import this module to ensure all templates are registered.
 */

export { registerTemplate, getTemplate, hasTemplate, getRegisteredTemplates } from './TemplateRegistry';
export type { LandingTemplateProps } from './TemplateRegistry';
export { ClassicTemplate } from './ClassicTemplate';
export { StorytellingTemplate } from './StorytellingTemplate';
export { MinimalFastCheckoutTemplate } from './MinimalFastCheckoutTemplate';

// ============================================================================
// Auto-register templates on module import
// ============================================================================
import { registerTemplate } from './TemplateRegistry';
import { ClassicTemplate } from './ClassicTemplate';
import { StorytellingTemplate } from './StorytellingTemplate';
import { MinimalFastCheckoutTemplate } from './MinimalFastCheckoutTemplate';

// Defense-in-depth: register non-versioned 'Classic' alias
// for any edge case where backend sends unversioned string
registerTemplate('Classic', ClassicTemplate);

// AF-28: Register all production templates (versioned)
registerTemplate('Classic:v1', ClassicTemplate);
registerTemplate('Storytelling:v1', StorytellingTemplate);
registerTemplate('MinimalFastCheckout:v1', MinimalFastCheckoutTemplate);
