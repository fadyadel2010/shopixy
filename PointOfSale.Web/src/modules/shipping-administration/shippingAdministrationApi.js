import api, { getSafeApiErrorMessage } from '../../services/api'

const ADMIN_SHIPPING_BASE = '/admin/shipping'
const ADMIN_SHIPPING_READ_HEADERS = {
    'Cache-Control': 'no-store',
    Pragma: 'no-cache',
}

const stringOrNull = (value) => (typeof value === 'string' && value.trim() ? value.trim() : null)
const requiredString = (value, field) => {
    const text = stringOrNull(value)
    if (!text) {
        throw new Error(`Shipping administration DTO rejected missing ${field}.`)
    }
    return text
}

const requiredArray = (value, field) => {
    if (!Array.isArray(value)) {
        throw new Error(`Shipping administration DTO rejected malformed ${field}.`)
    }
    return value
}

export function admitSupportedCarriers(payload) {
    const seen = new Set()
    return requiredArray(payload, 'supported carriers').map((carrier) => {
        if (!carrier || typeof carrier !== 'object') {
            throw new Error('Shipping administration DTO rejected malformed supported carrier.')
        }

        const carrierCode = requiredString(carrier.carrierCode, 'carrier code')
        if (seen.has(carrierCode)) {
            throw new Error('Shipping administration DTO rejected duplicate carrier code.')
        }
        seen.add(carrierCode)

        return {
            carrierCode,
            displayName: requiredString(carrier.displayName, 'carrier display name'),
            serviceLevels: requiredArray(carrier.serviceLevels, 'service levels').map((level) => ({
                code: requiredString(level?.code, 'service level code'),
                name: requiredString(level?.name, 'service level name'),
                description: stringOrNull(level?.description),
                typicalDaysMin: Number.isInteger(level?.typicalDaysMin) ? level.typicalDaysMin : 0,
                typicalDaysMax: Number.isInteger(level?.typicalDaysMax) ? level.typicalDaysMax : 0,
                isEnabled: typeof level?.isEnabled === 'boolean' ? level.isEnabled : null,
            })),
        }
    })
}

