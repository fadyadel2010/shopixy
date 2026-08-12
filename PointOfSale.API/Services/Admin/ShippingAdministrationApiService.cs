using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.ShippingAdministration;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.ShippingAdministration;
using PointOfSale.Core.Interfaces.ShippingAdministration;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Shipping;

namespace PointOfSale.API.Services.Admin;

public sealed class ShippingAdministrationApiService : IShippingAdministrationApiService
{
    private readonly ApplicationDbContext _db;
    private readonly ICarrierAccountAdministrationRuntime _runtime;
    private readonly IShippingCarrierRegistry _carrierRegistry;
    private readonly ICarrierCapabilityRegistry _carrierCapabilityRegistry;
    private readonly ICarrierIntegrationAuthority _carrierIntegrationAuthority;

    public ShippingAdministrationApiService(
        ApplicationDbContext db,
        ICarrierAccountAdministrationRuntime runtime,
        IShippingCarrierRegistry carrierRegistry,
        ICarrierCapabilityRegistry? carrierCapabilityRegistry = null,
        ICarrierIntegrationAuthority? carrierIntegrationAuthority = null)
    {
        _db = db;
        _runtime = runtime;
        _carrierRegistry = carrierRegistry;
        _carrierCapabilityRegistry = carrierCapabilityRegistry ?? new CarrierCapabilityRegistry(carrierRegistry);
        _carrierIntegrationAuthority = carrierIntegrationAuthority ?? NullCarrierIntegrationAuthority.Instance;
    }

    public Task<IReadOnlyList<SupportedShippingCarrierDto>> ListSupportedCarriersAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SupportedShippingCarrierDto> carriers = _carrierCapabilityRegistry.GetAllCapabilities()
            .OrderBy(capability => capability.CarrierCode)
            .Select(capability =>
            {
                var adapter = _carrierRegistry.GetAdapter(capability.CarrierCode);
                var serviceLevels = (adapter?.GetSupportedServiceLevels() ?? Array.Empty<ServiceLevelInfo>())
                    .Select(level => new ShippingAdministrationServiceLevelDto(
                        level.Code,
                        level.Name,
                        level.Description,
                        level.TypicalDaysMin,
                        level.TypicalDaysMax))
                    .ToList();
                var isExternal = !CarrierCodes.IsManual(capability.CarrierCode);
                var productionBlocked = capability.ProductionCapability is
                    CarrierProductionCapability.Unavailable or
                    CarrierProductionCapability.NotCertified;
                return new SupportedShippingCarrierDto(
                    capability.CarrierCode,
                    capability.DisplayName,
                    serviceLevels,
                    IsExternal: isExternal,
                    AccountRequired: isExternal && !productionBlocked,
                    ReadinessRequired: isExternal && !productionBlocked,
                    ProductionCapability: capability.ProductionCapability.ToString(),
                    CredentialFields: capability.CredentialFields
                        .Select(field => new CarrierCredentialFieldDto(
                            field.Key,
                            field.Label,
                            field.IsSecret,
                            field.Required,
                            field.Target))
                        .ToList(),
                    EnvironmentSupported: capability.EnvironmentSupported,
                    ConnectionTestSupported: capability.ConnectionTestSupported,
                    ConnectionTestMode: capability.ConnectionTestMode.ToString(),
                    RequiresOriginReadiness: capability.RequiresOriginReadiness,
                    RequiresGeographyMapping: capability.RequiresGeographyMapping,
                    SupportsWebhooks: capability.SupportsWebhooks,
                    WebhookTrustModes: capability.WebhookTrustModes);
            })
            .ToList();

