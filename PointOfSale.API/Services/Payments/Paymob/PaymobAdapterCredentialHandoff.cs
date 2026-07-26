using PointOfSale.Core.Payments.Runtime.Domain;

namespace PointOfSale.API.Services.Payments.Paymob;

/// <summary>
/// Carries one validated tenant credential set from the application boundary to one adapter invocation.
/// It performs no configuration lookup and never retains credentials after adapter admission.
/// </summary>
public interface IPaymobAdapterCredentialHandoff
{
    IDisposable Begin(PaymobTenantRuntimeCredentials credentials);

    PaymobTenantRuntimeCredentials ConsumeFor(PaymentAction action);
}

public sealed class PaymobAdapterCredentialHandoff : IPaymobAdapterCredentialHandoff
{
    private readonly object _gate = new();
    private ActiveHandoff? _active;

    public IDisposable Begin(PaymobTenantRuntimeCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        ValidateCredentialShape(credentials);

        lock (_gate)
        {
            if (_active is not null)
            {
                throw new InvalidOperationException(
                    "A tenant Paymob adapter credential handoff is already active in this application scope.");
            }

            var active = new ActiveHandoff(new object(), credentials);
            _active = active;
            return new HandoffLease(this, active.Token);
        }
    }

    public PaymobTenantRuntimeCredentials ConsumeFor(PaymentAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        ActiveHandoff active;
        lock (_gate)
        {
            active = _active
                ?? throw new InvalidOperationException(
                    "Validated tenant Paymob credentials were not supplied by the application boundary.");

            // A handoff is single-use and is cleared before any provider I/O can begin.
            _active = null;
        }

        var actionTenantId = ResolveActionTenantId(action);
        if (active.Credentials.TenantId != actionTenantId)
        {
            throw new InvalidOperationException(
                "The supplied tenant Paymob credentials do not belong to the canonical payment action tenant.");
        }

        return active.Credentials;
    }

    private void Release(object token)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_active?.Token, token))
            {
                _active = null;
            }
        }
    }

    private static Guid ResolveActionTenantId(PaymentAction action)
    {
        var values = action.Metadata.Values;
        var rawTenantId = values.TryGetValue("tenantId", out var camelCase)
            ? camelCase
            : values.TryGetValue("tenant_id", out var snakeCase)
                ? snakeCase
                : null;

        if (!Guid.TryParse(rawTenantId, out var tenantId) || tenantId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "The canonical payment action does not contain a valid tenant identity for Paymob credential handoff.");
        }

        return tenantId;
    }

    private static void ValidateCredentialShape(PaymobTenantRuntimeCredentials credentials)
    {
        if (credentials.TenantId == Guid.Empty ||
            credentials.TenantPaymentMethodId == Guid.Empty ||
            string.IsNullOrWhiteSpace(credentials.ApiKey) ||
            string.IsNullOrWhiteSpace(credentials.IntegrationId) ||
            string.IsNullOrWhiteSpace(credentials.IframeId) ||
            string.IsNullOrWhiteSpace(credentials.HmacSecret))
        {
            throw new InvalidOperationException(
                "The tenant Paymob credential handoff is incomplete and cannot authorize adapter execution.");
        }
    }

    private sealed record ActiveHandoff(object Token, PaymobTenantRuntimeCredentials Credentials);

    private sealed class HandoffLease : IDisposable
    {
        private PaymobAdapterCredentialHandoff? _owner;
        private readonly object _token;

        public HandoffLease(PaymobAdapterCredentialHandoff owner, object token)
        {
            _owner = owner;
            _token = token;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.Release(_token);
        }
    }
}
