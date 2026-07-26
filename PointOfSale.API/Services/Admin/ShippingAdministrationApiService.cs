using Microsoft.EntityFrameworkCore;
using PointOfSale.API.DTOs.ShippingAdministration;
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

    public ShippingAdministrationApiService(
        ApplicationDbContext db,
        ICarrierAccountAdministrationRuntime runtime,
        IShippingCarrierRegistry carrierRegistry)
    {
        _db = db;
        _runtime = runtime;
        _carrierRegistry = carrierRegistry;
    }

    public Task<IReadOnlyList<SupportedShippingCarrierDto>> ListSupportedCarriersAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SupportedShippingCarrierDto> carriers = _carrierRegistry.GetAllAdapters()
            .OrderBy(adapter => adapter.CarrierCode)
            .Select(adapter => new SupportedShippingCarrierDto(
                adapter.CarrierCode,
                adapter.DisplayName,
                adapter.GetSupportedServiceLevels()
                    .Select(level => new ShippingAdministrationServiceLevelDto(
                        level.Code,
                        level.Name,
                        level.Description,
                        level.TypicalDaysMin,
                        level.TypicalDaysMax))
                    .ToList()))
            .ToList();

        return Task.FromResult(carriers);
    }

    public async Task<IReadOnlyList<CarrierAccountAdministrationDto>> ListCarrierAccountsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var accounts = await _db.CarrierAccounts
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(account => account.TenantId == tenantId && !account.IsDeleted)
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
        if (string.IsNullOrWhiteSpace(command.ApiKey))
        {
            return Failure("UNKNOWN_CREDENTIAL_REFERENCE", carrierAccountId);
        }

        var credentials = new CarrierConnectionTestCredentials(
            command.ApiKey,
            command.ApiSecret,
            command.AccountId,
            command.ApiUrl,
            command.UseSandbox,
            command.ExtraConfig);

        return (await _runtime.RunConnectionTestAsync(tenantId, carrierAccountId, credentials, actor, correlationId, cancellationToken)).ToDto();
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

        return new CarrierAccountAdministrationDto(
            account.Id,
            account.CarrierCode,
            account.Environment?.ToString(),
            account.ActiveCredentialReferenceId,
            account.State.ToString(),
            account.OperationalState.ToString(),
            account.HealthState.ToString(),
            account.HealthReason,
            account.LastHealthEvaluatedAtUtc,
            account.ActivatedAtUtc,
            account.SuspendedAtUtc,
            account.DisabledAtUtc,
            account.RetiredAtUtc,
            credentials,
            assignments,
            policies,
            serviceLevels,
            lastConnectionTest);
    }

    private static CarrierReadinessEvidence ToReadiness(ReadinessEvidenceCommand command) =>
        new(command.CarrierMappingPublished, command.ProviderCapabilitySupported);

    private static ShippingAdministrationResultDto Failure(string failureCode, Guid? carrierAccountId = null) =>
        new(false, carrierAccountId, null, null, failureCode, new[] { failureCode });
}
