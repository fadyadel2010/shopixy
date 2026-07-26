import { useState, useEffect, useRef, useCallback } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useAuthStore } from '../store/authStore'
import api from '../services/api'

/**
 * Tenant Registration Page
 * Phase 8.2: Implementation following Phase 8.1 UX Design exactly
 * 
 * Single-page registration with:
 * - Business Name
 * - Email
 * - Password (with show/hide toggle)
 * - Store Address (subdomain with real-time availability check)
 */
export default function Register() {
    const { t, i18n } = useTranslation()
    const navigate = useNavigate()
    const { isAuthenticated, login } = useAuthStore()
    const isRTL = i18n.resolvedLanguage === 'ar'

    // Redirect if already authenticated
    useEffect(() => {
        if (isAuthenticated) {
            navigate('/', { replace: true })
        }
    }, [isAuthenticated, navigate])

    // Form state
    const [formData, setFormData] = useState({
        businessName: '',
        email: '',
        password: '',
        subdomain: ''
    })

    // Validation state
    const [errors, setErrors] = useState({})
    const [touched, setTouched] = useState({})

    // Subdomain availability state
    const [subdomainStatus, setSubdomainStatus] = useState({
        checking: false,
        available: null,
        suggestions: [],
        message: ''
    })

    // Email check state
    const [emailStatus, setEmailStatus] = useState({
        checking: false,
        registered: false
    })

    // Form submission state
    const [isSubmitting, setIsSubmitting] = useState(false)
    const [submitError, setSubmitError] = useState('')

    // Password visibility
    const [showPassword, setShowPassword] = useState(false)

    // Refs for focus management
    const businessNameRef = useRef(null)
    const emailRef = useRef(null)
    const passwordRef = useRef(null)
    const subdomainRef = useRef(null)
    const emailCheckRequestIdRef = useRef(0)
    const currentEmailRef = useRef('')
    const latestSubdomainRef = useRef(formData.subdomain)

    // Auto-focus first field on mount
    useEffect(() => {
        businessNameRef.current?.focus()
    }, [])

    useEffect(() => {
        currentEmailRef.current = formData.email
    }, [formData.email])

    // Platform domain for preview
    const platformDomain = 'myplatform.com'

    // ==========================================================================
    // Validation
    // ==========================================================================

    const validateField = useCallback((name, value) => {
        switch (name) {
            case 'businessName':
                if (!value.trim()) return t('tenantRegistration.validation.businessNameRequired')
                if (value.trim().length < 2) return t('tenantRegistration.validation.businessNameTooShort')
                if (value.trim().length > 100) return t('tenantRegistration.validation.businessNameTooLong')
                return ''

            case 'email':
                if (!value.trim()) return t('tenantRegistration.validation.emailRequired')
                if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)) return t('tenantRegistration.validation.emailInvalid')
                return ''

            case 'password':
                if (!value) return t('tenantRegistration.validation.passwordRequired')
                if (value.length < 8) return t('tenantRegistration.validation.passwordTooShort')
                return ''

            case 'subdomain':
                if (!value.trim()) return t('tenantRegistration.validation.storeAddressRequired')
                if (value.length < 3) return t('tenantRegistration.validation.storeAddressTooShort')
                if (value.length > 63) return t('tenantRegistration.validation.storeAddressTooLong')
                if (!/^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$/.test(value)) {
                    if (value.includes('--')) return t('tenantRegistration.validation.storeAddressExtraHyphens')
                    if (!/^[a-z0-9]/.test(value)) return t('tenantRegistration.validation.storeAddressStartsInvalid')
                    if (!/[a-z0-9]$/.test(value)) return t('tenantRegistration.validation.storeAddressEndsInvalid')
                    return t('tenantRegistration.validation.storeAddressInvalidCharacters')
                }
                return ''

            default:
                return ''
        }
    }, [t])

    // ==========================================================================
    // Generate subdomain from business name
    // ==========================================================================

    const generateSubdomain = useCallback((businessName) => {
        if (!businessName.trim()) return ''

        let slug = businessName.toLowerCase()
            .trim()
            .replace(/[^a-z0-9\s-]/g, '') // Remove special chars
            .replace(/\s+/g, '-')          // Spaces to hyphens
            .replace(/-+/g, '-')           // Collapse multiple hyphens
            .replace(/^-|-$/g, '')         // Trim hyphens

        // Ensure valid length
        if (slug.length < 3) return ''
        if (slug.length > 63) slug = slug.substring(0, 63)

        return slug
    }, [])

    // ==========================================================================
    // Subdomain availability check (debounced)
    // ==========================================================================

    const checkSubdomainAvailability = useCallback(async (subdomain) => {
        const requestedSlug = subdomain
        const isCurrentSubdomainRequest = () => requestedSlug === latestSubdomainRef.current

        if (!requestedSlug || requestedSlug.length < 3) {
            if (!isCurrentSubdomainRequest()) {
                return
            }

            setSubdomainStatus({ checking: false, available: null, suggestions: [], message: '' })
            return
        }

        // Validate format first
        const error = validateField('subdomain', requestedSlug)
        if (error) {
            if (!isCurrentSubdomainRequest()) {
                return
            }

            setSubdomainStatus({ checking: false, available: false, suggestions: [], message: error })
            return
        }

        if (!isCurrentSubdomainRequest()) {
            return
        }

        setSubdomainStatus(prev => ({ ...prev, checking: true }))

        try {
            const response = await api.get(`/platform/subdomain/check?slug=${encodeURIComponent(requestedSlug)}`)
            const { available, suggestions, reason } = response.data

            if (!isCurrentSubdomainRequest()) {
                return
            }

            setSubdomainStatus({
                checking: false,
                available,
                suggestions: suggestions || [],
                message: available
                    ? t('tenantRegistration.status.addressAvailable')
                    : (reason || t('tenantRegistration.status.addressTaken'))
            })
        } catch (err) {
            if (!isCurrentSubdomainRequest()) {
                return
            }

            console.error('Subdomain check failed:', err)
            setSubdomainStatus({
                checking: false,
                available: null,
                suggestions: [],
                message: t('tenantRegistration.status.addressCheckUnavailable')
            })
        }
    }, [t, validateField])

    // Debounced subdomain check
    useEffect(() => {
        const requestedSlug = formData.subdomain
        const timer = setTimeout(() => {
            if (requestedSlug && touched.subdomain && requestedSlug === latestSubdomainRef.current) {
                void checkSubdomainAvailability(requestedSlug)
            }
        }, 400) // 400ms debounce as per Phase 8.1

        return () => clearTimeout(timer)
    }, [formData.subdomain, touched.subdomain, checkSubdomainAvailability])

    // ==========================================================================
    // Email existence check (debounced)
    // ==========================================================================

    const checkEmailExists = useCallback(async (email, requestId) => {
        const isCurrentEmailRequest = () => (
            requestId === emailCheckRequestIdRef.current &&
            currentEmailRef.current === email
        )

        if (!email || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
            if (isCurrentEmailRequest()) {
                setEmailStatus({ checking: false, registered: false })
            }
            return
        }

        if (!isCurrentEmailRequest()) {
            return
        }

        setEmailStatus(prev => ({ ...prev, checking: true }))

        try {
            const response = await api.get(`/platform/email/check?email=${encodeURIComponent(email)}`)

            if (!isCurrentEmailRequest()) {
                return
            }

            setEmailStatus({
                checking: false,
                registered: response.data.isRegistered
            })

            if (response.data.isRegistered) {
                setErrors(prev => ({ ...prev, email: t('tenantRegistration.validation.emailRegistered') }))
            }
        } catch (err) {
            if (!isCurrentEmailRequest()) {
                return
            }

            console.error('Email check failed:', err)
            setEmailStatus({ checking: false, registered: false })
        }
    }, [t])

    // Debounced email check
    useEffect(() => {
        if (!formData.email || !touched.email || errors.email) {
            emailCheckRequestIdRef.current += 1
            return
        }

        const email = formData.email
        const requestId = ++emailCheckRequestIdRef.current
        const timer = setTimeout(() => {
            void checkEmailExists(email, requestId)
        }, 300) // 300ms debounce as per Phase 8.1

        return () => {
            clearTimeout(timer)

            if (emailCheckRequestIdRef.current === requestId) {
                emailCheckRequestIdRef.current += 1
            }
        }
    }, [formData.email, touched.email, errors.email, checkEmailExists])

    // ==========================================================================
    // Field handlers
    // ==========================================================================

    const handleChange = (e) => {
        const { name, value } = e.target
        let processedValue = value

        // Lowercase subdomain in real-time
        if (name === 'subdomain') {
            processedValue = value.toLowerCase().replace(/[^a-z0-9-]/g, '')
            latestSubdomainRef.current = processedValue
        }

        setFormData(prev => ({ ...prev, [name]: processedValue }))

        // Clear error on change
        if (errors[name]) {
            setErrors(prev => ({ ...prev, [name]: '' }))
        }

        // Reset subdomain status when typing
        if (name === 'subdomain') {
            setSubdomainStatus(prev => ({ ...prev, available: null, message: '' }))
        }

        // Reset email status when typing
        if (name === 'email') {
            currentEmailRef.current = processedValue
            emailCheckRequestIdRef.current += 1
            setEmailStatus({ checking: false, registered: false })
        }
    }

    const handleBlur = (e) => {
        const { name, value } = e.target
        setTouched(prev => ({ ...prev, [name]: true }))

        const error = validateField(name, value)
        setErrors(prev => ({ ...prev, [name]: error }))

        // Auto-generate subdomain from business name
        if (name === 'businessName' && value.trim() && !formData.subdomain) {
            const generatedSubdomain = generateSubdomain(value)
            if (generatedSubdomain) {
                latestSubdomainRef.current = generatedSubdomain
                setFormData(prev => ({ ...prev, subdomain: generatedSubdomain }))
                setTouched(prev => ({ ...prev, subdomain: true }))
            }
        }
    }

    const handleSuggestionClick = (suggestion) => {
        latestSubdomainRef.current = suggestion
        setFormData(prev => ({ ...prev, subdomain: suggestion }))
        setTouched(prev => ({ ...prev, subdomain: true }))
        void checkSubdomainAvailability(suggestion)
        subdomainRef.current?.focus()
    }

    // ==========================================================================
    // Form submission
    // ==========================================================================

    const isFormValid = () => {
        return (
            formData.businessName.trim().length >= 2 &&
            /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(formData.email) &&
            formData.password.length >= 8 &&
            subdomainStatus.available === true &&
            !emailStatus.registered
        )
    }

    const handleSubmit = async (e) => {
        e.preventDefault()

        // Validate all fields
        const newErrors = {}
        Object.keys(formData).forEach(key => {
            const error = validateField(key, formData[key])
            if (error) newErrors[key] = error
        })

        if (Object.keys(newErrors).length > 0) {
            setErrors(newErrors)
            setTouched({ businessName: true, email: true, password: true, subdomain: true })
            // Focus first error field
            const firstErrorField = ['businessName', 'email', 'password', 'subdomain'].find(f => newErrors[f])
            if (firstErrorField === 'businessName') businessNameRef.current?.focus()
            else if (firstErrorField === 'email') emailRef.current?.focus()
            else if (firstErrorField === 'password') passwordRef.current?.focus()
            else if (firstErrorField === 'subdomain') subdomainRef.current?.focus()
            return
        }

        if (!subdomainStatus.available) {
            setErrors(prev => ({ ...prev, subdomain: t('tenantRegistration.validation.availableStoreAddress') }))
            subdomainRef.current?.focus()
            return
        }

        setIsSubmitting(true)
        setSubmitError('')

        // Generate unique request ID for idempotency
        const requestId = `reg-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`

        try {
            const response = await api.post('/platform/register', {
                businessName: formData.businessName.trim(),
                email: formData.email.trim().toLowerCase(),
                password: formData.password,
                subdomain: formData.subdomain
            }, {
                headers: {
                    'X-Request-Id': requestId
                }
            })

            const { user } = response.data

            // Auto-login with tenantId for immediate multi-tenant API access
            login(
                {
                    id: user.id,
                    email: user.email,
                    firstName: user.firstName,
                    lastName: user.lastName,
                    role: user.role,
                    username: user.email,
                    tenantId: user.tenantId  // CRITICAL: Include tenantId for multi-tenant
                }
            )

            // Redirect to dashboard
            navigate('/', { replace: true })

        } catch (err) {
            if (import.meta.env.DEV) {
                console.error('Registration failed:', err)
            }

            const errorData = err.response?.data

            if (errorData?.fieldErrors) {
                setErrors(errorData.fieldErrors)
                // Focus first error field
                const firstErrorField = ['businessName', 'email', 'password', 'subdomain'].find(f => errorData.fieldErrors[f])
                if (firstErrorField === 'businessName') businessNameRef.current?.focus()
                else if (firstErrorField === 'email') emailRef.current?.focus()
                else if (firstErrorField === 'password') passwordRef.current?.focus()
                else if (firstErrorField === 'subdomain') subdomainRef.current?.focus()
            }

            if (errorData?.suggestions && errorData.suggestions.length > 0) {
                setSubdomainStatus(prev => ({
                    ...prev,
                    available: false,
                    suggestions: errorData.suggestions,
                    message: errorData.message || t('tenantRegistration.status.addressTaken')
                }))
            }

            setSubmitError(
                errorData?.message ||
                err.message ||
                t('tenantRegistration.messages.genericError')
            )
        } finally {
            setIsSubmitting(false)
        }
    }

    // ==========================================================================
    // Render
    // ==========================================================================

    return (
        <div
            className="min-h-screen flex items-center justify-center bg-gradient-to-br from-primary-500 to-primary-700 py-12 px-4"
            dir={isRTL ? 'rtl' : 'ltr'}
        >
            <div className="w-full max-w-lg">
                {/* Header */}
                <div className="text-center mb-8">
                    <h1 className="text-3xl font-bold text-white">{t('tenantRegistration.title')}</h1>
                    <p className="text-primary-100 mt-2">{t('tenantRegistration.subtitle')}</p>
                </div>

                {/* Registration Card */}
                <div className="bg-white rounded-xl shadow-2xl p-8">
                    {/* Sign in link */}
                    <div className={`mb-6 ${isRTL ? 'text-left' : 'text-right'}`}>
                        <span className="text-gray-600 text-sm">{t('tenantRegistration.signInPrompt')} </span>
                        <Link to="/login" className="text-primary-600 hover:text-primary-700 font-medium text-sm">
                            {t('tenantRegistration.signIn')}
                        </Link>
                    </div>

                    {/* Submit error alert */}
                    {submitError && (
                        <div
                            className="mb-6 p-4 bg-red-50 border border-red-200 rounded-lg flex items-start gap-3"
                            role="alert"
                        >
                            <svg className="w-5 h-5 text-red-500 flex-shrink-0 mt-0.5" fill="currentColor" viewBox="0 0 20 20">
                                <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clipRule="evenodd" />
                            </svg>
                            <div>
                                <p className="text-red-800 font-medium">{submitError}</p>
                            </div>
                        </div>
                    )}

                    <form onSubmit={handleSubmit} noValidate>
                        {/* Business Name */}
                        <div className="mb-5">
                            <label htmlFor="businessName" className="block text-sm font-medium text-gray-700 mb-1">
                                {t('tenantRegistration.fields.businessName')}
                            </label>
                            <input
                                ref={businessNameRef}
                                type="text"
                                id="businessName"
                                name="businessName"
                                value={formData.businessName}
                                onChange={handleChange}
                                onBlur={handleBlur}
                                disabled={isSubmitting}
                                className={`w-full px-4 py-3 border rounded-lg focus:outline-none focus:ring-2 transition-colors ${errors.businessName && touched.businessName
                                    ? 'border-red-300 focus:ring-red-200 focus:border-red-500'
                                    : 'border-gray-300 focus:ring-primary-200 focus:border-primary-500'
                                    } disabled:bg-gray-100 disabled:cursor-not-allowed`}
                                aria-describedby="businessName-helper businessName-error"
                                aria-invalid={errors.businessName && touched.businessName ? 'true' : 'false'}
                                autoComplete="organization"
                                maxLength={100}
                            />
                            <p id="businessName-helper" className="mt-1 text-sm text-gray-500">
                                {t('tenantRegistration.helpers.businessName')}
                            </p>
                            {errors.businessName && touched.businessName && (
                                <p id="businessName-error" className="mt-1 text-sm text-red-600" role="alert">
                                    {errors.businessName}
                                </p>
                            )}
                        </div>

                        {/* Email */}
                        <div className="mb-5">
                            <label htmlFor="email" className="block text-sm font-medium text-gray-700 mb-1">
                                {t('tenantRegistration.fields.email')}
                            </label>
                            <div className="relative">
                                <input
                                    ref={emailRef}
                                    type="email"
                                    id="email"
                                    name="email"
                                    value={formData.email}
                                    onChange={handleChange}
                                    onBlur={handleBlur}
                                    disabled={isSubmitting}
                                    className={`w-full px-4 py-3 border rounded-lg focus:outline-none focus:ring-2 transition-colors ${(errors.email && touched.email) || emailStatus.registered
                                        ? 'border-red-300 focus:ring-red-200 focus:border-red-500'
                                        : 'border-gray-300 focus:ring-primary-200 focus:border-primary-500'
                                        } disabled:bg-gray-100 disabled:cursor-not-allowed`}
                                    aria-describedby="email-helper email-error"
                                    aria-invalid={(errors.email && touched.email) || emailStatus.registered ? 'true' : 'false'}
                                    autoComplete="email"
                                    inputMode="email"
                                />
                                {emailStatus.checking && (
                                    <div className="absolute right-3 top-1/2 -translate-y-1/2">
                                        <svg className="animate-spin h-5 w-5 text-gray-400" fill="none" viewBox="0 0 24 24">
                                            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                                            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                                        </svg>
                                    </div>
                                )}
                            </div>
                            <p id="email-helper" className="mt-1 text-sm text-gray-500">
                                {t('tenantRegistration.helpers.email')}
                            </p>
                            {errors.email && touched.email && (
                                <p id="email-error" className="mt-1 text-sm text-red-600" role="alert">
                                    {errors.email}
                                    {emailStatus.registered && (
                                        <Link to="/login" className="ml-1 underline hover:text-red-700">
                                            {t('tenantRegistration.signIn')}
                                        </Link>
                                    )}
                                </p>
                            )}
                        </div>

                        {/* Password */}
                        <div className="mb-5">
                            <label htmlFor="password" className="block text-sm font-medium text-gray-700 mb-1">
                                {t('tenantRegistration.fields.password')}
                            </label>
                            <div className="relative">
                                <input
                                    ref={passwordRef}
                                    type={showPassword ? 'text' : 'password'}
                                    id="password"
                                    name="password"
                                    value={formData.password}
                                    onChange={handleChange}
                                    onBlur={handleBlur}
                                    disabled={isSubmitting}
                                    className={`w-full px-4 py-3 pr-16 border rounded-lg focus:outline-none focus:ring-2 transition-colors ${errors.password && touched.password
                                        ? 'border-red-300 focus:ring-red-200 focus:border-red-500'
                                        : 'border-gray-300 focus:ring-primary-200 focus:border-primary-500'
                                        } disabled:bg-gray-100 disabled:cursor-not-allowed`}
                                    aria-describedby="password-helper password-error"
                                    aria-invalid={errors.password && touched.password ? 'true' : 'false'}
                                    autoComplete="new-password"
                                />
                                <button
                                    type="button"
                                    onClick={() => setShowPassword(!showPassword)}
                                    className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-500 hover:text-gray-700 text-sm font-medium focus:outline-none focus:text-primary-600"
                                    aria-label={showPassword
                                        ? t('tenantRegistration.actions.hidePassword')
                                        : t('tenantRegistration.actions.showPassword')}
                                >
                                    {showPassword
                                        ? t('tenantRegistration.actions.hidePassword')
                                        : t('tenantRegistration.actions.showPassword')}
                                </button>
                            </div>
                            <p id="password-helper" className="mt-1 text-sm text-gray-500">
                                {t('tenantRegistration.helpers.password')}
                            </p>
                            {errors.password && touched.password && (
                                <p id="password-error" className="mt-1 text-sm text-red-600" role="alert">
                                    {errors.password}
                                </p>
                            )}
                        </div>

                        {/* Store Address (Subdomain) */}
                        <div className="mb-6">
                            <label htmlFor="subdomain" className="block text-sm font-medium text-gray-700 mb-1">
                                {t('tenantRegistration.fields.storeAddress')}
                            </label>
                            <div className="flex">
                                <input
                                    ref={subdomainRef}
                                    type="text"
                                    id="subdomain"
                                    name="subdomain"
                                    value={formData.subdomain}
                                    onChange={handleChange}
                                    onBlur={handleBlur}
                                    disabled={isSubmitting}
                                    className={`flex-1 px-4 py-3 border rounded-l-lg focus:outline-none focus:ring-2 focus:z-10 transition-colors ${(errors.subdomain && touched.subdomain) || subdomainStatus.available === false
                                        ? 'border-red-300 focus:ring-red-200 focus:border-red-500'
                                        : subdomainStatus.available === true
                                            ? 'border-green-300 focus:ring-green-200 focus:border-green-500'
                                            : 'border-gray-300 focus:ring-primary-200 focus:border-primary-500'
                                        } disabled:bg-gray-100 disabled:cursor-not-allowed`}
                                    aria-describedby="subdomain-status subdomain-error"
                                    aria-invalid={(errors.subdomain && touched.subdomain) || subdomainStatus.available === false ? 'true' : 'false'}
                                    autoComplete="off"
                                    inputMode="url"
                                    maxLength={63}
                                />
                                <span className="inline-flex items-center px-4 py-3 bg-gray-100 border border-l-0 border-gray-300 rounded-r-lg text-gray-500 text-sm">
                                    .{platformDomain}
                                </span>
                            </div>

                            {/* Subdomain status */}
                            <div id="subdomain-status" className="mt-2" aria-live="polite">
                                {subdomainStatus.checking && (
                                    <p className="text-sm text-gray-500 flex items-center gap-2">
                                        <svg className="animate-spin h-4 w-4" fill="none" viewBox="0 0 24 24">
                                            <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                                            <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                                        </svg>
                                        {t('tenantRegistration.status.checkingAvailability')}
                                    </p>
                                )}

                                {!subdomainStatus.checking && subdomainStatus.available === true && (
                                    <div>
                                        <p className="text-sm text-green-600 flex items-center gap-1">
                                            <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
                                                <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" />
                                            </svg>
                                            {subdomainStatus.message}
                                        </p>
                                        <p className="text-sm text-gray-500 mt-1">
                                            {t('tenantRegistration.status.yourStore')}:{' '}
                                            <span className="font-medium">{formData.subdomain}.{platformDomain}</span>
                                        </p>
                                    </div>
                                )}

                                {!subdomainStatus.checking && subdomainStatus.available === false && (
                                    <div>
                                        <p className="text-sm text-red-600 flex items-center gap-1">
                                            <svg className="w-4 h-4" fill="currentColor" viewBox="0 0 20 20">
                                                <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clipRule="evenodd" />
                                            </svg>
                                            {subdomainStatus.message}
                                        </p>

                                        {/* Suggestions */}
                                        {subdomainStatus.suggestions.length > 0 && (
                                            <div className="mt-2">
                                                <p className="text-sm text-gray-600 mb-2">
                                                    {t('tenantRegistration.status.trySuggestions')}
                                                </p>
                                                <div className="flex flex-wrap gap-2">
                                                    {subdomainStatus.suggestions.map((suggestion) => (
                                                        <button
                                                            key={suggestion}
                                                            type="button"
                                                            onClick={() => handleSuggestionClick(suggestion)}
                                                            className="px-3 py-1.5 bg-gray-100 hover:bg-gray-200 text-gray-700 text-sm rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-primary-200"
                                                        >
                                                            {suggestion}
                                                        </button>
                                                    ))}
                                                </div>
                                            </div>
                                        )}
                                    </div>
                                )}
                            </div>

                            {errors.subdomain && touched.subdomain && !subdomainStatus.message && (
                                <p id="subdomain-error" className="mt-1 text-sm text-red-600" role="alert">
                                    {errors.subdomain}
                                </p>
                            )}
                        </div>

                        {/* Submit Button */}
                        <button
                            type="submit"
                            disabled={!isFormValid() || isSubmitting}
                            className={`w-full py-3 px-4 rounded-lg font-medium text-white transition-all focus:outline-none focus:ring-2 focus:ring-offset-2 ${isFormValid() && !isSubmitting
                                ? 'bg-primary-600 hover:bg-primary-700 focus:ring-primary-500'
                                : 'bg-gray-400 cursor-not-allowed'
                                }`}
                            aria-busy={isSubmitting}
                        >
                            {isSubmitting ? (
                                <span className="flex items-center justify-center gap-2">
                                    <svg className="animate-spin h-5 w-5" fill="none" viewBox="0 0 24 24">
                                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4z" />
                                    </svg>
                                    {t('tenantRegistration.actions.creatingStore')}
                                </span>
                            ) : (
                                t('tenantRegistration.actions.createStore')
                            )}
                        </button>

                        {/* Legal text */}
                        <p className="mt-4 text-center text-xs text-gray-500">
                            {t('tenantRegistration.legal.prefix')}{' '}
                            <a href="/terms" target="_blank" className="underline hover:text-gray-700">
                                {t('tenantRegistration.legal.termsOfService')}
                            </a>
                            {' '}
                            {t('tenantRegistration.legal.and')}{' '}
                            <a href="/privacy" target="_blank" className="underline hover:text-gray-700">
                                {t('tenantRegistration.legal.privacyPolicy')}
                            </a>.
                        </p>
                    </form>
                </div>

                {/* Value propositions - visible on larger screens */}
                <div className="mt-8 grid grid-cols-2 gap-4 text-white text-sm">
                    <div className="flex items-center gap-2">
                        <svg className="w-5 h-5 text-green-300" fill="currentColor" viewBox="0 0 20 20">
                            <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" />
                        </svg>
                        {t('tenantRegistration.benefits.freeToStart')}
                    </div>
                    <div className="flex items-center gap-2">
                        <svg className="w-5 h-5 text-green-300" fill="currentColor" viewBox="0 0 20 20">
                            <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" />
                        </svg>
                        {t('tenantRegistration.benefits.noCardRequired')}
                    </div>
                    <div className="flex items-center gap-2">
                        <svg className="w-5 h-5 text-green-300" fill="currentColor" viewBox="0 0 20 20">
                            <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" />
                        </svg>
                        {t('tenantRegistration.benefits.setupInSeconds')}
                    </div>
                    <div className="flex items-center gap-2">
                        <svg className="w-5 h-5 text-green-300" fill="currentColor" viewBox="0 0 20 20">
                            <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" />
                        </svg>
                        {t('tenantRegistration.benefits.storePrivate')}
                    </div>
                </div>
            </div>
        </div>
    )
}
