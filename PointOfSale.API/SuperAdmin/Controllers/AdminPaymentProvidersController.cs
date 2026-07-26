using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.API.Services.Admin;
using PointOfSale.API.SuperAdmin.Services;
using PointOfSale.Core.Payments.Administration;

namespace PointOfSale.API.SuperAdmin.Controllers;

[Route("api/admin/payment-providers")]
public sealed class AdminPaymentProvidersController : SuperAdminBaseController
{
    private readonly IPaymentProviderAdministrationService _paymentProviderAdministrationService;
    private readonly IPaymentProviderDashboardService _paymentProviderDashboardService;
    private readonly IPaymentProviderActionService _paymentProviderActionService;
    private readonly IPaymentProviderSecretService _paymentProviderSecretService;
    private readonly ISuperAdminAuditService _auditService;

    public AdminPaymentProvidersController(
        IPaymentProviderAdministrationService paymentProviderAdministrationService,
        IPaymentProviderDashboardService paymentProviderDashboardService,
        IPaymentProviderActionService paymentProviderActionService,
        IPaymentProviderSecretService paymentProviderSecretService,
        ISuperAdminAuditService auditService)
    {
        _paymentProviderAdministrationService = paymentProviderAdministrationService;
        _paymentProviderDashboardService = paymentProviderDashboardService;
        _paymentProviderActionService = paymentProviderActionService;
        _paymentProviderSecretService = paymentProviderSecretService;
        _auditService = auditService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PaymentProviderListResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaymentProviders(CancellationToken cancellationToken = default)
    {
        var providers = _paymentProviderAdministrationService.GetProviders();

        await _auditService.LogActionAsync(
            action: "PAYMENT_PROVIDERS_VIEWED",
            entityType: "PaymentProviderConfiguration",
            entityId: "payment-providers",
            description: "Super Admin viewed CommerceHub payment providers.",
            success: true,
            details: new { Count = providers.Count },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(new PaymentProviderListResponse
        {
            Providers = providers,
            Permissions = PaymentProviderPermissions.All
        });
    }

    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(PaymentProviderDashboardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPaymentProviderDashboard(CancellationToken cancellationToken = default)
    {
        var dashboard = await _paymentProviderDashboardService.GetDashboardAsync(cancellationToken);

        await _auditService.LogActionAsync(
            action: "PAYMENT_PROVIDER_DASHBOARD_VIEWED",
            entityType: "PaymentProviderDashboard",
            entityId: "payment-providers-dashboard",
            description: "Super Admin viewed the CommerceHub payment provider dashboard.",
            success: true,
            details: new
            {
                ProviderCount = dashboard.Summary.ProviderCount,
                CapabilityCount = dashboard.Summary.CapabilityCount,
                SecretMetadataCount = dashboard.Summary.ConfiguredSecretCount,
                Permission = PaymentProviderPermissions.View,
                SecretsRedacted = true,
                RawAuditPayloadsReturned = false,
                ProviderPayloadsReturned = false
            },
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(dashboard);
    }

    [HttpGet("{providerCode}")]
    [ProducesResponseType(typeof(PaymentProviderConfigurationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPaymentProvider(
        string providerCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var provider = _paymentProviderAdministrationService.GetProviderConfiguration(providerCode);

            await _auditService.LogActionAsync(
                action: "PAYMENT_PROVIDER_CONFIGURATION_VIEWED",
                entityType: "PaymentProviderConfiguration",
                entityId: provider.ProviderCode,
                description: "Super Admin viewed a CommerceHub payment provider configuration.",
                success: true,
                details: new { provider.ProviderCode, provider.Enabled, provider.Environment, provider.Status },
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return OkResponse(new PaymentProviderConfigurationResponse
            {
                Provider = provider,
                Permissions = PaymentProviderPermissions.All
            });
        }
        catch (PaymentProviderAdministrationException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpPut("{providerCode}")]
    [ProducesResponseType(typeof(PaymentProviderConfigurationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SavePaymentProvider(
        string providerCode,
        [FromBody] PaymentProviderConfigurationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var saveResult = _paymentProviderAdministrationService.SaveProviderConfiguration(providerCode, request);
            var provider = saveResult.Provider;

            await _auditService.LogActionAsync(
                action: "PAYMENT_PROVIDER_CONFIGURATION_SAVED",
                entityType: "PaymentProviderConfiguration",
                entityId: provider.ProviderCode,
                description: "Super Admin saved a CommerceHub payment provider configuration.",
                success: true,
                details: new
                {
                    provider.ProviderCode,
                    provider.Enabled,
                    provider.Environment,
                    provider.Status,
                    ChangedFields = saveResult.RedactedChangedFields,
                    MetadataKeys = provider.Metadata.Keys.ToArray(),
                    SecretsRedacted = true
                },
                reference: provider.ProviderCode,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return OkResponse(new PaymentProviderConfigurationResponse
            {
                Provider = provider,
                Permissions = PaymentProviderPermissions.All
            });
        }
        catch (PaymentProviderAdministrationException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpPost("{providerCode}/actions/test-connection")]
    [ProducesResponseType(typeof(PaymentProviderActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> TestPaymentProviderConnection(
        string providerCode,
        CancellationToken cancellationToken = default)
    {
        var execution = await _paymentProviderActionService.TestConnectionAsync(providerCode, cancellationToken);
        var result = execution.Result;

        await _auditService.LogActionAsync(
            action: "PAYMENT_PROVIDER_CONNECTION_TESTED",
            entityType: "PaymentProviderDiagnostic",
            entityId: string.IsNullOrWhiteSpace(result.Provider) ? providerCode : result.Provider,
            description: "Super Admin ran a payment provider connection diagnostic.",
            success: result.Success,
            details: new
            {
                result.Provider,
                result.Environment,
                result.Status,
                result.ErrorCategory,
                DurationMs = result.LatencyMs,
                CheckedAtUtc = result.CheckedAtUtc,
                Permission = PaymentProviderPermissions.TestConnection,
                SecretsRedacted = true,
                ProviderPayloadPersisted = false
            },
            reference: string.IsNullOrWhiteSpace(result.Provider) ? providerCode : result.Provider,
            actorId: CurrentSuperAdminId,
            actorEmail: CurrentSuperAdminEmail,
            cancellationToken: cancellationToken);

        return OkResponse(new PaymentProviderActionResponse
        {
            Result = result,
            Health = execution.Health,
            Permissions = PaymentProviderPermissions.All
        });
    }

    [HttpGet("{providerCode}/secrets")]
    [ProducesResponseType(typeof(PaymentProviderSecretLifecycleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPaymentProviderSecrets(
        string providerCode,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var lifecycle = _paymentProviderSecretService.GetSecretMetadata(providerCode);

            await _auditService.LogActionAsync(
                action: "PAYMENT_PROVIDER_SECRET_METADATA_VIEWED",
                entityType: "PaymentProviderSecret",
                entityId: lifecycle.ProviderCode,
                description: "Super Admin viewed payment provider secret lifecycle metadata.",
                success: true,
                details: new
                {
                    lifecycle.ProviderCode,
                    SecretCount = lifecycle.Secrets.Count,
                    Permission = PaymentProviderPermissions.ViewSecretMetadata,
                    SecretsRedacted = true
                },
                reference: lifecycle.ProviderCode,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return OkResponse(lifecycle);
        }
        catch (PaymentProviderAdministrationException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }

    [HttpPost("{providerCode}/secrets/rotate")]
    [ProducesResponseType(typeof(RotatePaymentProviderSecretResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RotatePaymentProviderSecret(
        string providerCode,
        [FromBody] RotatePaymentProviderSecretRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rotation = _paymentProviderSecretService.RotateSecret(
                providerCode,
                request,
                CurrentSuperAdminId,
                CurrentSuperAdminEmail);

            await _auditService.LogActionAsync(
                action: "PAYMENT_PROVIDER_SECRET_ROTATED",
                entityType: "PaymentProviderSecret",
                entityId: rotation.Secret.SecretId,
                description: "Super Admin rotated a payment provider secret.",
                success: true,
                details: new
                {
                    rotation.ProviderCode,
                    rotation.Secret.SecretType,
                    rotation.Secret.Version,
                    rotation.Secret.Fingerprint,
                    Reason = request.Reason,
                    Permission = PaymentProviderPermissions.RotateSecrets,
                    SecretsRedacted = true,
                    SecretValuePersistedToAudit = false,
                    EncryptedSecretPersistedToAudit = false
                },
                reference: rotation.Secret.SecretId,
                actorId: CurrentSuperAdminId,
                actorEmail: CurrentSuperAdminEmail,
                cancellationToken: cancellationToken);

            return OkResponse(rotation);
        }
        catch (PaymentProviderAdministrationException exception)
        {
            return ErrorResponse(exception.StatusCode, exception.Code, exception.Message);
        }
    }
}
