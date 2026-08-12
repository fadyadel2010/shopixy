using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PointOfSale.Core.Constants;
using PointOfSale.Core.Entities.ShippingAdministration;
using PointOfSale.Core.Interfaces.ShippingAdministration;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Shipping.Administration;

public sealed class CarrierAccountAdministrationRuntime : ICarrierAccountAdministrationRuntime
{
    private static readonly IReadOnlyDictionary<CarrierAccountState, CarrierAccountState[]> AllowedTransitions =
        new Dictionary<CarrierAccountState, CarrierAccountState[]>
        {
            [CarrierAccountState.Draft] = new[] { CarrierAccountState.CredentialsPending },
            [CarrierAccountState.CredentialsPending] = new[] { CarrierAccountState.ConnectionTestPending },
            [CarrierAccountState.ConnectionTestPending] = new[] { CarrierAccountState.ReadyForActivation },
            [CarrierAccountState.ReadyForActivation] = new[] { CarrierAccountState.Active, CarrierAccountState.Disabled },
            [CarrierAccountState.Active] = new[] { CarrierAccountState.Suspended, CarrierAccountState.Disabled },
            [CarrierAccountState.Suspended] = new[] { CarrierAccountState.Active, CarrierAccountState.Disabled },
            [CarrierAccountState.Disabled] = new[] { CarrierAccountState.Retired },
            [CarrierAccountState.Retired] = Array.Empty<CarrierAccountState>()
        };

    private readonly ApplicationDbContext _db;
    private readonly IShippingCarrierRegistry _carrierRegistry;
    private readonly ILogger<CarrierAccountAdministrationRuntime> _logger;

    public CarrierAccountAdministrationRuntime(
        ApplicationDbContext db,
        IShippingCarrierRegistry carrierRegistry,
        ILogger<CarrierAccountAdministrationRuntime> logger)
    {
        _db = db;
        _carrierRegistry = carrierRegistry;
        _logger = logger;
    }

    public async Task<CarrierAccountRuntimeResult> RegisterCarrierAccountAsync(
        Guid tenantId,
        string carrierCode,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var normalizedCarrierCode = CarrierCodes.Normalize(carrierCode);
        if (CarrierCodes.IsManual(normalizedCarrierCode))
        {
            return CarrierAccountRuntimeResult.Failure(
                "MANUAL_CARRIER_ACCOUNT_NOT_SUPPORTED",
                new[] { "Manual Fulfillment does not require a carrier account." });
        }

        if (!_carrierRegistry.IsCarrierSupported(normalizedCarrierCode))
        {
            return CarrierAccountRuntimeResult.Failure("UNKNOWN_CARRIER", new[] { "Unknown carrier" });
        }

        var existing = await _db.Set<CarrierAccount>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                account => account.TenantId == tenantId &&
                           account.CarrierCode == normalizedCarrierCode &&
                           !account.IsDeleted,
                cancellationToken);

        if (existing != null)
        {
            return CarrierAccountRuntimeResult.Success(existing.Id, existing.State, existing.HealthState);
        }