        return Task.FromResult(carriers);
    }

    public async Task<IReadOnlyList<CarrierAccountAdministrationDto>> ListCarrierAccountsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var accounts = await _db.CarrierAccounts
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(account => account.TenantId == tenantId &&
                              !account.IsDeleted &&
                              account.CarrierCode != CarrierCodes.Manual)
            .OrderBy(account => account.CarrierCode)
            .ToListAsync(cancellationToken);

        var result = new List<CarrierAccountAdministrationDto>(accounts.Count);
        foreach (var account in accounts)
        {
            result.Add(await MapAccountAsync(account, cancellationToken));
        }

        return result;
    }

    public async Task<CarrierAccountAdministrationDto?> GetCarrierAccountAsync(Guid tenantId, Guid carrierAccountId, CancellationToken cancellationToken)
    {
        var account = await _db.CarrierAccounts
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                item => item.TenantId == tenantId &&
                        item.Id == carrierAccountId &&
                        !item.IsDeleted,
                cancellationToken);

        return account == null ? null : await MapAccountAsync(account, cancellationToken);
    }

    public async Task<IReadOnlyList<CarrierAdministrationAuditDto>> GetAuditAsync(Guid tenantId, Guid? carrierAccountId, int limit, CancellationToken cancellationToken)
    {
        var boundedLimit = Math.Clamp(limit, 1, 250);
        var query = _db.CarrierAdministrationAudit
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(audit => audit.TenantId == tenantId && !audit.IsDeleted);

        if (carrierAccountId.HasValue)
        {
            query = query.Where(audit => audit.CarrierAccountId == carrierAccountId.Value);
        }

        return await query
            .OrderByDescending(audit => audit.OccurredAtUtc)
            .Take(boundedLimit)
            .Select(audit => new CarrierAdministrationAuditDto(
                audit.Id,
                audit.CarrierAccountId,
                audit.Action.ToString(),
                audit.ActorId,
                audit.CorrelationId,
                audit.PreviousState,
                audit.NextState,
                audit.Reason,
                audit.OccurredAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<ShippingAdministrationResultDto> CreateCarrierAccountAsync(Guid tenantId, CreateCarrierAccountCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.CarrierCode))
        {
            return Failure("UNKNOWN_CARRIER");
        }

        if (CarrierCodes.IsManual(command.CarrierCode))
        {
            return Failure("MANUAL_CARRIER_ACCOUNT_NOT_SUPPORTED");
        }

        var capability = _carrierCapabilityRegistry.GetCapability(command.CarrierCode);
        if (capability == null)
        {
            return Failure("UNKNOWN_CARRIER");
        }

        if (capability.ProductionCapability is CarrierProductionCapability.Unavailable or CarrierProductionCapability.NotCertified)
        {
            return Failure("CARRIER_NOT_PRODUCTION_ACTIVATABLE");
        }

        return (await _runtime.RegisterCarrierAccountAsync(tenantId, command.CarrierCode, actor, correlationId, cancellationToken)).ToDto();
    }

    public async Task<ShippingAdministrationResultDto> UpdateCarrierAccountAsync(Guid tenantId, Guid carrierAccountId, UpdateCarrierAccountCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.Environment))
        {
            return await SelectEnvironmentAsync(tenantId, carrierAccountId, new EnvironmentCommand(command.Environment), actor, correlationId, cancellationToken);
        }

        var account = await GetCarrierAccountAsync(tenantId, carrierAccountId, cancellationToken);
        return account == null
            ? Failure("UNKNOWN_CARRIER_ACCOUNT")
            : new ShippingAdministrationResultDto(true, carrierAccountId, account.State, account.HealthState, null, Array.Empty<string>());
    }

    public async Task<ShippingAdministrationResultDto> UpdateCredentialReferenceAsync(Guid tenantId, Guid carrierAccountId, CredentialReferenceCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.SecretReference))
        {
            return Failure("UNKNOWN_CREDENTIAL_REFERENCE", carrierAccountId);
        }

        var runtimeCommand = new CarrierCredentialReferenceCommand(
            carrierAccountId,
            command.SecretReference,
            command.MaskedFingerprint,
            command.ExpiresAtUtc);

        return (await _runtime.UpdateCredentialReferenceAsync(tenantId, runtimeCommand, actor, correlationId, cancellationToken)).ToDto();
    }

    public async Task<ShippingAdministrationResultDto> SelectEnvironmentAsync(Guid tenantId, Guid carrierAccountId, EnvironmentCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<CarrierAccountEnvironment>(command.Environment, ignoreCase: true, out var environment) ||
            !Enum.IsDefined(environment))
        {
            return Failure("UNKNOWN_ENVIRONMENT", carrierAccountId);
        }

        return (await _runtime.SelectEnvironmentAsync(tenantId, carrierAccountId, environment, actor, correlationId, cancellationToken)).ToDto();
    }

    public async Task<ShippingAdministrationResultDto> AssignWarehouseAsync(Guid tenantId, Guid carrierAccountId, WarehouseAssignmentCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken)
    {
        if (command.WarehouseId == Guid.Empty)
        {
            return Failure("UNKNOWN_WAREHOUSE", carrierAccountId);
        }

        var runtimeCommand = new CarrierWarehouseAssignmentCommand(
            carrierAccountId,
            command.WarehouseId,
            command.OriginReadinessPublished,
            command.ReadinessEvidenceReference);

        return (await _runtime.AssignWarehouseAsync(tenantId, runtimeCommand, actor, correlationId, cancellationToken)).ToDto();
    }

    public async Task<ShippingAdministrationResultDto> SetPolicyAsync(Guid tenantId, Guid carrierAccountId, string scope, PolicyCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<CarrierPolicyScope>(scope, ignoreCase: true, out var parsedScope) ||
            !Enum.IsDefined(parsedScope) ||
            !Enum.TryParse<CarrierPolicyMode>(command.Mode, ignoreCase: true, out var mode) ||
            !Enum.IsDefined(mode))
        {
            return Failure("UNKNOWN_POLICY", carrierAccountId);
        }

        var runtimeCommand = new CarrierPolicyCommand(
            carrierAccountId,
            parsedScope,
            mode,
            command.CustomsReadinessPublished,
            command.ReadinessEvidenceReference);

        return (await _runtime.SetPolicyAsync(tenantId, runtimeCommand, actor, correlationId, cancellationToken)).ToDto();
    }

    public async Task<ShippingAdministrationResultDto> SetServiceLevelAsync(Guid tenantId, Guid carrierAccountId, string serviceLevelCode, ServiceLevelCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serviceLevelCode))
        {
            return Failure("UNKNOWN_SERVICE_LEVEL", carrierAccountId);
        }

        return (await _runtime.SetServiceLevelAsync(tenantId, carrierAccountId, serviceLevelCode, command.Enabled, actor, correlationId, cancellationToken)).ToDto();
    }

    public async Task<ShippingAdministrationResultDto> RunConnectionTestAsync(Guid tenantId, Guid carrierAccountId, ConnectionTestCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken)
    {
        var account = await _db.CarrierAccounts
            .AsNoTracking()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.TenantId == tenantId && item.Id == carrierAccountId && !item.IsDeleted, cancellationToken);
        if (account == null)
        {
            return Failure("UNKNOWN_CARRIER_ACCOUNT", carrierAccountId);
        }

        var capability = _carrierCapabilityRegistry.GetCapability(account.CarrierCode);
        if (capability == null)
        {
            return Failure("UNKNOWN_CARRIER", carrierAccountId);
        }

        if (!capability.ConnectionTestSupported || capability.ConnectionTestMode == CarrierConnectionTestMode.Unsupported)
        {
            return Failure("CONNECTION_TEST_UNSUPPORTED", carrierAccountId);
        }

        if (capability.CredentialFields.Any(field => field.Required && string.IsNullOrWhiteSpace(ReadCredentialTarget(command, field.Target))))
        {
            return Failure("UNKNOWN_CREDENTIAL_REFERENCE", carrierAccountId);
        }

        var storeResult = await _carrierIntegrationAuthority.ValidateAndStoreIntegrationAsync(
            tenantId,
            account.CarrierCode,
            new CarrierIntegrationUpdate(
                DisplayName: null,
                IsEnabled: true,
                IsDefault: null,
                ApiKey: command.ApiKey,
                ApiSecret: command.ApiSecret,
                AccountId: command.AccountId,
                ApiUrl: command.ApiUrl,
                UseSandbox: command.UseSandbox,
                SupportsCod: null,
                SupportsTracking: null,
                SupportsWebhooks: null,
                SupportsLabels: null,
                Configuration: SerializeExtraConfig(command.ExtraConfig),
                PickupAddress: null,
                PickupCity: null,
                PickupCountry: null,
                PickupPhone: null,
                PickupContactName: null,
                DefaultServiceLevel: null,
                UseCarrierRates: null,
                FixedShippingCost: null,
                MarkupPercentage: null,
                FreeShippingThreshold: null,
                SortOrder: null,
                Notes: null),
            cancellationToken);

        if (!storeResult.Succeeded || storeResult.Configuration == null)
        {
            return Failure(storeResult.Blockers.FirstOrDefault() ?? "CARRIER_CONFIGURATION_SAVE_FAILED", carrierAccountId);
        }

        var validation = await _carrierIntegrationAuthority.ValidateResolvedCredentialsAsync(
            tenantId,
            account.CarrierCode,
            cancellationToken);

        return await RecordCanonicalConnectionValidationAsync(
            tenantId,
            carrierAccountId,
            command.UseSandbox,
            validation,
            actor,
            correlationId,
            cancellationToken);
    }

    public async Task<ShippingAdministrationResultDto> ActivateAsync(Guid tenantId, Guid carrierAccountId, ReadinessEvidenceCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken)
    {
        var account = await _db.CarrierAccounts
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.TenantId == tenantId && item.Id == carrierAccountId && !item.IsDeleted, cancellationToken);
        if (account == null)
        {
            return Failure("UNKNOWN_CARRIER_ACCOUNT", carrierAccountId);
        }

        var readiness = ToReadiness(command);
        if (account.State == CarrierAccountState.ConnectionTestPending)
        {
            var ready = (await _runtime.TransitionStateAsync(tenantId, carrierAccountId, CarrierAccountState.ReadyForActivation, readiness, actor, correlationId, cancellationToken)).ToDto();
            if (!ready.Succeeded)
            {
                return ready;
            }
        }

        return (await _runtime.TransitionStateAsync(tenantId, carrierAccountId, CarrierAccountState.Active, readiness, actor, correlationId, cancellationToken)).ToDto();
    }

    public async Task<ShippingAdministrationResultDto> SuspendAsync(Guid tenantId, Guid carrierAccountId, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken) =>
        (await _runtime.TransitionStateAsync(tenantId, carrierAccountId, CarrierAccountState.Suspended, new CarrierReadinessEvidence(true, true), actor, correlationId, cancellationToken)).ToDto();

    public async Task<ShippingAdministrationResultDto> DisableAsync(Guid tenantId, Guid carrierAccountId, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken) =>
        (await _runtime.TransitionStateAsync(tenantId, carrierAccountId, CarrierAccountState.Disabled, new CarrierReadinessEvidence(true, true), actor, correlationId, cancellationToken)).ToDto();

    public async Task<ShippingAdministrationResultDto> RetireAsync(Guid tenantId, Guid carrierAccountId, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken) =>
        (await _runtime.TransitionStateAsync(tenantId, carrierAccountId, CarrierAccountState.Retired, new CarrierReadinessEvidence(true, true), actor, correlationId, cancellationToken)).ToDto();

    public async Task<ShippingAdministrationResultDto> EvaluateHealthAsync(Guid tenantId, Guid carrierAccountId, ReadinessEvidenceCommand command, CarrierAdministrationActor actor, string correlationId, CancellationToken cancellationToken) =>
        (await _runtime.EvaluateHealthAsync(tenantId, carrierAccountId, ToReadiness(command), actor, correlationId, cancellationToken)).ToDto();

    private async Task<CarrierAccountAdministrationDto> MapAccountAsync(CarrierAccount account, CancellationToken cancellationToken)
    {
        var credentials = await _db.Set<CarrierCredentialReference>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == account.TenantId &&
                           item.CarrierAccountId == account.Id &&
                           !item.IsDeleted)
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new CarrierCredentialReferenceDto(
                item.Id,
                item.MaskedFingerprint,
                item.State.ToString(),
                item.ExpiresAtUtc))
            .ToListAsync(cancellationToken);

        var assignments = await _db.Set<CarrierWarehouseAssignment>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == account.TenantId &&
                           item.CarrierAccountId == account.Id &&
                           !item.IsDeleted)
            .OrderBy(item => item.WarehouseId)
            .Select(item => new CarrierWarehouseAssignmentDto(
                item.WarehouseId,
                item.IsEnabled,
                item.OriginReadinessPublished,
                item.ReadinessEvidenceReference))
            .ToListAsync(cancellationToken);

        var policies = await _db.Set<CarrierAccountPolicy>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == account.TenantId &&
                           item.CarrierAccountId == account.Id &&
                           !item.IsDeleted)
            .OrderBy(item => item.Scope)
            .Select(item => new CarrierPolicyDto(
                item.Scope.ToString(),
                item.Mode.ToString(),
                item.CustomsReadinessPublished,
                item.ReadinessEvidenceReference))
            .ToListAsync(cancellationToken);

        var enabledServiceLevels = await _db.Set<CarrierServiceLevelEnablement>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == account.TenantId &&
                           item.CarrierAccountId == account.Id &&
                           !item.IsDeleted)
            .ToDictionaryAsync(item => item.ServiceLevelCode, item => item.IsEnabled, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var adapter = _carrierRegistry.GetAdapter(account.CarrierCode);
        var serviceLevels = (adapter?.GetSupportedServiceLevels() ?? Array.Empty<ServiceLevelInfo>())
            .Select(level => new ShippingAdministrationServiceLevelDto(
                level.Code,
                level.Name,
                level.Description,
                level.TypicalDaysMin,
                level.TypicalDaysMax,
                enabledServiceLevels.TryGetValue(level.Code, out var enabled) ? enabled : null))
            .ToList();

        var lastConnectionTest = await _db.Set<CarrierConnectionTestRecord>()
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(item => item.TenantId == account.TenantId &&
                           item.CarrierAccountId == account.Id &&
                           !item.IsDeleted)
            .OrderByDescending(item => item.TestedAtUtc)
            .Select(item => new CarrierConnectionTestDto(
                item.Id,
                item.Environment.ToString(),
                item.Status.ToString(),
                item.FailureCode,
                item.FailureReason,
                item.TestedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

        var readiness = await _carrierIntegrationAuthority.GetIntegrationReadinessAsync(
            account.TenantId,
            account.CarrierCode,
            purpose: CarrierIntegrationPurpose.StorefrontAdmission,
            cancellationToken: cancellationToken);

        return new CarrierAccountAdministrationDto(
            account.Id,
            account.CarrierCode,
            account.Environment?.ToString(),
            account.ActiveCredentialReferenceId,
            account.State.ToString(),
            readiness.CanExecute ? CarrierOperationalState.Ready.ToString() : account.OperationalState.ToString(),
            readiness.CanExecute ? CarrierAccountHealthState.Healthy.ToString() : account.HealthState.ToString(),
            readiness.CanExecute ? null : account.HealthReason,
            account.LastHealthEvaluatedAtUtc,
            account.ActivatedAtUtc,
            account.SuspendedAtUtc,
            account.DisabledAtUtc,
            account.RetiredAtUtc,
            credentials,
            assignments,
            policies,
            serviceLevels,
            lastConnectionTest,
            new CarrierIntegrationReadinessDto(
                readiness.Enabled,
                readiness.CanExecute,
                readiness.Environment,
                readiness.ProductionCapability.ToString(),
                readiness.Blockers,
                readiness.OriginReadinessRequired,
                readiness.OriginReadinessSatisfied,
                readiness.CredentialAvailability));
    }

    private static CarrierReadinessEvidence ToReadiness(ReadinessEvidenceCommand command) =>
        new(command.CarrierMappingPublished, command.ProviderCapabilitySupported);

    private static string? ReadCredentialTarget(ConnectionTestCommand command, string target)
    {
        if (string.Equals(target, "ApiKey", StringComparison.OrdinalIgnoreCase))
            return command.ApiKey;
        if (string.Equals(target, "ApiSecret", StringComparison.OrdinalIgnoreCase))
            return command.ApiSecret;
        if (string.Equals(target, "AccountId", StringComparison.OrdinalIgnoreCase))
            return command.AccountId;
        if (string.Equals(target, "ApiUrl", StringComparison.OrdinalIgnoreCase))
            return command.ApiUrl;
        if (target.StartsWith("ExtraConfig.", StringComparison.OrdinalIgnoreCase))
        {
            var key = target["ExtraConfig.".Length..];
            return command.ExtraConfig != null && command.ExtraConfig.TryGetValue(key, out var value)
                ? value
                : null;
        }

        return null;
    }

    private async Task<ShippingAdministrationResultDto> RecordCanonicalConnectionValidationAsync(
        Guid tenantId,
        Guid carrierAccountId,
        bool useSandbox,
        CarrierValidationResult validation,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var account = await _db.CarrierAccounts
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.TenantId == tenantId && item.Id == carrierAccountId && !item.IsDeleted, cancellationToken);
        if (account == null)
        {
            return Failure("UNKNOWN_CARRIER_ACCOUNT", carrierAccountId);
        }

        account.Environment = useSandbox ? CarrierAccountEnvironment.Sandbox : CarrierAccountEnvironment.Production;
        account.LastHealthEvaluatedAtUtc = DateTime.UtcNow;

        if (validation.Success)
        {
            if (account.State is CarrierAccountState.Draft or CarrierAccountState.CredentialsPending)
            {
                account.State = CarrierAccountState.ConnectionTestPending;
            }

            account.OperationalState = CarrierOperationalState.Ready;
            account.HealthState = CarrierAccountHealthState.Healthy;
            account.HealthReason = null;
        }
        else
        {
            account.OperationalState = CarrierOperationalState.NotReady;
            account.HealthState = IsProviderConnectivityFailure(validation.ErrorCode)
                ? CarrierAccountHealthState.Warning
                : CarrierAccountHealthState.Blocked;
            account.HealthReason = ToMerchantConnectionFailure(validation.ErrorCode);
        }

        var record = new CarrierConnectionTestRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CarrierAccountId = account.Id,
            CredentialReferenceId = account.ActiveCredentialReferenceId,
            Environment = account.Environment.Value,
            Status = validation.Success ? CarrierConnectionTestStatus.Succeeded : CarrierConnectionTestStatus.Failed,
            FailureCode = validation.Success ? null : validation.ErrorCode ?? "CONNECTION_VERIFICATION_FAILED",
            FailureReason = validation.Success ? null : validation.ErrorMessage,
            TestedAtUtc = DateTime.UtcNow
        };
        _db.Set<CarrierConnectionTestRecord>().Add(record);
        _db.Set<CarrierAdministrationAudit>().Add(new CarrierAdministrationAudit
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CarrierAccountId = account.Id,
            Action = CarrierAdministrationAuditAction.ConnectionTest,
            ActorId = actor.ActorId,
            CorrelationId = correlationId,
            PreviousState = null,
            NextState = record.Status.ToString(),
            Reason = validation.Success ? "Persisted credentials validated" : record.FailureCode,
            OccurredAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        return validation.Success
            ? new ShippingAdministrationResultDto(true, account.Id, account.State.ToString(), account.HealthState.ToString(), null, Array.Empty<string>())
            : new ShippingAdministrationResultDto(false, account.Id, account.State.ToString(), account.HealthState.ToString(), record.FailureCode, new[] { record.FailureCode ?? "CONNECTION_VERIFICATION_FAILED" });
    }

    private static string? SerializeExtraConfig(IReadOnlyDictionary<string, string>? extraConfig)
    {
        if (extraConfig == null || extraConfig.Count == 0)
        {
            return null;
        }

        var values = extraConfig
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Trim(), StringComparer.OrdinalIgnoreCase);
        return values.Count == 0 ? null : JsonSerializer.Serialize(values);
    }

    private static bool IsProviderConnectivityFailure(string? errorCode) =>
        string.Equals(errorCode, "CONNECTION_ERROR", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, "NETWORK_ERROR", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, "PROVIDER_UNAVAILABLE", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(errorCode, "CONNECTION_TEST_EXCEPTION", StringComparison.OrdinalIgnoreCase);

    private static string ToMerchantConnectionFailure(string? errorCode) =>
        IsProviderConnectivityFailure(errorCode)
            ? "Provider could not be reached. Credentials were saved and were not marked invalid."
            : string.Equals(errorCode, "INVALID_CREDENTIALS", StringComparison.OrdinalIgnoreCase)
                ? "Connection verification failed. Check the saved credentials and retry."
                : "Connection verification failed. Credentials remain saved for retry.";

    private static ShippingAdministrationResultDto Failure(string failureCode, Guid? carrierAccountId = null) =>
        new(false, carrierAccountId, null, null, failureCode, new[] { failureCode });
}
