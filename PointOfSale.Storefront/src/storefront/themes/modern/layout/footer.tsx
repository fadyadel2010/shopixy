import React from 'react';
import DefaultFooter from '../../../../components/layout/default/footer';
import type { FooterPresentationProps } from '@/storefront/presentation/models';

const ModernFooter: React.FC<FooterPresentationProps> = (props) => <DefaultFooter {...props} />;

export default ModernFooter;
