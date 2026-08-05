import React from 'react';
import { Footer } from '../Footer';
import type { FooterPresentationProps } from '@/storefront/presentation/models';

const DefaultFooter: React.FC<FooterPresentationProps> = (props) => <Footer {...props} />;

export default DefaultFooter;
