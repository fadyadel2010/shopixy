import { superAdminApiClient } from '../core/superAdminApiClient'
import type { SuperAdminApiSuccessResponse } from '../auth/superAdminAuth.types'
import type {
    PaymentProviderConfiguration,
    PaymentProviderConfigurationRequest,
    PaymentProviderConfigurationResponse,
    PaymentProviderActionResponse,
    PaymentProviderDashboard,
    PaymentProviderListResponse,
    PaymentProviderSessionReconciliationRequest,
    PaymentProviderSessionReconciliationResponse,
    PaymentProviderSecretLifecycleResponse,
    PaymentProviderSecretMetadata,
    PaymentProviderSummary,
    DevelopmentOnlinePaymentSuccessRequest,
    DevelopmentOnlinePaymentSuccessResponse,
    RotatePaymentProviderSecretRequest,
    RotatePaymentProviderSecretResponse,
} from './paymentProviderTypes'

const SUPER_ADMIN_PAYMENT_PROVIDERS_ENDPOINT = '/payment-providers'

function requireProviderSummary(provider: PaymentProviderSummary): PaymentProviderSummary {
    if (
        !provider ||
        typeof provider.providerCode !== 'string' ||
        !provider.providerCode.trim() ||
        typeof provider.displayName !== 'string' ||
        !provider.displayName.trim() ||
        typeof provider.enabled !== 'boolean' ||
        !Array.isArray(provider.capabilities)
    ) {
        throw new Error('Payment provider response failed admission.')
    }

    return provider
}

function requireProviderConfiguration(provider: PaymentProviderConfiguration): PaymentProviderConfiguration {
    requireProviderSummary(provider)

    if (!provider.configuration || typeof provider.configuration !== 'object') {
        throw new Error('Payment provider configuration response failed admission.')
    }

    return provider
}

function requireDashboard(response: PaymentProviderDashboard): PaymentProviderDashboard {
    if (
        !response ||
        !Array.isArray(response.providers) ||
        !response.summary ||
        typeof response.summary.providerCount !== 'number' ||
        !Array.isArray(response.capabilities) ||
        !Array.isArray(response.health) ||
        !Array.isArray(response.secrets) ||
        !Array.isArray(response.recentActivity)
    ) {
        throw new Error('Payment provider dashboard response failed admission.')
    }

    response.providers.forEach(requireProviderConfiguration)
    return response
}

function requireConnectionResult(response: PaymentProviderActionResponse): PaymentProviderActionResponse {
    if (
        !response ||
        !response.result ||
        typeof response.result.success !== 'boolean' ||
        typeof response.result.provider !== 'string' ||
        typeof response.result.status !== 'string' ||
        typeof response.result.message !== 'string' ||
        !response.health ||
        typeof response.health.status !== 'string'
    ) {
        throw new Error('Payment provider action response failed admission.')
    }

    return response
}

function requireSecretMetadata(secret: PaymentProviderSecretMetadata): PaymentProviderSecretMetadata {
    if (
        !secret ||
        typeof secret.secretId !== 'string' ||
        !secret.secretId.trim() ||
        typeof secret.providerCode !== 'string' ||
        !secret.providerCode.trim() ||
        typeof secret.secretType !== 'string' ||
        !secret.secretType.trim() ||
        typeof secret.version !== 'number' ||
        typeof secret.status !== 'string' ||
        typeof secret.fingerprint !== 'string'
    ) {
        throw new Error('Payment provider secret metadata failed admission.')
    }

    return secret
}

function requireSessionReconciliation(
    response: PaymentProviderSessionReconciliationResponse,
): PaymentProviderSessionReconciliationResponse {
    if (
        !response ||
        typeof response.providerCode !== 'string' ||
        typeof response.paymentId !== 'string' ||
        typeof response.orderId !== 'string' ||
        typeof response.providerReference !== 'string' ||
        typeof response.status !== 'string' ||
        typeof response.retryAllowed !== 'boolean' ||
        typeof response.message !== 'string'
    ) {
        throw new Error('Provider-session reconciliation response failed admission.')
    }

    return response
}

export async function getPaymentProviders(): Promise<PaymentProviderListResponse> {
    const response = await superAdminApiClient.getRaw<SuperAdminApiSuccessResponse<PaymentProviderListResponse>>(
        SUPER_ADMIN_PAYMENT_PROVIDERS_ENDPOINT,
    )
    const providers = response.data.providers ?? []
    const providerCodes = new Set<string>()

    providers.forEach((provider) => {
        const admittedProvider = requireProviderSummary(provider)
        const normalizedCode = admittedProvider.providerCode.trim().toUpperCase()

        if (providerCodes.has(normalizedCode)) {
            throw new Error('Payment provider response contained duplicate providers.')
        }

        providerCodes.add(normalizedCode)
    })

    return {
        providers,
        permissions: response.data.permissions ?? [],
    }
}

export async function getPaymentProviderDashboard(): Promise<PaymentProviderDashboard> {
    const response = await superAdminApiClient.getRaw<SuperAdminApiSuccessResponse<PaymentProviderDashboard>>(
        `${SUPER_ADMIN_PAYMENT_PROVIDERS_ENDPOINT}/dashboard`,
    )

    return requireDashboard({
        providers: response.data.providers ?? [],
        summary: response.data.summary,
        capabilities: response.data.capabilities ?? [],
        health: response.data.health ?? [],
        secrets: response.data.secrets ?? [],
        permissions: response.data.permissions ?? [],
        recentActivity: response.data.recentActivity ?? [],
        warnings: response.data.warnings ?? [],
    })
}

