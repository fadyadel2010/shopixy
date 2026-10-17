using System.Net.Http.Headers;
using Moq;
using PointOfSale.Infrastructure.Shipping;
using PointOfSale.Infrastructure.Shipping.Adapters;

namespace PointOfSale.Tests.Shipping;

public sealed class FedExAccountValidationBoundaryTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("missing-header", false)]
    [InlineData("missing-token", false)]
    [InlineData("whitespace-token", false)]
    [InlineData("wrong-scheme", false)]
    [InlineData("missing-expiry", false)]
    [InlineData("expired", false)]
    [InlineData("refused", false)]
    public async Task AccountValidation_RequiresUsableProjectToken_WithoutGrantingExecutionAuthority(string shape, bool expected)
    {
        var oauth = new Mock<IFedExOAuthClient>(MockBehavior.Strict);
        var credentials = new CarrierCredentials { ApiKey = "synthetic-project", ApiSecret = "synthetic-secret", AccountId = "synthetic-account", UseSandbox = true };
        var header = shape == "missing-header" ? null : new AuthenticationHeaderValue(shape == "wrong-scheme" ? "Basic" : "Bearer",
            shape == "missing-token" ? null : shape == "whitespace-token" ? "synthetic token" : "synthetic-token-not-execution-evidence");
        DateTimeOffset? expiry = shape == "missing-expiry" ? null : DateTimeOffset.UtcNow.AddMinutes(shape == "expired" ? -1 : 5);
        oauth.Setup(c => c.GetAuthorizationHeaderAsync(It.IsAny<FedExOAuthRequest>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FedExAuthorizationHeaderResult(shape != "refused", header, expiry, false, null, null));
        var adapter = new FedExAdapter(oauth.Object, new FedExOptions());
        var result = await adapter.ValidateCredentialsAsync(credentials);
        Assert.Equal(expected, result.Success);
        if (expected) Assert.Contains("qualification separate", result.AccountName);
        else { Assert.Equal("FEDEX_OAUTH_AUTHORIZATION_FAILED", result.ErrorCode); Assert.Null(result.AccountName); }
        Assert.DoesNotContain("synthetic-secret", result.ErrorMessage ?? "");
        Assert.DoesNotContain("synthetic-token", result.ErrorMessage ?? "");
        Assert.Equal("Unavailable", CarrierOperationSupports.Describe("fedex").Single(o => o.Operation == "quote").Implementation);
        oauth.Verify(c => c.GetAuthorizationHeaderAsync(It.IsAny<FedExOAuthRequest>(), true, It.IsAny<CancellationToken>()), Times.Once);
        oauth.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancelledValidation_DoesNotContactOAuthOrTurnCancellationIntoCredentialFailure()
    {
        var oauth = new Mock<IFedExOAuthClient>(MockBehavior.Strict);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var adapter = new FedExAdapter(oauth.Object, new FedExOptions());
        await Assert.ThrowsAsync<OperationCanceledException>(() => adapter.ValidateCredentialsAsync(new CarrierCredentials { ApiKey = "" }, cancelled.Token));
        oauth.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationDuringOAuth_IsNeverAdmittedAsSuccessfulOrFailedCredentialValidation(bool oauthSuccess)
    {
        using var cancellation = new CancellationTokenSource();
        var oauth = new Mock<IFedExOAuthClient>(MockBehavior.Strict);
        var result = new FedExAuthorizationHeaderResult(oauthSuccess,
            new AuthenticationHeaderValue("Bearer", "synthetic-token"), DateTimeOffset.UtcNow.AddMinutes(5), false, null, null);
        oauth.Setup(client => client.GetAuthorizationHeaderAsync(It.IsAny<FedExOAuthRequest>(), true, cancellation.Token))
            .Returns(() => { cancellation.Cancel(); return Task.FromResult(result); });
        var adapter = new FedExAdapter(oauth.Object, new FedExOptions());
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => adapter.ValidateCredentialsAsync(
            new CarrierCredentials { ApiKey = "synthetic-project", ApiSecret = "synthetic-secret", AccountId = "synthetic-account", UseSandbox = true }, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        oauth.Verify(client => client.GetAuthorizationHeaderAsync(It.IsAny<FedExOAuthRequest>(), true, cancellation.Token), Times.Once);
        oauth.VerifyNoOtherCalls();
    }
}