        var account = new CarrierAccount
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CarrierCode = normalizedCarrierCode,
            State = CarrierAccountState.Draft,
            OperationalState = CarrierOperationalState.NotReady,
            HealthState = CarrierAccountHealthState.Blocked,
            HealthReason = "Carrier account is registered but incomplete"
        };

        _db.Set<CarrierAccount>().Add(account);
        AddAudit(account, CarrierAdministrationAuditAction.Registration, actor, correlationId, null, account.State.ToString(), "Carrier account registered");
        await _db.SaveChangesAsync(cancellationToken);
        return CarrierAccountRuntimeResult.Success(account.Id, account.State, account.HealthState, new[] { "CREDENTIAL_REFERENCE_MISSING" });
    }

    public async Task<CarrierAccountRuntimeResult> UpdateCredentialReferenceAsync(
        Guid tenantId,
        CarrierCredentialReferenceCommand command,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var account = await FindAccountAsync(tenantId, command.CarrierAccountId, cancellationToken);
        if (account == null)
        {
            return CarrierAccountRuntimeResult.Failure("UNKNOWN_CARRIER_ACCOUNT");
        }

        if (string.IsNullOrWhiteSpace(command.SecretReference))
        {
            return CarrierAccountRuntimeResult.Failure("UNKNOWN_CREDENTIAL_REFERENCE", carrierAccountId: account.Id, state: account.State, healthState: account.HealthState);
        }

        var currentActive = await _db.Set<CarrierCredentialReference>()
            .IgnoreQueryFilters()
            .Where(reference => reference.TenantId == tenantId &&
                                reference.CarrierAccountId == account.Id &&
                                reference.State == CarrierCredentialReferenceState.Active)
            .ToListAsync(cancellationToken);

        foreach (var reference in currentActive)
        {
            reference.State = CarrierCredentialReferenceState.Rotating;
        }

        var credentialReference = new CarrierCredentialReference
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CarrierAccountId = account.Id,
            SecretReference = command.SecretReference.Trim(),
            MaskedFingerprint = command.MaskedFingerprint,
            ExpiresAtUtc = command.ExpiresAtUtc,
            State = CarrierCredentialReferenceState.Active
        };

        _db.Set<CarrierCredentialReference>().Add(credentialReference);
        account.ActiveCredentialReferenceId = credentialReference.Id;
        if (account.State == CarrierAccountState.Draft)
        {
            account.State = CarrierAccountState.CredentialsPending;
        }

        AddAudit(account, CarrierAdministrationAuditAction.CredentialReferenceUpdated, actor, correlationId, null, account.State.ToString(), "Credential reference updated");
        await _db.SaveChangesAsync(cancellationToken);
        return CarrierAccountRuntimeResult.Success(account.Id, account.State, account.HealthState);
    }

    public async Task<CarrierAccountRuntimeResult> SelectEnvironmentAsync(
        Guid tenantId,
        Guid carrierAccountId,
        CarrierAccountEnvironment environment,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(environment))
        {
            return CarrierAccountRuntimeResult.Failure("UNKNOWN_ENVIRONMENT");
        }

        var account = await FindAccountAsync(tenantId, carrierAccountId, cancellationToken);
        if (account == null)
        {
            return CarrierAccountRuntimeResult.Failure("UNKNOWN_CARRIER_ACCOUNT");
        }

        var previous = account.Environment?.ToString();
        account.Environment = environment;
        AddAudit(account, CarrierAdministrationAuditAction.EnvironmentChanged, actor, correlationId, previous, environment.ToString(), "Environment selected");
        await _db.SaveChangesAsync(cancellationToken);
        return CarrierAccountRuntimeResult.Success(account.Id, account.State, account.HealthState);
    }

    public async Task<CarrierAccountRuntimeResult> AssignWarehouseAsync(
        Guid tenantId,
        CarrierWarehouseAssignmentCommand command,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var account = await FindAccountAsync(tenantId, command.CarrierAccountId, cancellationToken);
        if (account == null)
        {
            return CarrierAccountRuntimeResult.Failure("UNKNOWN_CARRIER_ACCOUNT");
        }

        if (CarrierCodes.IsManual(account.CarrierCode))
        {
            return CarrierAccountRuntimeResult.Failure(
                "MANUAL_CARRIER_ACCOUNT_NOT_SUPPORTED",
                new[] { "Manual Fulfillment does not require warehouse assignments." },
                account.Id,
                account.State,
                account.HealthState);
        }

        var warehouseExists = await _db.Warehouses
            .IgnoreQueryFilters()
            .AnyAsync(
                warehouse => warehouse.Id == command.WarehouseId &&
                             warehouse.TenantId == tenantId &&
                             warehouse.IsActive &&
                             !warehouse.IsDeleted,
                cancellationToken);
        if (!warehouseExists)
        {
            return CarrierAccountRuntimeResult.Failure("UNKNOWN_WAREHOUSE", carrierAccountId: account.Id, state: account.State, healthState: account.HealthState);
        }

        var assignment = await _db.Set<CarrierWarehouseAssignment>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                item => item.TenantId == tenantId &&
                        item.CarrierAccountId == account.Id &&
                        item.WarehouseId == command.WarehouseId &&
                        !item.IsDeleted,
                cancellationToken);

        if (assignment == null)
        {
            assignment = new CarrierWarehouseAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CarrierAccountId = account.Id,
                WarehouseId = command.WarehouseId
            };
            _db.Set<CarrierWarehouseAssignment>().Add(assignment);
        }

        assignment.IsEnabled = true;
        assignment.OriginReadinessPublished = command.OriginReadinessPublished;
        assignment.ReadinessEvidenceReference = command.ReadinessEvidenceReference;

        AddAudit(account, CarrierAdministrationAuditAction.WarehouseAssignment, actor, correlationId, null, command.WarehouseId.ToString(), "Warehouse assigned");
        await _db.SaveChangesAsync(cancellationToken);
        return CarrierAccountRuntimeResult.Success(account.Id, account.State, account.HealthState);
    }

    public async Task<CarrierAccountRuntimeResult> SetPolicyAsync(
        Guid tenantId,
        CarrierPolicyCommand command,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(command.Scope) || !Enum.IsDefined(command.Mode))
        {
            return CarrierAccountRuntimeResult.Failure("UNKNOWN_POLICY");
        }

        var account = await FindAccountAsync(tenantId, command.CarrierAccountId, cancellationToken);
        if (account == null)
        {
            return CarrierAccountRuntimeResult.Failure("UNKNOWN_CARRIER_ACCOUNT");
        }

        var policy = await _db.Set<CarrierAccountPolicy>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                item => item.TenantId == tenantId &&
                        item.CarrierAccountId == account.Id &&
                        item.Scope == command.Scope,
                cancellationToken);

        if (policy == null)
        {
            policy = new CarrierAccountPolicy
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CarrierAccountId = account.Id,
                Scope = command.Scope
            };
            _db.Set<CarrierAccountPolicy>().Add(policy);
        }

        policy.Mode = command.Mode;
        policy.CustomsReadinessPublished = command.CustomsReadinessPublished;
        policy.ReadinessEvidenceReference = command.ReadinessEvidenceReference;
        AddAudit(account, CarrierAdministrationAuditAction.PolicyChanged, actor, correlationId, null, command.Scope.ToString(), "Policy changed");
        await _db.SaveChangesAsync(cancellationToken);
        return CarrierAccountRuntimeResult.Success(account.Id, account.State, account.HealthState);
    }

    public async Task<CarrierAccountRuntimeResult> SetServiceLevelAsync(
        Guid tenantId,
        Guid carrierAccountId,
        string serviceLevelCode,
        bool enabled,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serviceLevelCode))
        {
            return CarrierAccountRuntimeResult.Failure("UNKNOWN_SERVICE_LEVEL");
        }

        var account = await FindAccountAsync(tenantId, carrierAccountId, cancellationToken);
        if (account == null)
        {
            return CarrierAccountRuntimeResult.Failure("UNKNOWN_CARRIER_ACCOUNT");
        }

        var adapter = _carrierRegistry.GetAdapter(account.CarrierCode);
        var supported = adapter?.GetSupportedServiceLevels()
            .Any(level => string.Equals(level.Code, serviceLevelCode, StringComparison.OrdinalIgnoreCase)) == true;
        if (!supported)
        {
            return CarrierAccountRuntimeResult.Failure("UNKNOWN_SERVICE_LEVEL", carrierAccountId: account.Id, state: account.State, healthState: account.HealthState);
        }

        var normalizedServiceLevel = serviceLevelCode.Trim().ToLowerInvariant();
        var serviceLevel = await _db.Set<CarrierServiceLevelEnablement>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                item => item.TenantId == tenantId &&
                        item.CarrierAccountId == account.Id &&
                        item.ServiceLevelCode == normalizedServiceLevel,
                cancellationToken);

        if (serviceLevel == null)
        {
            serviceLevel = new CarrierServiceLevelEnablement
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CarrierAccountId = account.Id,
                ServiceLevelCode = normalizedServiceLevel
            };
            _db.Set<CarrierServiceLevelEnablement>().Add(serviceLevel);
        }

        serviceLevel.IsEnabled = enabled;
        AddAudit(account, CarrierAdministrationAuditAction.ServiceLevelChanged, actor, correlationId, null, normalizedServiceLevel, "Service level changed");
        await _db.SaveChangesAsync(cancellationToken);
        return CarrierAccountRuntimeResult.Success(account.Id, account.State, account.HealthState);
    }

    public async Task<CarrierAccountRuntimeResult> RunConnectionTestAsync(
        Guid tenantId,
        Guid carrierAccountId,
        CarrierConnectionTestCredentials credentials,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var account = await FindAccountAsync(tenantId, carrierAccountId, cancellationToken);
        if (account == null)
        {
            return CarrierAccountRuntimeResult.Failure("UNKNOWN_CARRIER_ACCOUNT");
        }

        if (account.Environment == null)
        {
            return CarrierAccountRuntimeResult.Failure("ENVIRONMENT_MISSING", carrierAccountId: account.Id, state: account.State, healthState: account.HealthState);
        }

        var adapter = _carrierRegistry.GetAdapter(account.CarrierCode);
        if (adapter == null)
        {
            return CarrierAccountRuntimeResult.Failure("UNKNOWN_CARRIER", carrierAccountId: account.Id, state: account.State, healthState: account.HealthState);
        }

        CarrierValidationResult validation;
        try
        {
            validation = await adapter.ValidateCredentialsAsync(
                new CarrierCredentials
                {
                    ApiKey = credentials.ApiKey,
                    ApiSecret = credentials.ApiSecret,
                    AccountId = credentials.AccountId,
                    ApiUrl = credentials.ApiUrl,
                    UseSandbox = credentials.UseSandbox,
                    ExtraConfig = credentials.ExtraConfig?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new Dictionary<string, string>()
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Carrier connection test failed for account {CarrierAccountId}", account.Id);
            validation = new CarrierValidationResult
            {
                Success = false,
                ErrorCode = "CONNECTION_TEST_EXCEPTION",
                ErrorMessage = "Connection test failed"
            };
        }

        var record = new CarrierConnectionTestRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CarrierAccountId = account.Id,
            CredentialReferenceId = account.ActiveCredentialReferenceId,
            Environment = account.Environment.Value,
            Status = validation.Success ? CarrierConnectionTestStatus.Succeeded : CarrierConnectionTestStatus.Failed,
            FailureCode = validation.Success ? null : validation.ErrorCode ?? "CONNECTION_TEST_FAILED",
            FailureReason = validation.Success ? null : validation.ErrorMessage,
            TestedAtUtc = DateTime.UtcNow
        };
        _db.Set<CarrierConnectionTestRecord>().Add(record);

        if (validation.Success && account.State == CarrierAccountState.CredentialsPending)
        {
            account.State = CarrierAccountState.ConnectionTestPending;
        }

        AddAudit(account, CarrierAdministrationAuditAction.ConnectionTest, actor, correlationId, null, record.Status.ToString(), validation.Success ? "Connection test succeeded" : record.FailureCode);
        await _db.SaveChangesAsync(cancellationToken);

        return validation.Success
            ? CarrierAccountRuntimeResult.Success(account.Id, account.State, account.HealthState)
            : CarrierAccountRuntimeResult.Failure(record.FailureCode ?? "CONNECTION_TEST_FAILED", new[] { record.FailureCode ?? "CONNECTION_TEST_FAILED" }, account.Id, account.State, account.HealthState);
    }

    public async Task<CarrierAccountRuntimeResult> TransitionStateAsync(
        Guid tenantId,
        Guid carrierAccountId,
        CarrierAccountState nextState,
        CarrierReadinessEvidence readiness,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(nextState))
        {
            return CarrierAccountRuntimeResult.Failure("UNKNOWN_STATE_TRANSITION");
        }

        var account = await FindAccountAsync(tenantId, carrierAccountId, cancellationToken);
        if (account == null)
        {
            return CarrierAccountRuntimeResult.Failure("UNKNOWN_CARRIER_ACCOUNT");
        }

        if (!AllowedTransitions.TryGetValue(account.State, out var nextStates) || !nextStates.Contains(nextState))
        {
            AddAudit(account, CarrierAdministrationAuditAction.StateTransitionRejected, actor, correlationId, account.State.ToString(), nextState.ToString(), "Illegal carrier account transition");
            await _db.SaveChangesAsync(cancellationToken);
            return CarrierAccountRuntimeResult.Failure("UNKNOWN_STATE_TRANSITION", new[] { "Illegal state transition" }, account.Id, account.State, account.HealthState);
        }

        if (nextState == CarrierAccountState.Active)
        {
            var gates = await EvaluateActivationGatesAsync(account, readiness, cancellationToken);
            if (gates.Count > 0)
            {
                AddAudit(account, CarrierAdministrationAuditAction.ActivationRejected, actor, correlationId, account.State.ToString(), nextState.ToString(), string.Join(",", gates));
                await _db.SaveChangesAsync(cancellationToken);
                return CarrierAccountRuntimeResult.Failure("ACTIVATION_GATES_BLOCKED", gates, account.Id, account.State, account.HealthState);
            }
        }

        var previous = account.State;
        account.State = nextState;
        ApplyOperationalState(account);
        AddAudit(account, AuditActionFor(nextState), actor, correlationId, previous.ToString(), nextState.ToString(), "Carrier account state changed");
        await EvaluateAndApplyHealthAsync(account, readiness, actor, correlationId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return CarrierAccountRuntimeResult.Success(account.Id, account.State, account.HealthState);
    }

    public async Task<CarrierAccountRuntimeResult> EvaluateHealthAsync(
        Guid tenantId,
        Guid carrierAccountId,
        CarrierReadinessEvidence readiness,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var account = await FindAccountAsync(tenantId, carrierAccountId, cancellationToken);
        if (account == null)
        {
            return CarrierAccountRuntimeResult.Failure("UNKNOWN_CARRIER_ACCOUNT");
        }

        var blockers = await EvaluateAndApplyHealthAsync(account, readiness, actor, correlationId, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return CarrierAccountRuntimeResult.Success(account.Id, account.State, account.HealthState, blockers);
    }

    private async Task<CarrierAccount?> FindAccountAsync(Guid tenantId, Guid carrierAccountId, CancellationToken cancellationToken) =>
        await _db.Set<CarrierAccount>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                account => account.Id == carrierAccountId &&
                           account.TenantId == tenantId &&
                           !account.IsDeleted,
                cancellationToken);

    private async Task<IReadOnlyList<string>> EvaluateAndApplyHealthAsync(
        CarrierAccount account,
        CarrierReadinessEvidence readiness,
        CarrierAdministrationActor actor,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var previous = account.HealthState;
        var blockers = await EvaluateActivationGatesAsync(account, readiness, cancellationToken);

        account.HealthState = account.State switch
        {
            CarrierAccountState.Disabled => CarrierAccountHealthState.Disabled,
            CarrierAccountState.Retired => CarrierAccountHealthState.Retired,
            _ when blockers.Count > 0 => CarrierAccountHealthState.Blocked,
            CarrierAccountState.Active => CarrierAccountHealthState.Healthy,
            _ => CarrierAccountHealthState.Warning
        };
        account.HealthReason = blockers.Count == 0 ? null : string.Join(",", blockers);
        account.LastHealthEvaluatedAtUtc = DateTime.UtcNow;

        if (previous != account.HealthState)
        {
            AddAudit(account, CarrierAdministrationAuditAction.HealthChanged, actor, correlationId, previous.ToString(), account.HealthState.ToString(), account.HealthReason);
        }

        return blockers;
    }

    private async Task<List<string>> EvaluateActivationGatesAsync(
        CarrierAccount account,
        CarrierReadinessEvidence readiness,
        CancellationToken cancellationToken)
    {
        var blockers = new List<string>();
        if (account.ActiveCredentialReferenceId == null ||
            !await _db.Set<CarrierCredentialReference>().IgnoreQueryFilters().AnyAsync(
                reference => reference.Id == account.ActiveCredentialReferenceId &&
                             reference.TenantId == account.TenantId &&
                             reference.State == CarrierCredentialReferenceState.Active,
                cancellationToken))
        {
            blockers.Add("CREDENTIAL_REFERENCE_MISSING");
        }

        if (account.Environment == null)
        {
            blockers.Add("ENVIRONMENT_MISSING");
        }

        if (!await _db.Set<CarrierConnectionTestRecord>().IgnoreQueryFilters().AnyAsync(
                test => test.TenantId == account.TenantId &&
                        test.CarrierAccountId == account.Id &&
                        test.Status == CarrierConnectionTestStatus.Succeeded,
                cancellationToken))
        {
            blockers.Add("CONNECTION_TEST_MISSING");
        }

        var activeAssignments = await _db.Set<CarrierWarehouseAssignment>()
            .IgnoreQueryFilters()
            .Where(assignment => assignment.TenantId == account.TenantId &&
                                 assignment.CarrierAccountId == account.Id &&
                                 assignment.IsEnabled &&
                                 !assignment.IsDeleted)
            .ToListAsync(cancellationToken);
        if (activeAssignments.Count == 0)
        {
            blockers.Add("WAREHOUSE_ASSIGNMENT_MISSING");
        }
        else if (activeAssignments.Any(assignment => !assignment.OriginReadinessPublished))
        {
            blockers.Add("ORIGIN_READINESS_MISSING");
        }

        if (readiness.CarrierMappingPublished != true)
        {
            blockers.Add(readiness.CarrierMappingPublished == null ? "UNKNOWN_READINESS" : "CARRIER_MAPPING_UNPUBLISHED");
        }

        var internationalPolicy = await _db.Set<CarrierAccountPolicy>()
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                policy => policy.TenantId == account.TenantId &&
                          policy.CarrierAccountId == account.Id &&
                          policy.Scope == CarrierPolicyScope.International,
                cancellationToken);
        if (internationalPolicy?.Mode == CarrierPolicyMode.Enabled &&
            !internationalPolicy.CustomsReadinessPublished)
        {
            blockers.Add("CUSTOMS_READINESS_MISSING");
        }

        if (readiness.ProviderCapabilitySupported != true)
        {
            blockers.Add(readiness.ProviderCapabilitySupported == null ? "UNKNOWN_ACTIVATION_GATE" : "PROVIDER_CAPABILITY_UNSUPPORTED");
        }

        var enabledServiceLevels = await _db.Set<CarrierServiceLevelEnablement>()
            .IgnoreQueryFilters()
            .Where(level => level.TenantId == account.TenantId &&
                            level.CarrierAccountId == account.Id &&
                            level.IsEnabled)
            .Select(level => level.ServiceLevelCode)
            .ToListAsync(cancellationToken);
        var adapter = _carrierRegistry.GetAdapter(account.CarrierCode);
        var supportedServiceLevels = adapter?.GetSupportedServiceLevels()
            .Select(level => level.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (enabledServiceLevels.Any(level => !supportedServiceLevels.Contains(level)))
        {
            blockers.Add("PROVIDER_CAPABILITY_UNSUPPORTED");
        }

        return blockers.Distinct(StringComparer.Ordinal).ToList();
    }

    private static void ApplyOperationalState(CarrierAccount account)
    {
        switch (account.State)
        {
            case CarrierAccountState.Active:
                account.OperationalState = CarrierOperationalState.Ready;
                account.ActivatedAtUtc = DateTime.UtcNow;
                break;
            case CarrierAccountState.Suspended:
                account.OperationalState = CarrierOperationalState.Suspended;
                account.SuspendedAtUtc = DateTime.UtcNow;
                break;
            case CarrierAccountState.Disabled:
                account.OperationalState = CarrierOperationalState.Disabled;
                account.DisabledAtUtc = DateTime.UtcNow;
                break;
            case CarrierAccountState.Retired:
                account.OperationalState = CarrierOperationalState.Retired;
                account.RetiredAtUtc = DateTime.UtcNow;
                break;
            default:
                account.OperationalState = CarrierOperationalState.NotReady;
                break;
        }
    }

    private static CarrierAdministrationAuditAction AuditActionFor(CarrierAccountState state) =>
        state switch
        {
            CarrierAccountState.Active => CarrierAdministrationAuditAction.Activation,
            CarrierAccountState.Suspended => CarrierAdministrationAuditAction.Suspension,
            CarrierAccountState.Disabled => CarrierAdministrationAuditAction.Disable,
            CarrierAccountState.Retired => CarrierAdministrationAuditAction.Retirement,
            _ => CarrierAdministrationAuditAction.Registration
        };

    private void AddAudit(
        CarrierAccount account,
        CarrierAdministrationAuditAction action,
        CarrierAdministrationActor actor,
        string correlationId,
        string? previousState,
        string? nextState,
        string? reason)
    {
        _db.Set<CarrierAdministrationAudit>().Add(new CarrierAdministrationAudit
        {
            Id = Guid.NewGuid(),
            TenantId = account.TenantId,
            CarrierAccountId = account.Id,
            Action = action,
            ActorId = actor.ActorId,
            CorrelationId = correlationId,
            PreviousState = previousState,
            NextState = nextState,
            Reason = reason,
            OccurredAtUtc = DateTime.UtcNow
        });
    }
}
