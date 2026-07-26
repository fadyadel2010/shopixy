# Commerce AI Admin Control - Translation Keys

## 📝 Translation Files

### English (en.json)
Add these keys to `PointOfSale.Web/src/i18n/locales/en.json`:

```json
{
  "commerceAi": {
    "title": "Commerce AI Assistant",
    "subtitle": "Help customers ask questions and prepare orders — safely and automatically.",
    "toggleTitle": "Enable AI Assistant on Storefront",
    "currentlyEnabled": "Currently active on your online store",
    "currentlyDisabled": "Not visible to customers",
    "statusEnabled": "Enabled",
    "statusDisabled": "Disabled",
    "since": "Since",
    "enabled": "Commerce AI has been enabled",
    "disabled": "Commerce AI has been disabled",
    "toggleFailed": "Failed to update Commerce AI settings",
    
    "whatItCanDo": "What the AI can do",
    "canDo1": "Answer customer questions about products",
    "canDo2": "Explain prices and shipping options",
    "canDo3": "Help customers prepare an order",
    "canDo4": "Explain order & delivery status",
    
    "whatItCannotDo": "What the AI cannot do",
    "cannotDo1": "Place orders",
    "cannotDo2": "Change prices",
    "cannotDo3": "Cancel orders",
    "cannotDo4": "Trigger payments",
    "cannotDo5": "Contact shipping companies",
    "cannotDo6": "Act without customer input",
    "controlNote": "Everything remains fully under your control.",
    
    "safetyPrivacy": "Safety & Privacy",
    "safety1": "The AI only sees your store data",
    "safety2": "No customer data is shared externally",
    "safety3": "All actions are read-only",
    "safety4": "You can disable the feature at any time",
    
    "systemStatus": "System Status",
    "status": "Status",
    "aiModels": "AI Models",
    "mode": "Mode",
    "noConfigNote": "No configuration required. No API keys. No additional costs."
  },
  
  "chat": {
    "title": "AI Assistant",
    "status": {
      "online": "Online"
    },
    "minimize": "Minimize",
    "close": "Close",
    "open": "Open chat",
    "welcome": "Hi! 👋 How can I help you today?",
    "minimized": "Chat with us",
    "error": {
      "send": "Failed to send message. Please try again.",
      "fallback": "I'm having trouble connecting. Please try again in a moment."
    }
  }
}
```

### Arabic (ar.json)
Add these keys to `PointOfSale.Web/src/i18n/locales/ar.json`:

```json
{
  "commerceAi": {
    "title": "مساعد التجارة الذكي",
    "subtitle": "ساعد العملاء في طرح الأسئلة وإعداد الطلبات — بشكل آمن وتلقائي.",
    "toggleTitle": "تفعيل المساعد الذكي على المتجر الإلكتروني",
    "currentlyEnabled": "نشط حالياً على متجرك الإلكتروني",
    "currentlyDisabled": "غير مرئي للعملاء",
    "statusEnabled": "مفعّل",
    "statusDisabled": "معطّل",
    "since": "منذ",
    "enabled": "تم تفعيل المساعد الذكي",
    "disabled": "تم تعطيل المساعد الذكي",
    "toggleFailed": "فشل تحديث إعدادات المساعد الذكي",
    
    "whatItCanDo": "ما يمكن للذكاء الاصطناعي فعله",
    "canDo1": "الإجابة على أسئلة العملاء حول المنتجات",
    "canDo2": "شرح الأسعار وخيارات الشحن",
    "canDo3": "مساعدة العملاء في إعداد الطلب",
    "canDo4": "شرح حالة الطلب والتسليم",
    
    "whatItCannotDo": "ما لا يمكن للذكاء الاصطناعي فعله",
    "cannotDo1": "إنشاء الطلبات",
    "cannotDo2": "تغيير الأسعار",
    "cannotDo3": "إلغاء الطلبات",
    "cannotDo4": "تفعيل المدفوعات",
    "cannotDo5": "الاتصال بشركات الشحن",
    "cannotDo6": "التصرف دون موافقة العميل",
    "controlNote": "كل شيء يبقى تحت سيطرتك الكاملة.",
    
    "safetyPrivacy": "الأمان والخصوصية",
    "safety1": "الذكاء الاصطناعي يرى بيانات متجرك فقط",
    "safety2": "لا يتم مشاركة بيانات العملاء خارجياً",
    "safety3": "جميع الإجراءات للقراءة فقط",
    "safety4": "يمكنك تعطيل الميزة في أي وقت",
    
    "systemStatus": "حالة النظام",
    "status": "الحالة",
    "aiModels": "نماذج الذكاء الاصطناعي",
    "mode": "الوضع",
    "noConfigNote": "لا حاجة للتكوين. لا مفاتيح API. لا تكاليف إضافية."
  },
  
  "chat": {
    "title": "المساعد الذكي",
    "status": {
      "online": "متصل"
    },
    "minimize": "تصغير",
    "close": "إغلاق",
    "open": "فتح المحادثة",
    "welcome": "مرحباً! 👋 كيف يمكنني مساعدتك اليوم؟",
    "minimized": "تحدث معنا",
    "error": {
      "send": "فشل إرسال الرسالة. يرجى المحاولة مرة أخرى.",
      "fallback": "أواجه مشكلة في الاتصال. يرجى المحاولة مرة أخرى بعد قليل."
    }
  }
}
```

