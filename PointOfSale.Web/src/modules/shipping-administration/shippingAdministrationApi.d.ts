export interface CarrierServiceLevel {
    code: string; name: string; description: string | null;
    typicalDaysMin: number; typicalDaysMax: number; isEnabled: boolean | null;
}
export interface CarrierOperation {
    operation: 'quote' | 'create' | 'tracking' | 'label' | 'cancellation' | 'recovery';
    implementation: 'Implemented' | 'LocalOnly' | 'StoredOnly' | 'Unavailable' | 'ReconciliationOnly';
    accountReady: boolean; requiresShipmentEvidence: boolean; blockers: string[]; limitations: string[];
}
export function admitCarrierOperations(payload: unknown): CarrierOperation[];
export interface SupportedCarrier {
    carrierCode: string; displayName: string; isExternal: boolean; accountRequired: boolean; readinessRequired: boolean;
    productionCapability: string; environmentSupported: boolean; connectionTestSupported: boolean; connectionTestMode: string;
    requiresOriginReadiness: boolean; requiresGeographyMapping: boolean; supportsWebhooks: boolean; webhookTrustModes: string[];
    credentialFields: Array<{ key: string; label: string; isSecret: boolean; required: boolean; target: string }>;
    serviceLevels: CarrierServiceLevel[];
    operations: CarrierOperation[];
}
export interface CarrierAccount {
    savedCredentialFields?: string[]; connectionTestIsCurrent?: boolean;
    id: string; carrierCode: string; environment: string | null; activeCredentialReferenceId: string | null;
    state: string; operationalState: string; healthState: string; healthReason: string | null;
    lastHealthEvaluatedAtUtc: string | null; activatedAtUtc: string | null; suspendedAtUtc: string | null;
    disabledAtUtc: string | null; retiredAtUtc: string | null;
    operations: CarrierOperation[];
    credentialReferences: Array<{ id: string; maskedFingerprint: string | null; state: string; expiresAtUtc: string | null }>;
    warehouseAssignments: Array<{ warehouseId: string; isEnabled: boolean; originReadinessPublished: boolean; readinessEvidenceReference: string | null }>;
    policies: Array<{ scope: string; mode: string; customsReadinessPublished: boolean; readinessEvidenceReference: string | null }>;
    serviceLevels: CarrierServiceLevel[];
    lastConnectionTest: { id: string; environment: string; status: string; failureCode: string | null; failureReason: string | null; testedAtUtc: string } | null;
    integrationReadiness: { enabled: boolean; canExecute: boolean; environment: string | null; productionCapability: string;
        blockers: string[]; originReadinessRequired: boolean; originReadinessSatisfied: boolean; credentialAvailability: string } | null;
}
export interface Warehouse {
    id: string; name: string; code: string | null; address: string | null; phone: string | null; isActive: boolean; isDefault: boolean;
}
export interface AdministrationAudit {
    id: string; carrierAccountId: string; action: string; actorId: string; correlationId: string | null;
    previousState: string | null; nextState: string | null; reason: string | null; occurredAtUtc: string;
}
export interface AdministrationResult { succeeded: boolean; carrierAccountId: string | null; state: string | null; healthState: string | null; failureCode: string | null; blockers: string[] }
export interface ConnectionResult { id: string; environment: string; status: string; failureCode: string | null; failureReason: string | null; testedAtUtc: string }
export interface ConnectionCommand {
    apiKey?: string | null; apiSecret?: string | null; accountId?: string | null; apiUrl?: string | null;
    useSandbox: boolean; extraConfig?: Record<string, string> | null; webhookSecret?: string | null; saveOnly?: boolean; useSavedCredentials?: boolean;
}
export function admitSupportedCarriers(payload: unknown): SupportedCarrier[];
export function admitCarrierAccounts(payload: unknown): CarrierAccount[];
export function admitWarehouses(payload: unknown): Warehouse[];
export function admitAdministrationAudit(payload: unknown): AdministrationAudit[];
export function fetchSupportedCarriers(): Promise<SupportedCarrier[]>;
export function fetchCarrierAccounts(): Promise<CarrierAccount[]>;
export function fetchWarehouses(): Promise<Warehouse[]>;
export function fetchShippingAdministrationAudit(limit?: number): Promise<AdministrationAudit[]>;
export function createCarrierAccount(carrierCode: string): Promise<AdministrationResult>;
export function runCarrierConnectionTest(id: string, command: ConnectionCommand): Promise<AdministrationResult>;
export function selectCarrierEnvironment(id: string, environment: string): Promise<AdministrationResult>;
export function setCarrierServiceLevel(id: string, code: string, enabled: boolean): Promise<AdministrationResult>;
export function updateCredentialReference(id: string, command: { secretReference: string; maskedFingerprint?: string | null; expiresAtUtc?: string | null }): Promise<AdministrationResult>;
export function assignCarrierWarehouse(id: string, warehouseId: string, command: { originReadinessPublished: boolean; readinessEvidenceReference?: string | null }): Promise<AdministrationResult>;
export function setCarrierPolicy(id: string, scope: string, command: { mode: string; customsReadinessPublished: boolean; readinessEvidenceReference?: string | null }): Promise<AdministrationResult>;
export function activateCarrierAccount(id: string): Promise<AdministrationResult>;
export function evaluateCarrierHealth(id: string): Promise<AdministrationResult>;
export function suspendCarrierAccount(id: string): Promise<AdministrationResult>;
export function disableCarrierAccount(id: string): Promise<AdministrationResult>;
export function retireCarrierAccount(id: string): Promise<AdministrationResult>;
export function getCarrierDisplayName(carriers: SupportedCarrier[], code: string): string;
export function getWarehouseDisplayName(warehouses: Warehouse[], id: string): string;
export function createShippingAdministrationSummary(accounts: CarrierAccount[]): {
    configuredCarriers: number; activeCarriers: number; blockedCarriers: number; connectionWarnings: number; activationBlockers: number;
    recommendedNextActionKey: string; recommendedNextAction: string;
};
