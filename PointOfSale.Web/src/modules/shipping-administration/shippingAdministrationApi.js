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

const operationNames = new Set(['quote', 'create', 'tracking', 'label', 'cancellation', 'recovery'])
const operationStates = new Set(['Implemented', 'LocalOnly', 'StoredOnly', 'Unavailable', 'ReconciliationOnly'])
export function admitCarrierOperations(payload) {
    if (payload == null) return [] // Older runtimes convey no positive operation authority.
    const seen = new Set()
    return requiredArray(payload, 'carrier operations').map((operation) => {
        if (!operation || !operationNames.has(operation.operation) || seen.has(operation.operation) || !operationStates.has(operation.implementation)) {
            throw new Error('Shipping administration DTO rejected malformed carrier operation.')
        }
        seen.add(operation.operation)
        return {
            operation: operation.operation, implementation: operation.implementation,
            accountReady: operation.implementation === 'Implemented' && operation.accountReady === true,
            requiresShipmentEvidence: operation.requiresShipmentEvidence !== false,
            blockers: Array.isArray(operation.blockers) ? operation.blockers.filter(item => typeof item === 'string') : [],
            limitations: Array.isArray(operation.limitations) ? operation.limitations.filter(item => typeof item === 'string') : [],
        }
    })
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
            operations: admitCarrierOperations(carrier.operations),
            isExternal: carrier.isExternal === true,
            accountRequired: carrier.accountRequired !== false,
            readinessRequired: carrier.readinessRequired === true,
            productionCapability: ['ProductionReady', 'Partial', 'Unavailable', 'NotCertified'].includes(carrier.productionCapability) ? carrier.productionCapability : 'Unavailable',
            credentialFields: Array.isArray(carrier.credentialFields) ? carrier.credentialFields.map((field) => ({
                key: requiredString(field?.key, 'credential field key'),
                label: requiredString(field?.label, 'credential field label'),
                isSecret: field?.isSecret === true,
                required: field?.required === true,
                target: requiredString(field?.target, 'credential field target'),
            })) : [],
            environmentSupported: carrier.environmentSupported === true,
            connectionTestSupported: carrier.connectionTestSupported === true && ['LocalOnly', 'Remote'].includes(carrier.connectionTestMode),
            connectionTestMode: ['LocalOnly', 'Remote'].includes(carrier.connectionTestMode) ? carrier.connectionTestMode : 'Unsupported',
            requiresOriginReadiness: carrier.requiresOriginReadiness === true,
            requiresGeographyMapping: carrier.requiresGeographyMapping === true,
            supportsWebhooks: carrier.supportsWebhooks === true,
            webhookTrustModes: Array.isArray(carrier.webhookTrustModes) ? carrier.webhookTrustModes.filter((item) => typeof item === 'string') : [],
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
            savedCredentialFields: Array.isArray(account.savedCredentialFields) ? account.savedCredentialFields.filter(field => typeof field === 'string') : [],
            connectionTestIsCurrent: account.connectionTestIsCurrent === true,
            operations: admitCarrierOperations(account.operations),
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
            integrationReadiness: account.integrationReadiness ? {
                enabled: account.integrationReadiness.enabled === true,
                canExecute: account.integrationReadiness.canExecute === true,
                environment: stringOrNull(account.integrationReadiness.environment),
                productionCapability: stringOrNull(account.integrationReadiness.productionCapability) || 'Unavailable',
                blockers: Array.isArray(account.integrationReadiness.blockers)
                    ? account.integrationReadiness.blockers.filter((item) => typeof item === 'string')
                    : [],
                originReadinessRequired: account.integrationReadiness.originReadinessRequired === true,
                originReadinessSatisfied: account.integrationReadiness.originReadinessSatisfied === true,
                credentialAvailability: stringOrNull(account.integrationReadiness.credentialAvailability) || 'Unknown',
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

export function admitWarehouses(payload) {
    const seen = new Set()
    return requiredArray(payload, 'warehouses').map((warehouse) => {
        if (!warehouse || typeof warehouse !== 'object') {
            throw new Error('Shipping administration DTO rejected malformed warehouse.')
        }

        const id = requiredString(warehouse.id, 'warehouse identity')
        if (seen.has(id)) {
            throw new Error('Shipping administration DTO rejected duplicate warehouse.')
        }
        seen.add(id)

        return {
            id,
            name: requiredString(warehouse.name, 'warehouse name'),
            code: stringOrNull(warehouse.code),
            address: stringOrNull(warehouse.address),
            phone: stringOrNull(warehouse.phone),
            isActive: warehouse.isActive === true,
            isDefault: warehouse.isDefault === true,
        }
    })
}

export async function fetchWarehouses() {
    try {
        const response = await api.get('/warehouses', {
            headers: ADMIN_SHIPPING_READ_HEADERS,
        })
        return admitWarehouses(response.data)
    } catch (error) {
        throw new Error(getSafeApiErrorMessage(error, 'Failed to load warehouses.'))
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

function administrationFailureCodes(payload) {
    // ApiErrorResponse serializes Details as "data", retaining PascalCase
    // dictionary keys. Also accept older details envelopes.
    const details = payload?.data || payload?.details || payload?.Details
    const failure = payload?.failureCode || details?.failureCode || details?.FailureCode
    const blockers = payload?.blockers || details?.blockers || details?.Blockers
    return [...new Set([failure || payload?.code, ...(Array.isArray(blockers) ? blockers : [])]
        .filter(code => typeof code === 'string' && /^[A-Za-z0-9_-]{1,100}$/.test(code)))]
}

async function mutateShippingAdministration(path, method, payload) {
    let response
    try {
        response = await api[method](`${ADMIN_SHIPPING_BASE}${path}`, payload)
    } catch (error) {
        const codes = administrationFailureCodes(error?.response?.data)
        throw new Error(codes.join(' — ') || getSafeApiErrorMessage(error, 'Shipping administration action failed.'))
    }
    if (response.data?.succeeded !== true) {
        throw new Error(administrationFailureCodes(response.data).join(' — ') || 'Shipping administration action was not confirmed.')
    }
    return response.data
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
    const externalAccounts = accounts.filter((account) => account.carrierCode !== 'manual')
    const configuredCarriers = externalAccounts.length
    const activeCarriers = externalAccounts.filter((account) => account.integrationReadiness?.canExecute === true).length
    const blockedCarriers = externalAccounts.filter((account) => account.integrationReadiness?.canExecute === false).length
    const connectionWarnings = externalAccounts.filter((account) => account.lastConnectionTest?.status === 'Failed').length
    const activationBlockers = externalAccounts.filter((account) => account.integrationReadiness?.canExecute === false).length

    return {
        configuredCarriers,
        activeCarriers,
        blockedCarriers,
        connectionWarnings,
        activationBlockers,
        recommendedNextActionKey: configuredCarriers === 0
            ? 'createAccount'
            : activationBlockers > 0
                ? 'reviewBlockers'
                : 'monitor',
        recommendedNextAction: configuredCarriers === 0
            ? 'Create a carrier account from a backend-supported carrier.'
            : activationBlockers > 0
                ? 'Review provider readiness blockers.'
                : 'Monitor health and audit for changes.',
    }
}

export function getCarrierDisplayName(supportedCarriers, carrierCode) {
    return supportedCarriers.find((carrier) => carrier.carrierCode === carrierCode)?.displayName || carrierCode
}

export function getWarehouseDisplayName(warehouses, warehouseId) {
    const warehouse = warehouses.find((item) => item.id === warehouseId)
    if (!warehouse) {
        return `Unavailable warehouse (${warehouseId})`
    }

    return warehouse.code ? `${warehouse.name} (${warehouse.code})` : warehouse.name
}