export function admitCarrierAccounts(payload) {
    const seen = new Set()
    return requiredArray(payload, 'carrier accounts').map((account) => {
        if (!account || typeof account !== 'object') {
            throw new Error('Shipping administration DTO rejected malformed carrier account.')
        }

        const id = requiredString(account.id, 'carrier account identity')
        if (seen.has(id)) {
            throw new Error('Shipping administration DTO rejected duplicate carrier account.')
        }
        seen.add(id)

        return {
            id,
            carrierCode: requiredString(account.carrierCode, 'carrier code'),
            environment: stringOrNull(account.environment),
            activeCredentialReferenceId: stringOrNull(account.activeCredentialReferenceId),
            state: requiredString(account.state, 'activation state'),
            operationalState: requiredString(account.operationalState, 'operational state'),
            healthState: requiredString(account.healthState, 'health state'),
            healthReason: stringOrNull(account.healthReason),
            lastHealthEvaluatedAtUtc: stringOrNull(account.lastHealthEvaluatedAtUtc),
            activatedAtUtc: stringOrNull(account.activatedAtUtc),
            suspendedAtUtc: stringOrNull(account.suspendedAtUtc),
            disabledAtUtc: stringOrNull(account.disabledAtUtc),
            retiredAtUtc: stringOrNull(account.retiredAtUtc),
            credentialReferences: requiredArray(account.credentialReferences, 'credential references').map((credential) => ({
                id: requiredString(credential?.id, 'credential reference identity'),
                maskedFingerprint: stringOrNull(credential?.maskedFingerprint),
                state: requiredString(credential?.state, 'credential state'),
                expiresAtUtc: stringOrNull(credential?.expiresAtUtc),
            })),
            warehouseAssignments: requiredArray(account.warehouseAssignments, 'warehouse assignments').map((assignment) => ({
                warehouseId: requiredString(assignment?.warehouseId, 'warehouse identity'),
                isEnabled: assignment?.isEnabled === true,
                originReadinessPublished: assignment?.originReadinessPublished === true,
                readinessEvidenceReference: stringOrNull(assignment?.readinessEvidenceReference),
            })),
            policies: requiredArray(account.policies, 'policies').map((policy) => ({
                scope: requiredString(policy?.scope, 'policy scope'),
                mode: requiredString(policy?.mode, 'policy mode'),
                customsReadinessPublished: policy?.customsReadinessPublished === true,
                readinessEvidenceReference: stringOrNull(policy?.readinessEvidenceReference),
            })),
            serviceLevels: requiredArray(account.serviceLevels, 'service levels').map((level) => ({
                code: requiredString(level?.code, 'service level code'),
                name: requiredString(level?.name, 'service level name'),
                description: stringOrNull(level?.description),
                typicalDaysMin: Number.isInteger(level?.typicalDaysMin) ? level.typicalDaysMin : 0,
                typicalDaysMax: Number.isInteger(level?.typicalDaysMax) ? level.typicalDaysMax : 0,
                isEnabled: level?.isEnabled === true,
            })),
            lastConnectionTest: account.lastConnectionTest ? {
                id: requiredString(account.lastConnectionTest.id, 'connection test identity'),
                environment: requiredString(account.lastConnectionTest.environment, 'connection test environment'),
                status: requiredString(account.lastConnectionTest.status, 'connection test status'),
                failureCode: stringOrNull(account.lastConnectionTest.failureCode),
                failureReason: stringOrNull(account.lastConnectionTest.failureReason),
                testedAtUtc: requiredString(account.lastConnectionTest.testedAtUtc, 'connection test timestamp'),
            } : null,
        }
    })
}

export function admitAdministrationAudit(payload) {
    const seen = new Set()
    return requiredArray(payload, 'audit').map((entry) => {
        if (!entry || typeof entry !== 'object') {
            throw new Error('Shipping administration DTO rejected malformed audit entry.')
        }

        const id = requiredString(entry.id, 'audit identity')
        if (seen.has(id)) {
            throw new Error('Shipping administration DTO rejected duplicate audit entry.')
        }
        seen.add(id)

        return {
            id,
            carrierAccountId: requiredString(entry.carrierAccountId, 'carrier account identity'),
            action: requiredString(entry.action, 'audit action'),
            actorId: requiredString(entry.actorId, 'audit actor'),
            correlationId: stringOrNull(entry.correlationId),
            previousState: stringOrNull(entry.previousState),
            nextState: stringOrNull(entry.nextState),
            reason: stringOrNull(entry.reason),
            occurredAtUtc: requiredString(entry.occurredAtUtc, 'audit timestamp'),
        }
    })
}

export async function fetchSupportedCarriers() {
    try {
        const response = await api.get(`${ADMIN_SHIPPING_BASE}/supported-carriers`, {
            headers: ADMIN_SHIPPING_READ_HEADERS,
        })
        return admitSupportedCarriers(response.data)
    } catch (error) {
        throw new Error(getSafeApiErrorMessage(error, 'Failed to load supported carriers.'))
    }
}

export async function fetchCarrierAccounts() {
    try {
        const response = await api.get(`${ADMIN_SHIPPING_BASE}/carrier-accounts`, {
            headers: ADMIN_SHIPPING_READ_HEADERS,
        })
        return admitCarrierAccounts(response.data)
    } catch (error) {
        throw new Error(getSafeApiErrorMessage(error, 'Failed to load carrier accounts.'))
    }
}

export async function fetchShippingAdministrationAudit(limit = 100) {
    try {
        const response = await api.get(`${ADMIN_SHIPPING_BASE}/audit`, {
            params: { limit },
            headers: ADMIN_SHIPPING_READ_HEADERS,
        })
        return admitAdministrationAudit(response.data)
    } catch (error) {
        throw new Error(getSafeApiErrorMessage(error, 'Failed to load shipping administration audit.'))
    }
}

