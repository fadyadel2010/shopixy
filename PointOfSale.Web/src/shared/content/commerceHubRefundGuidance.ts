export interface BilingualCopy {
    en: string
    ar: string
}

export interface VendorRefundGuidanceContent {
    title: BilingualCopy
    subtitle: BilingualCopy
    workflow: BilingualCopy
    returnStage: BilingualCopy
    warning: BilingualCopy
}

export interface SuperAdminRefundGuidanceContent {
    title: BilingualCopy
    subtitle: BilingualCopy
    summary: BilingualCopy
    steps: BilingualCopy[]
    importantTitle: BilingualCopy
    warnings: BilingualCopy[]
}

export const commerceHubRefundGuidanceContent: {
    vendor: VendorRefundGuidanceContent
    superAdmin: SuperAdminRefundGuidanceContent
} = {
    vendor: {
        title: {
            en: 'Refund Workflow Explanation',
            ar: 'شرح مسار الاسترجاع',
        },
        subtitle: {
            en: 'Help vendors understand who handles the money, who handles the returned item, and what to do next.',
            ar: 'يساعد هذا الشرح البائع على معرفة من يتعامل مع المبلغ، ومن يستلم المنتج المرتجع، وما الخطوة التالية.',
        },
        workflow: {
            en: 'When a refund is approved, the platform returns the money to the customer. You are responsible for receiving the returned product.',
            ar: 'عند الموافقة على طلب الاسترجاع، تقوم المنصة بإعادة المبلغ للعميل. أنت مسؤول عن استلام المنتج المرتجع.',
        },
        returnStage: {
            en: 'Please arrange product pickup and confirm once received.',
            ar: 'يرجى ترتيب استلام المنتج والتأكيد بعد استلامه.',
        },
        warning: {
            en: 'If the product is not received, you may be financially liable.',
            ar: 'في حالة عدم استلام المنتج، قد تتحمل المسؤولية المالية.',
        },
    },
    superAdmin: {
        title: {
            en: 'Admin Guidance Panel',
            ar: 'لوحة إرشادات المشرف',
        },
        subtitle: {
            en: 'Use this panel to keep the refund flow controlled, traceable, and safe for all parties.',
            ar: 'استخدم هذه اللوحة للحفاظ على مسار الاسترجاع منظمًا وقابلًا للتتبع وآمنًا لجميع الأطراف.',
        },
        summary: {
            en: 'This refund follows a controlled workflow:',
            ar: 'تمر عملية الاسترجاع بعدة مراحل:',
        },
        steps: [
            {
                en: '1. Customer submits request with evidence',
                ar: '1. يقوم العميل بتقديم الطلب مع الأدلة',
            },
            {
                en: '2. You review and approve/reject',
                ar: '2. تقوم بمراجعة الطلب والموافقة أو الرفض',
            },
            {
                en: '3. Return process is initiated',
                ar: '3. تبدأ عملية إرجاع المنتج',
            },
            {
                en: '4. Refund moves to manual payout once the return milestone is met',
                ar: '4. ينتقل طلب الاسترجاع إلى التحويل اليدوي بعد تحقق مرحلة الإرجاع المطلوبة',
            },
        ],
        importantTitle: {
            en: 'Important',
            ar: 'هام',
        },
        warnings: [
            {
                en: 'Do not confirm payout before the return conditions are met and the money is actually sent',
                ar: 'لا تؤكد تحويل المبلغ قبل تحقق شروط الإرجاع والتأكد من إرسال المبلغ فعليًا',
            },
            {
                en: 'Always verify evidence before approval',
                ar: 'تأكد من مراجعة الأدلة قبل الموافقة',
            },
        ],
    },
}

export function getGuidanceLanguage(language: string | undefined): 'en' | 'ar' {
    return String(language || 'en').toLowerCase().startsWith('ar') ? 'ar' : 'en'
}
