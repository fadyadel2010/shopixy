using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Interfaces;

namespace PointOfSale.API.Security;

public sealed class ManualWalletUploadTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly ITenantContext _tenantContext;
    private readonly ICheckoutSessionTokenService _checkoutSessionTokenService;

    public ManualWalletUploadTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ITenantContext tenantContext,
        ICheckoutSessionTokenService checkoutSessionTokenService)
        : base(options, logger, encoder)
    {
        _tenantContext = tenantContext;
        _checkoutSessionTokenService = checkoutSessionTokenService;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ManualWalletUploadTokenAuthenticationDefaults.HeaderName, out var tokenValues))
        {
            return Task.FromResult(AuthenticateResult.Fail("Manual wallet upload token is required."));
        }

        var token = tokenValues.ToString().Trim();
        var tenantId = _tenantContext.TenantId;
        if (tenantId == null || tenantId == Guid.Empty)
        {
            return Task.FromResult(AuthenticateResult.Fail("Tenant context is required."));
        }

        var validation = _checkoutSessionTokenService.ValidateManualWalletUploadToken(token, tenantId.Value);
        if (!validation.IsValid || validation.IsExpired || !Guid.TryParseExact(validation.Subject, "N", out var checkoutSessionId))
        {
            return Task.FromResult(AuthenticateResult.Fail("Manual wallet upload token is invalid or expired."));
        }

        var claims = new List<Claim>
        {
            new(ManualWalletUploadTokenAuthenticationDefaults.CheckoutSessionIdClaimType, checkoutSessionId.ToString("D")),
            new(ManualWalletUploadTokenAuthenticationDefaults.TenantIdClaimType, tenantId.Value.ToString("D"))
        };

        if (!string.IsNullOrWhiteSpace(validation.Nonce))
        {
            claims.Add(new Claim(
                ManualWalletUploadTokenAuthenticationDefaults.RecoveryNonceClaimType,
                validation.Nonce));
        }

        var identity = new ClaimsIdentity(claims, ManualWalletUploadTokenAuthenticationDefaults.SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ManualWalletUploadTokenAuthenticationDefaults.SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
