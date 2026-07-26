export interface BilingualCopy {
    en: string;
    ar: string;
}

export interface CustomerRefundGuidanceEntry {
    explanation: BilingualCopy;
    next: BilingualCopy;
    action: BilingualCopy;
}

export const commerceHubCustomerRefundGuidance = {
    sectionTitle: {
        en: 'Refund Status Explanation',
        ar: 'شرح حالة الاسترجاع',
    },
    sectionSubtitle: {
        en: 'See what is happening now, what happens next, and whether you need to do anything.',
        ar: 'اعرف ما الذي يحدث الآن، وما الخطوة التالية، وهل تحتاج إلى اتخاذ أي إجراء.',
    },
    labels: {
        now: {
            en: 'Now',
            ar: 'الآن',
        },
        next: {
            en: 'Next',
            ar: 'التالي',
        },
        action: {
            en: 'Action Required',
            ar: 'الإجراء المطلوب',
        },
        guarantee: {
            en: 'Refund Guarantee',
            ar: 'ضمان الاسترداد',
        },
    },
    guaranteeMessage: {
        en: 'Your refund is guaranteed once the item is returned.',
        ar: 'استرداد المبلغ مضمون بمجرد استلام المنتج.',
    },
    entries: {
        Requested: {
            explanation: {
                en: 'Your refund request has been submitted and is under review.',
                ar: 'تم استلام طلب الاسترجاع الخاص بك وجاري مراجعته.',
            },
            next: {
                en: 'Our team will review the request and update the result here.',
                ar: 'سيقوم فريقنا بمراجعة الطلب وتحديث النتيجة هنا.',
            },
            action: {
                en: 'No action is required right now.',
                ar: 'لا يوجد أي إجراء مطلوب منك الآن.',
            },
        },
        Approved: {
            explanation: {
                en: 'Your refund request has been approved. Please return the item so we can process your refund.',
                ar: 'تمت الموافقة على طلب الاسترجاع. يرجى تسليم المنتج لإتمام عملية استرداد المبلغ.',
            },
            next: {
                en: 'The return step will be arranged and your refund will move forward after the item is handed over.',
                ar: 'سيتم ترتيب خطوة الإرجاع، وسيتقدم طلب الاسترداد بعد تسليم المنتج.',
            },
            action: {
                en: 'Please keep the item ready for return and follow the pickup updates below.',
                ar: 'يرجى تجهيز المنتج للإرجاع ومتابعة تحديثات الاستلام أدناه.',
            },
        },
        WaitingForReturn: {
            explanation: {
                en: 'Your refund request has been approved. We are waiting for the returned item to reach the required checkpoint.',
                ar: 'تمت الموافقة على طلب الاسترجاع. ننتظر وصول المنتج المرتجع إلى نقطة التحقق المطلوبة.',
            },
            next: {
                en: 'Once the vendor return milestone is reached, the refund will move into payout processing.',
                ar: 'عندما تصل عملية الإرجاع إلى المرحلة المطلوبة، سينتقل الاسترداد إلى مرحلة تحويل المبلغ.',
            },
            action: {
                en: 'Please keep following the pickup and return updates below.',
                ar: 'يرجى متابعة تحديثات الاستلام والإرجاع أدناه.',
            },
        },
        AwaitingPayout: {
            explanation: {
                en: 'Your refund has been approved and is being processed. The payment will be sent shortly.',
                ar: 'تمت الموافقة على طلب الاسترجاع وجاري تحويل المبلغ إليك قريبًا.',
            },
            next: {
                en: 'The platform team will send the refund to your saved payout method and confirm it here.',
                ar: 'سيقوم فريق المنصة بتحويل المبلغ إلى وسيلة الاسترداد المحفوظة وتأكيد ذلك هنا.',
            },
            action: {
                en: 'No action is required from you right now.',
                ar: 'لا يوجد إجراء مطلوب منك الآن.',
            },
        },
        ReadyForExecution: {
            explanation: {
                en: 'Your refund has been approved and is being processed. The payment will be sent shortly.',
                ar: 'تمت الموافقة على طلب الاسترجاع وجاري تحويل المبلغ إليك قريبًا.',
            },
            next: {
                en: 'The platform team will send the refund to your saved payout method and confirm it here.',
                ar: 'سيقوم فريق المنصة بتحويل المبلغ إلى وسيلة الاسترداد المحفوظة وتأكيد ذلك هنا.',
            },
            action: {
                en: 'No action is required from you right now.',
                ar: 'لا يوجد إجراء مطلوب منك الآن.',
            },
        },
        PickupScheduled: {
            explanation: {
                en: 'A courier will collect the item from you at the scheduled time.',
                ar: 'سيقوم مندوب الشحن باستلام المنتج منك في الموعد المحدد.',
            },
            next: {
                en: 'Once the courier collects the item, your refund will continue to the next step.',
                ar: 'بمجرد استلام المنتج من شركة الشحن، ستنتقل عملية الاسترداد إلى الخطوة التالية.',
            },
            action: {
                en: 'Please hand over the item at the scheduled pickup time.',
                ar: 'يرجى تسليم المنتج في موعد الاستلام المحدد.',
            },
        },
        PickedUp: {
            explanation: {
                en: 'The item has been collected. Your refund will be processed shortly.',
                ar: 'تم استلام المنتج من طرف شركة الشحن. سيتم تنفيذ عملية الاسترداد قريبًا.',
            },
            next: {
                en: 'We are waiting for the configured return checkpoint so the refund can be released safely.',
                ar: 'نحن بانتظار نقطة التحقق المحددة للإرجاع حتى يتم تحويل المبلغ بأمان.',
            },
            action: {
                en: 'No action is needed unless support contacts you for clarification.',
                ar: 'لا يوجد إجراء مطلوب منك إلا إذا تواصل معك الدعم لطلب توضيح.',
            },
        },
        Paid: {
            explanation: {
                en: 'Your refund has been sent successfully.',
                ar: 'تم تحويل المبلغ إليك بنجاح.',
            },
            next: {
                en: 'The refund flow is complete and no further steps are pending.',
                ar: 'اكتملت عملية الاسترجاع ولا توجد خطوات إضافية معلقة.',
            },
            action: {
                en: 'No action is required.',
                ar: 'لا يوجد أي إجراء مطلوب.',
            },
        },
        Completed: {
            explanation: {
                en: 'Your refund has been sent successfully.',
                ar: 'تم تحويل المبلغ إليك بنجاح.',
            },
            next: {
                en: 'The refund flow is complete and no further steps are pending.',
                ar: 'اكتملت عملية الاسترجاع ولا توجد خطوات إضافية معلقة.',
            },
            action: {
                en: 'No action is required.',
                ar: 'لا يوجد أي إجراء مطلوب.',
            },
        },
        Executed: {
            explanation: {
                en: 'Your refund has been sent successfully.',
                ar: 'تم تحويل المبلغ إليك بنجاح.',
            },
            next: {
                en: 'The refund flow is complete and no further steps are pending.',
                ar: 'اكتملت عملية الاسترجاع ولا توجد خطوات إضافية معلقة.',
            },
            action: {
                en: 'No action is required.',
                ar: 'لا يوجد أي إجراء مطلوب.',
            },
        },
        Rejected: {
            explanation: {
                en: 'Your refund request was not approved.',
                ar: 'لم تتم الموافقة على طلب الاسترجاع الخاص بك.',
            },
            next: {
                en: 'Please review the notes in this refund entry or contact support if you need help.',
                ar: 'يرجى مراجعة الملاحظات في هذا الطلب أو التواصل مع الدعم إذا كنت بحاجة إلى مساعدة.',
            },
            action: {
                en: 'No return action is required unless support advises otherwise.',
                ar: 'لا يوجد إجراء إرجاع مطلوب إلا إذا أوضح لك الدعم خلاف ذلك.',
            },
        },
        Failed: {
            explanation: {
                en: 'Your refund could not be completed yet.',
                ar: 'تعذر إكمال عملية الاسترجاع حتى الآن.',
            },
            next: {
                en: 'Our team will review the issue and update this refund once the problem is resolved.',
                ar: 'سيقوم فريقنا بمراجعة المشكلة وتحديث حالة الاسترداد بعد حلها.',
            },
            action: {
                en: 'Please wait for an update or contact support if the delay continues.',
                ar: 'يرجى انتظار التحديث أو التواصل مع الدعم إذا استمر التأخير.',
            },
        },
        Cancelled: {
            explanation: {
                en: 'This refund request has been cancelled.',
                ar: 'تم إلغاء طلب الاسترجاع هذا.',
            },
            next: {
                en: 'No further refund steps will be taken for this request.',
                ar: 'لن يتم اتخاذ خطوات إضافية للاسترداد لهذا الطلب.',
            },
            action: {
                en: 'No action is required.',
                ar: 'لا يوجد أي إجراء مطلوب.',
            },
        },
    } satisfies Record<string, CustomerRefundGuidanceEntry>,
} as const;
