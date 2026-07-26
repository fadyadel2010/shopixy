export interface PaymentProviderCapability {
    code: string
    displayName: string
    description: string
    supported: boolean
    requiresConfiguration: boolean
}

export interface PaymentProviderSummary {
    providerCode: string
    displayName: string
    description: string
    enabled: boolean
    environment: string
    status: string
    health: PaymentProviderHealth
    capabilities: PaymentProviderCapability[]
    metadata: Record<string, unknown>
}

export interface PaymentProviderConfiguration extends PaymentProviderSummary {
    configuration: Record<string, unknown>
}

export interface PaymentProviderConfigurationRequest {
    providerCode?: string
    enabled?: boolean
    environment?: string
    configuration?: Record<string, unknown>
    metadata?: Record<string, unknown>
    secretReplacements?: Record<string, string>
}

export interface PaymentProviderActionResult {
    success: boolean
    status: 'Healthy' | 'Warning' | 'Error' | 'Unknown' | string
    provider: string
    environment: string
    message: string
    checkedAtUtc: string
    latencyMs: number
    errorCategory: string
    warnings: string[]
    errors: string[]
    providerVersion?: string | null
    capabilities: PaymentProviderCapability[]
}

export interface PaymentProviderHealth {
    status: 'Healthy' | 'Warning' | 'Error' | 'Unknown' | string
    environment: string
    lastSuccessfulCheckUtc?: string | null
    lastFailedCheckUtc?: string | null
    lastCheckDurationMs?: number | null
    providerVersion?: string | null
    connectionQuality: string
    operationalMessages: string[]
    latestResult?: PaymentProviderActionResult | null
    previousResult?: PaymentProviderActionResult | null
}

export interface PaymentProviderListResponse {
    providers: PaymentProviderSummary[]
    permissions: string[]
}

export interface PaymentProviderDashboard {
    providers: PaymentProviderDashboardProvider[]
    summary: PaymentProviderDashboardSummary
    capabilities: PaymentProviderCapability[]
    health: PaymentProviderDashboardHealth[]
    secrets: PaymentProviderDashboardSecretSummary[]
    permissions: string[]
    recentActivity: PaymentProviderAdministrationActivity[]
    warnings: string[]
}

export interface PaymentProviderDashboardProvider extends PaymentProviderConfiguration {
    lastCheckUtc?: string | null
    secrets: PaymentProviderSecretMetadata[]
    permissions: string[]
    version?: string | null
}

export interface PaymentProviderDashboardSummary {
    providerCount: number
    enabledCount: number
    disabledCount: number
    healthyCount: number
    warningCount: number
    errorCount: number
    unknownHealthCount: number
    configuredSecretCount: number
    capabilityCount: number
    generatedAtUtc: string
}

export interface PaymentProviderDashboardHealth {
    providerCode: string
    status: string
    environment: string
    lastCheckUtc?: string | null
    lastCheckDurationMs?: number | null
    connectionQuality: string
}

export interface PaymentProviderDashboardSecretSummary {
    providerCode: string
    secretCount: number
    activeSecretCount: number
    lastRotatedAtUtc?: string | null
    secrets: PaymentProviderSecretMetadata[]
}

export interface PaymentProviderAdministrationActivity {
    activityId: string
    providerCode: string
    action: string
    displayName: string
    description: string
    occurredAtUtc: string
    actor?: string | null
    success: boolean
    reference?: string | null
}

export interface ProviderDashboardFiltersState {
    provider: string
    environment: string
    status: string
    health: string
    enabled: 'all' | 'enabled' | 'disabled'
    capability: string
    search: string
    sort: 'provider' | 'status' | 'environment' | 'health' | 'lastCheck'
}

export interface PaymentProviderConfigurationResponse {
    provider: PaymentProviderConfiguration
    permissions: string[]
    validationErrors: string[]
}

export interface PaymentProviderActionResponse {
    result: PaymentProviderActionResult
    health: PaymentProviderHealth
    permissions: string[]
}

export interface PaymentProviderSecretMetadata {
    secretId: string
    providerCode: string
    secretType: string
    version: number
    createdAtUtc: string
    createdBy?: string | null
    activatedAtUtc?: string | null
    retiredAtUtc?: string | null
    status: string
    fingerprint: string
    lastRotatedAtUtc?: string | null
    lastUsedAtUtc?: string | null
}

export interface PaymentProviderSecretLifecycleResponse {
    providerCode: string
    secrets: PaymentProviderSecretMetadata[]
    permissions: string[]
    validationErrors: string[]
}

export interface RotatePaymentProviderSecretRequest {
    secretType: string
    newSecret: string
    confirmNewSecret: string
    reason?: string
    administratorConfirmation: boolean
}

export interface RotatePaymentProviderSecretResponse {
    providerCode: string
    secret: PaymentProviderSecretMetadata
    versions: PaymentProviderSecretMetadata[]
    permissions: string[]
    validationErrors: string[]
}

export interface PaymentProviderAdministrationState {
    loading: boolean
    saving: boolean
    runningTest: boolean
    refreshing: boolean
    latestResult: PaymentProviderActionResult | null
    historicalResult: PaymentProviderActionResult | null
    dirty: boolean
    successMessage: string | null
    failureMessage: string | null
    selectedProviderCode: string | null
    providerList: PaymentProviderSummary[]
    configuration: PaymentProviderConfiguration | null
    originalConfiguration: PaymentProviderConfiguration | null
    validationErrors: string[]
}