async function mutateShippingAdministration(path, method, payload) {
    try {
        const response = await api[method](`${ADMIN_SHIPPING_BASE}${path}`, payload)
        return response.data
    } catch (error) {
        throw new Error(getSafeApiErrorMessage(error, 'Shipping administration action failed.'))
    }
}

export function createCarrierAccount(carrierCode) {
    return mutateShippingAdministration('/carrier-accounts', 'post', { carrierCode })
}

export function updateCredentialReference(carrierAccountId, command) {
    return mutateShippingAdministration(`/carrier-accounts/${carrierAccountId}/credential-reference`, 'put', command)
}

export function selectCarrierEnvironment(carrierAccountId, environment) {
    return mutateShippingAdministration(`/carrier-accounts/${carrierAccountId}/environment`, 'put', { environment })
}

export function assignCarrierWarehouse(carrierAccountId, warehouseId, command) {
    return mutateShippingAdministration(`/carrier-accounts/${carrierAccountId}/warehouse-assignments/${warehouseId}`, 'put', {
        warehouseId,
        ...command,
    })
}

export function setCarrierPolicy(carrierAccountId, scope, command) {
    return mutateShippingAdministration(`/carrier-accounts/${carrierAccountId}/policies/${scope.toLowerCase()}`, 'put', command)
}

export function setCarrierServiceLevel(carrierAccountId, serviceLevelCode, enabled) {
    return mutateShippingAdministration(`/carrier-accounts/${carrierAccountId}/service-levels/${serviceLevelCode}`, 'put', { enabled })
}

export function runCarrierConnectionTest(carrierAccountId, command) {
    return mutateShippingAdministration(`/carrier-accounts/${carrierAccountId}/connection-tests`, 'post', command)
}

export function activateCarrierAccount(carrierAccountId) {
    return mutateShippingAdministration(`/carrier-accounts/${carrierAccountId}/activate`, 'post', {})
}

export function evaluateCarrierHealth(carrierAccountId) {
    return mutateShippingAdministration(`/carrier-accounts/${carrierAccountId}/health/evaluate`, 'post', {})
}

export function suspendCarrierAccount(carrierAccountId) {
    return mutateShippingAdministration(`/carrier-accounts/${carrierAccountId}/suspend`, 'post')
}

export function disableCarrierAccount(carrierAccountId) {
    return mutateShippingAdministration(`/carrier-accounts/${carrierAccountId}/disable`, 'post')
}

export function retireCarrierAccount(carrierAccountId) {
    return mutateShippingAdministration(`/carrier-accounts/${carrierAccountId}/retire`, 'post')
}

export function createShippingAdministrationSummary(accounts) {
    const configuredCarriers = accounts.length
    const activeCarriers = accounts.filter((account) => account.state === 'Active').length
    const blockedCarriers = accounts.filter((account) => account.healthState === 'Blocked').length
    const connectionWarnings = accounts.filter((account) => account.lastConnectionTest?.status === 'Failed').length
    const activationBlockers = accounts.filter((account) => account.healthState === 'Blocked' || account.state === 'CredentialsPending').length

    return {
        configuredCarriers,
        activeCarriers,
        blockedCarriers,
        connectionWarnings,
        activationBlockers,
        recommendedNextAction: configuredCarriers === 0
            ? 'Create a carrier account from a backend-supported carrier.'
            : activationBlockers > 0
                ? 'Review backend-reported activation blockers.'
                : 'Monitor health and audit for changes.',
    }
}

export function getCarrierDisplayName(supportedCarriers, carrierCode) {
    return supportedCarriers.find((carrier) => carrier.carrierCode === carrierCode)?.displayName || carrierCode
}