export async function getPaymentProviderConfiguration(
    providerCode: string,
): Promise<PaymentProviderConfigurationResponse> {
    const response = await superAdminApiClient.getRaw<SuperAdminApiSuccessResponse<PaymentProviderConfigurationResponse>>(
        `${SUPER_ADMIN_PAYMENT_PROVIDERS_ENDPOINT}/${encodeURIComponent(providerCode)}`,
    )

    return {
        provider: requireProviderConfiguration(response.data.provider),
        permissions: response.data.permissions ?? [],
        validationErrors: response.data.validationErrors ?? [],
    }
}

export async function savePaymentProviderConfiguration(
    providerCode: string,
    payload: PaymentProviderConfigurationRequest,
): Promise<PaymentProviderConfigurationResponse> {
    const response = await superAdminApiClient.putRaw<SuperAdminApiSuccessResponse<PaymentProviderConfigurationResponse>, PaymentProviderConfigurationRequest>(
        `${SUPER_ADMIN_PAYMENT_PROVIDERS_ENDPOINT}/${encodeURIComponent(providerCode)}`,
        payload,
    )

    return {
        provider: requireProviderConfiguration(response.data.provider),
        permissions: response.data.permissions ?? [],
        validationErrors: response.data.validationErrors ?? [],
    }
}

export async function testPaymentProviderConnection(providerCode: string): Promise<PaymentProviderActionResponse> {
    const response = await superAdminApiClient.postRaw<SuperAdminApiSuccessResponse<PaymentProviderActionResponse>, undefined>(
        `${SUPER_ADMIN_PAYMENT_PROVIDERS_ENDPOINT}/${encodeURIComponent(providerCode)}/actions/test-connection`,
        undefined,
    )

    return requireConnectionResult({
        result: response.data.result,
        health: response.data.health,
        permissions: response.data.permissions ?? [],
    })
}

export async function getPaymentProviderSecrets(
    providerCode: string,
): Promise<PaymentProviderSecretLifecycleResponse> {
    const response = await superAdminApiClient.getRaw<SuperAdminApiSuccessResponse<PaymentProviderSecretLifecycleResponse>>(
        `${SUPER_ADMIN_PAYMENT_PROVIDERS_ENDPOINT}/${encodeURIComponent(providerCode)}/secrets`,
    )

    const secrets = response.data.secrets ?? []
    secrets.forEach(requireSecretMetadata)

    return {
        providerCode: response.data.providerCode,
        secrets,
        permissions: response.data.permissions ?? [],
        validationErrors: response.data.validationErrors ?? [],
    }
}

export async function rotatePaymentProviderSecret(
    providerCode: string,
    payload: RotatePaymentProviderSecretRequest,
): Promise<RotatePaymentProviderSecretResponse> {
    const response = await superAdminApiClient.postRaw<SuperAdminApiSuccessResponse<RotatePaymentProviderSecretResponse>, RotatePaymentProviderSecretRequest>(
        `${SUPER_ADMIN_PAYMENT_PROVIDERS_ENDPOINT}/${encodeURIComponent(providerCode)}/secrets/rotate`,
        payload,
    )

    const versions = response.data.versions ?? []
    versions.forEach(requireSecretMetadata)

    return {
        providerCode: response.data.providerCode,
        secret: requireSecretMetadata(response.data.secret),
        versions,
        permissions: response.data.permissions ?? [],
        validationErrors: response.data.validationErrors ?? [],
    }
}

export async function reconcilePaymentProviderSessionNotCreated(
    providerCode: string,
    payload: PaymentProviderSessionReconciliationRequest,
): Promise<PaymentProviderSessionReconciliationResponse> {
    const response = await superAdminApiClient.postRaw<
        SuperAdminApiSuccessResponse<PaymentProviderSessionReconciliationResponse>,
        PaymentProviderSessionReconciliationRequest
    >(
        `${SUPER_ADMIN_PAYMENT_PROVIDERS_ENDPOINT}/${encodeURIComponent(providerCode)}/actions/reconcile-session-not-created`,
        payload,
    )

    return requireSessionReconciliation({
        ...response.data,
        permissions: response.data.permissions ?? [],
    })
}

export async function simulateDevelopmentOnlinePaymentSuccess(
    paymentId: string,
    payload: DevelopmentOnlinePaymentSuccessRequest,
): Promise<DevelopmentOnlinePaymentSuccessResponse> {
    if (!import.meta.env.DEV) {
        throw new Error('Online payment success simulation is available only in Development builds.')
    }

    const response = await superAdminApiClient.postRaw<
        DevelopmentOnlinePaymentSuccessResponse,
        DevelopmentOnlinePaymentSuccessRequest
    >(`/api/dev/commercehub/payments/${encodeURIComponent(paymentId)}/simulate-success`, payload)

    const result = response
    if (
        !result ||
        result.evidenceType !== 'DevelopmentOnlinePaymentSuccessSimulation' ||
        result.isDevelopmentSimulation !== true ||
        result.externalMoneyMovementConfirmed !== false ||
        typeof result.processed !== 'boolean' ||
        typeof result.message !== 'string'
    ) {
        throw new Error('Development payment simulation response failed evidence admission.')
    }

    return result
}