## 🔄 Adding Translations to Your Project

### For Admin UI (PointOfSale.Web)

1. Open `PointOfSale.Web/src/i18n/locales/en.json`
2. Add the English translations above
3. Open `PointOfSale.Web/src/i18n/locales/ar.json`
4. Add the Arabic translations above
5. Save both files

### For Storefront (PointOfSale.Storefront)

1. Open `PointOfSale.Storefront/src/i18n/locales/en.json`
2. Add the chat-related translations
3. Open `PointOfSale.Storefront/src/i18n/locales/ar.json`
4. Add the Arabic chat translations
5. Save both files

## 📋 Translation Coverage

### Admin UI Translations
- ✅ Page title and subtitle
- ✅ Toggle button labels
- ✅ Status indicators
- ✅ Success/error messages
- ✅ Educational content (what AI can/cannot do)
- ✅ Safety & privacy guarantees
- ✅ System status labels

### Storefront Translations
- ✅ Chat widget title
- ✅ Status indicators
- ✅ Button labels (minimize, close, open)
- ✅ Welcome message
- ✅ Error messages
- ✅ Minimized state label

## 🌍 RTL Support

The Commerce AI Admin Control Page automatically supports RTL (Right-to-Left) layout for Arabic:

```typescript
const isRTL = i18n.language === 'ar';

<div dir={isRTL ? 'rtl' : 'ltr'}>
  {/* Content */}
</div>
```

### RTL Features:
- ✅ Text alignment (right-aligned for Arabic)
- ✅ Layout direction (mirrored for Arabic)
- ✅ Icon positioning (adjusted for RTL)
- ✅ Toggle switch direction (adjusted for RTL)

## 🎨 Styling Considerations

### CSS Classes for RTL
The component uses conditional classes for RTL support:

```css
.chat-widget--rtl {
  direction: rtl;
  text-align: right;
}
```

### Flexbox Direction
Flexbox layouts automatically adjust for RTL when `dir="rtl"` is set on the parent element.

## ✅ Testing Translations

### Test English:
1. Set Admin UI language to English
2. Navigate to `/admin/ecommerce/commerce-ai`
3. Verify all text is in English
4. Toggle AI on/off and verify messages

### Test Arabic:
1. Set Admin UI language to Arabic
2. Navigate to `/admin/ecommerce/commerce-ai`
3. Verify all text is in Arabic
4. Verify RTL layout is correct
5. Toggle AI on/off and verify messages

## 📝 Adding More Languages

To add support for additional languages:

1. Create a new locale file (e.g., `fr.json` for French)
2. Copy the English translations
3. Translate all values to the target language
4. Add the locale to your i18n configuration
5. Test the new language in the UI

## 🔤 Translation Guidelines

### Keep It Simple
- Use clear, non-technical language
- Avoid jargon and acronyms
- Keep sentences short and direct

### Be Consistent
- Use the same terms throughout
- Maintain consistent tone and style
- Follow platform-specific conventions

### Test Thoroughly
- Verify translations in context
- Check for text overflow in UI
- Test RTL layout for Arabic
- Verify all placeholders work correctly

## 🆘 Translation Issues

If you encounter translation issues:

1. **Missing translations**: Check that all keys are present in locale files
2. **Text overflow**: Adjust UI layout or shorten translations
3. **RTL issues**: Verify `dir="rtl"` is set correctly
4. **Wrong language**: Check i18n configuration and language detection

## 📚 Resources

- [i18next Documentation](https://www.i18next.com/)
- [React i18next](https://react.i18next.com/)
- [RTL Styling Guide](https://rtlstyling.com/)
- [Arabic Typography Best Practices](https://www.w3.org/International/articles/arabic-type/)
