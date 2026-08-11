using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.Controllers;
using PointOfSale.API.Instrumentation;
using PointOfSale.API.Middleware;
using PointOfSale.API.Services.Storefront;
using PointOfSale.Core.Certificates;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Events;
using PointOfSale.Core.Events.Domain;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Models;
using PointOfSale.Infrastructure.Certificates;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Data.Repositories;
using PointOfSale.Tests.Fakes;

namespace PointOfSale.Tests.CustomDomains;

public sealed class AdminDomainsHttpIntegrationTests
{
    [Fact]
    public async Task CertificateCommands_ThroughRealHttp_ProvisionActivateRenewRefreshReadOperationsAndDoNotLeakSecrets()
    {
        await using var app = await AdminDomainHttpFixture.StartAsync();
        var domain = await app.SeedDomainAsync(app.TenantA, "ssl-admin.example.com", DomainLifecycleState.Verified);
        using var client = app.CreateClient(app.TenantA);

        var provisionResponse = await client.PostAsJsonAsync(
            $"/api/admin/storefront/domains/{domain.Id}/ssl/provision",
            new { idempotencyKey = "provision-http" });

        Assert.Equal(HttpStatusCode.Accepted, provisionResponse.StatusCode);
        var provisionBody = await provisionResponse.Content.ReadAsStringAsync();
        AssertNoCertificateSecrets(provisionBody);
        using var provisionJson = JsonDocument.Parse(provisionBody);
        var certificateId = provisionJson.RootElement.GetProperty("certificateId").GetGuid();
        var provisionOperationId = provisionJson.RootElement.GetProperty("operationId").GetGuid();
        AssertCertificateStatus(provisionJson.RootElement.GetProperty("certificateStatus"), CertificateStatus.Issued);

        var activateResponse = await client.PostAsJsonAsync(
            $"/api/admin/storefront/domains/{domain.Id}/ssl/activate",
            new { certificateId, idempotencyKey = "activate-http" });

        Assert.Equal(HttpStatusCode.Accepted, activateResponse.StatusCode);
        using var activateJson = await ReadJsonAsync(activateResponse);
        AssertCertificateStatus(activateJson.RootElement.GetProperty("certificateStatus"), CertificateStatus.Active);

        var certificateResponse = await client.GetAsync($"/api/admin/storefront/domains/{domain.Id}/ssl/certificate");
        Assert.Equal(HttpStatusCode.OK, certificateResponse.StatusCode);
        var certificateBody = await certificateResponse.Content.ReadAsStringAsync();
        AssertNoCertificateSecrets(certificateBody);
        using (var certificateJson = JsonDocument.Parse(certificateBody))
        {
            Assert.Equal(certificateId, certificateJson.RootElement.GetProperty("certificateId").GetGuid());
            AssertCertificateStatus(certificateJson.RootElement.GetProperty("status"), CertificateStatus.Active);
        }

        var operationsResponse = await client.GetAsync($"/api/admin/storefront/domains/{domain.Id}/ssl/operations?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, operationsResponse.StatusCode);
        using (var operationsJson = await ReadJsonAsync(operationsResponse))
        {
            Assert.True(operationsJson.RootElement.GetProperty("totalCount").GetInt32() >= 2);
            AssertNoCertificateSecrets(operationsJson.RootElement.GetRawText());
        }

        var operationResponse = await client.GetAsync($"/api/admin/storefront/domains/{domain.Id}/ssl/operations/{provisionOperationId}");
        Assert.Equal(HttpStatusCode.OK, operationResponse.StatusCode);
        using (var operationJson = await ReadJsonAsync(operationResponse))
        {
            Assert.Equal(provisionOperationId, operationJson.RootElement.GetProperty("operationId").GetGuid());
        }

        var renewResponse = await client.PostAsJsonAsync(
            $"/api/admin/storefront/domains/{domain.Id}/ssl/renew",
            new { certificateId, idempotencyKey = "renew-http" });
        Assert.Equal(HttpStatusCode.Accepted, renewResponse.StatusCode);
        using var renewJson = await ReadJsonAsync(renewResponse);
        Assert.Equal("Renewed", renewJson.RootElement.GetProperty("outcome").GetString());
        var renewedCertificateId = renewJson.RootElement.GetProperty("certificateId").GetGuid();

        var refreshResponse = await client.PostAsJsonAsync(
            $"/api/admin/storefront/domains/{domain.Id}/ssl/refresh",
            new { certificateId = renewedCertificateId, idempotencyKey = "refresh-http" });
        Assert.Equal(HttpStatusCode.Accepted, refreshResponse.StatusCode);
        using var refreshJson = await ReadJsonAsync(refreshResponse);
        Assert.Equal("Refreshed", refreshJson.RootElement.GetProperty("outcome").GetString());

        var reloaded = await app.FindDomainAsync(domain.Id);
        Assert.Equal(DomainLifecycleState.SslActive, reloaded!.LifecycleState);
        Assert.Equal(1, await app.CountAuditsAsync(domain.Id, "admin_certificate_renew_requested"));
        Assert.Equal(1, await app.CountAuditsAsync(domain.Id, "admin_certificate_refresh_requested"));
    }

    [Fact]
    public async Task CertificateRevokeAndRetry_ThroughRealHttp_UseServiceFencingAndIdempotency()
    {
        await using var app = await AdminDomainHttpFixture.StartAsync();
        using var client = app.CreateClient(app.TenantA);

        var revokeDomain = await app.SeedDomainAsync(app.TenantA, "revoke-http.example.com", DomainLifecycleState.Verified);
        var issuedCertificate = await app.SeedCertificateAsync(app.TenantA, revokeDomain.Id, revokeDomain.NormalizedHostName, CertificateStatus.Issued);

        var revokeResponse = await client.PostAsJsonAsync(
            $"/api/admin/storefront/domains/{revokeDomain.Id}/ssl/revoke",
            new { certificateId = issuedCertificate.Id, reason = CertificateRevocationReasonCodes.AdminRequested, idempotencyKey = "revoke-http" });

        Assert.Equal(HttpStatusCode.Accepted, revokeResponse.StatusCode);
        using (var revokeJson = await ReadJsonAsync(revokeResponse))
        {
            Assert.Equal("Revoked", revokeJson.RootElement.GetProperty("outcome").GetString());
            AssertCertificateStatus(revokeJson.RootElement.GetProperty("certificateStatus"), CertificateStatus.Revoked);
        }

        var retryDomain = await app.SeedDomainAsync(app.TenantA, "retry-http.example.com", DomainLifecycleState.Verified);
        var retryCertificate = await app.SeedCertificateAsync(app.TenantA, retryDomain.Id, retryDomain.NormalizedHostName, CertificateStatus.Failed);
        var failedOperation = await app.SeedCertificateOperationAsync(
            app.TenantA,
            retryDomain.Id,
            retryCertificate.Id,
            CertificateOperationType.Provisioning,
            "retry-original",
            CertificateOperationStatus.Failed,
            CertificateOperationPhase.RetryBackoff,
            retryable: true,
            retryAfterUtc: DateTime.UtcNow.AddMinutes(-1));

        var retryResponse = await client.PostAsJsonAsync(
            $"/api/admin/storefront/domains/{retryDomain.Id}/ssl/retry",
            new { operationId = failedOperation.Id, idempotencyKey = "retry-command" });

        Assert.Equal(HttpStatusCode.Accepted, retryResponse.StatusCode);
        using var retryJson = await ReadJsonAsync(retryResponse);
        Assert.Equal(failedOperation.Id, retryJson.RootElement.GetProperty("operationId").GetGuid());
        AssertCertificateStatus(retryJson.RootElement.GetProperty("certificateStatus"), CertificateStatus.Issued);
        Assert.Equal(1, await app.CountAuditsAsync(retryDomain.Id, "admin_certificate_retry_requested"));
    }

    [Fact]
    public async Task CertificateCommands_ThroughRealHttp_FailClosedForAuthTenantLifecycleConflictRateLimitAndSecrets()
    {
        await using var app = await AdminDomainHttpFixture.StartAsync();
        var tenantBDomain = await app.SeedDomainAsync(app.TenantB, "tenant-b-cert-secret.example.com", DomainLifecycleState.Verified);
        var tenantBCertificate = await app.SeedCertificateAsync(app.TenantB, tenantBDomain.Id, tenantBDomain.NormalizedHostName, CertificateStatus.Active, isAuthoritative: true);

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.PostAsJsonAsync(
            $"/api/admin/storefront/domains/{tenantBDomain.Id}/ssl/refresh",
            new { certificateId = tenantBCertificate.Id, idempotencyKey = "anon" });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var viewer = app.CreateClient(app.TenantB, role: "Viewer");
        var viewerResponse = await viewer.PostAsJsonAsync(
            $"/api/admin/storefront/domains/{tenantBDomain.Id}/ssl/refresh",
            new { certificateId = tenantBCertificate.Id, idempotencyKey = "viewer" });
        Assert.Equal(HttpStatusCode.Forbidden, viewerResponse.StatusCode);

        using var tenantAClient = app.CreateClient(app.TenantA);
        var routes = new[]
        {
            HttpRequestMessageFactory(HttpMethod.Get, $"/api/admin/storefront/domains/{tenantBDomain.Id}/ssl/certificate"),
            HttpRequestMessageFactory(HttpMethod.Get, $"/api/admin/storefront/domains/{tenantBDomain.Id}/ssl/operations"),
            HttpRequestMessageFactory(HttpMethod.Get, $"/api/admin/storefront/domains/{tenantBDomain.Id}/ssl/operations/{Guid.NewGuid()}"),
            HttpRequestMessageFactory(HttpMethod.Post, $"/api/admin/storefront/domains/{tenantBDomain.Id}/ssl/refresh"),
            HttpRequestMessageFactory(HttpMethod.Post, $"/api/admin/storefront/domains/{tenantBDomain.Id}/ssl/revoke")
        };

        foreach (var request in routes)
        {
            using var response = await tenantAClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest);
            Assert.DoesNotContain("tenant-b-cert-secret.example.com", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(tenantBCertificate.SerialNumber!, body, StringComparison.OrdinalIgnoreCase);
            AssertNoCertificateSecrets(body);
        }

        var unverified = await app.SeedDomainAsync(app.TenantA, "unverified-cert.example.com", DomainLifecycleState.PendingVerification);
        var invalidLifecycle = await tenantAClient.PostAsJsonAsync(
            $"/api/admin/storefront/domains/{unverified.Id}/ssl/provision",
            new { idempotencyKey = "invalid-lifecycle" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidLifecycle.StatusCode);

        var conflictDomain = await app.SeedDomainAsync(app.TenantA, "conflict-cert.example.com", DomainLifecycleState.Verified);
        await app.SeedCertificateOperationAsync(
            app.TenantA,
            conflictDomain.Id,
            null,
            CertificateOperationType.Provisioning,
            "existing-conflict",
            CertificateOperationStatus.Running,
            CertificateOperationPhase.ProviderRequest,
            retryable: false,
            retryAfterUtc: null);
        var conflictResponse = await tenantAClient.PostAsJsonAsync(
            $"/api/admin/storefront/domains/{conflictDomain.Id}/ssl/provision",
            new { idempotencyKey = "new-conflict" });
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);

        var refreshDomain = await app.SeedDomainAsync(app.TenantA, "refresh-rate.example.com", DomainLifecycleState.SslActive);
        var refreshCertificate = await app.SeedCertificateAsync(app.TenantA, refreshDomain.Id, refreshDomain.NormalizedHostName, CertificateStatus.Active, isAuthoritative: true);
        app.Edge.RouteExists = false;
        var firstRefresh = await tenantAClient.PostAsJsonAsync(
            $"/api/admin/storefront/domains/{refreshDomain.Id}/ssl/refresh",
            new { certificateId = refreshCertificate.Id, idempotencyKey = "refresh-rate" });
        Assert.Equal(HttpStatusCode.Accepted, firstRefresh.StatusCode);
        var limitedRefresh = await tenantAClient.PostAsJsonAsync(
            $"/api/admin/storefront/domains/{refreshDomain.Id}/ssl/refresh",
            new { certificateId = refreshCertificate.Id, idempotencyKey = "refresh-rate" });
        Assert.Equal((HttpStatusCode)429, limitedRefresh.StatusCode);
        Assert.True(limitedRefresh.Headers.TryGetValues("Retry-After", out var retryAfterValues));
        Assert.True(int.TryParse(retryAfterValues.Single(), out var retryAfterSeconds));
        Assert.True(retryAfterSeconds > 0);
        AssertNoCertificateSecrets(await limitedRefresh.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CertificateActivation_ThroughRealHttp_RejectsConflictingIdempotencyPayloadAndPreservesPrimary()
    {
        await using var app = await AdminDomainHttpFixture.StartAsync();
        var domain = await app.SeedDomainAsync(
            app.TenantA,
            "primary-preserve.example.com",
            DomainLifecycleState.Primary,
            domainType: DomainType.Primary);
        var first = await app.SeedCertificateAsync(app.TenantA, domain.Id, domain.NormalizedHostName, CertificateStatus.Issued, generation: 1);
        var second = await app.SeedCertificateAsync(app.TenantA, domain.Id, domain.NormalizedHostName, CertificateStatus.Issued, generation: 2);
        using var client = app.CreateClient(app.TenantA);

        var firstResponse = await client.PostAsJsonAsync(
            $"/api/admin/storefront/domains/{domain.Id}/ssl/activate",
            new { certificateId = first.Id, idempotencyKey = "activate-conflict" });
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.Equal(DomainLifecycleState.Primary, (await app.FindDomainAsync(domain.Id))!.LifecycleState);

        var conflict = await client.PostAsJsonAsync(
            $"/api/admin/storefront/domains/{domain.Id}/ssl/activate",
            new { certificateId = second.Id, idempotencyKey = "activate-conflict" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var conflictBody = await conflict.Content.ReadAsStringAsync();
        AssertNoCertificateSecrets(conflictBody);
        Assert.Equal(DomainLifecycleState.Primary, (await app.FindDomainAsync(domain.Id))!.LifecycleState);
    }

    [Fact]
    public async Task StartVerification_ThroughRealHttp_RequiresAuthenticationAndRole()
    {
        await using var app = await AdminDomainHttpFixture.StartAsync();
        var domain = await app.SeedDomainAsync(app.TenantA, "auth-start.example.com", DomainLifecycleState.Draft);

        using var anonymous = app.CreateClient();
        var anonymousResponse = await anonymous.PutAsJsonAsync(
            $"/api/admin/storefront/domains/{domain.Id}/verification/start",
            new { verificationMethod = VerificationMethod.DnsTxt });

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

        using var viewer = app.CreateClient(app.TenantA, role: "Viewer");
        var viewerResponse = await viewer.PutAsJsonAsync(
            $"/api/admin/storefront/domains/{domain.Id}/verification/start",
            new { verificationMethod = VerificationMethod.DnsTxt });

        Assert.Equal(HttpStatusCode.Forbidden, viewerResponse.StatusCode);
    }

    [Fact]
    public async Task StartVerification_ThroughRealHttp_ReturnsMethodSpecificCnameInstructions()
    {
        await using var app = await AdminDomainHttpFixture.StartAsync();
        var domain = await app.SeedDomainAsync(app.TenantA, "cname-http.example.com", DomainLifecycleState.Draft);
        using var client = app.CreateClient(app.TenantA);

        var startResponse = await client.PutAsJsonAsync(
            $"/api/admin/storefront/domains/{domain.Id}/verification/start",
            new { verificationMethod = VerificationMethod.DnsCname, expectedVersion = domain.Version });

        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        using var startJson = await ReadJsonAsync(startResponse);
        var instructions = startJson.RootElement.GetProperty("instructions");
        var token = instructions.GetProperty("verificationToken").GetString();

        AssertVerificationMethod(instructions.GetProperty("method"), VerificationMethod.DnsCname);
        Assert.Equal("CNAME", instructions.GetProperty("dnsRecordType").GetString());
        Assert.Equal("_shopixy-verification.cname-http.example.com", instructions.GetProperty("dnsRecordName").GetString());
        Assert.Equal("_shopixy-verification", instructions.GetProperty("dnsRecordHost").GetString());
        Assert.False(string.IsNullOrWhiteSpace(token));

        var expectedTarget = $"verify-{token!.Replace("shopixy-domain-verify-", string.Empty, StringComparison.OrdinalIgnoreCase)}.domains.shopixy.com";
        Assert.Equal(expectedTarget, instructions.GetProperty("dnsRecordValue").GetString());

        var readResponse = await client.GetAsync($"/api/admin/storefront/domains/{domain.Id}/verification");
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        using var readJson = await ReadJsonAsync(readResponse);

        Assert.Equal("CNAME", readJson.RootElement.GetProperty("dnsRecordType").GetString());
        Assert.Equal(expectedTarget, readJson.RootElement.GetProperty("dnsRecordValue").GetString());
    }

    [Fact]
    public async Task VerificationCommands_ThroughRealHttp_MutateOnlyAuthorizedTenant()
    {
        await using var app = await AdminDomainHttpFixture.StartAsync();
        var domain = await app.SeedDomainAsync(app.TenantA, "verified-http.example.com", DomainLifecycleState.Draft);
        using var client = app.CreateClient(app.TenantA);

        var startResponse = await client.PutAsJsonAsync(
            $"/api/admin/storefront/domains/{domain.Id}/verification/start",
            new { verificationMethod = VerificationMethod.DnsTxt, expectedVersion = domain.Version });

        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        using var startJson = await ReadJsonAsync(startResponse);
        var token = startJson.RootElement.GetProperty("instructions").GetProperty("verificationToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        var persistedAfterStart = await app.FindDomainAsync(domain.Id);
        Assert.Equal(DomainLifecycleState.PendingVerification, persistedAfterStart!.LifecycleState);
        Assert.Equal(token, persistedAfterStart.VerificationToken);
        Assert.Equal(1, await app.CountAuditsAsync(domain.Id, "DomainVerificationStarted"));

        var verificationRead = await client.GetAsync($"/api/admin/storefront/domains/{domain.Id}/verification");
        Assert.Equal(HttpStatusCode.OK, verificationRead.StatusCode);
        using (var verificationJson = await ReadJsonAsync(verificationRead))
        {
            Assert.Equal("verified-http.example.com", verificationJson.RootElement.GetProperty("hostName").GetString());
            Assert.Equal("verified-http.example.com", verificationJson.RootElement.GetProperty("normalizedHostName").GetString());
            Assert.Equal("TXT", verificationJson.RootElement.GetProperty("dnsRecordType").GetString());
            Assert.Equal(token, verificationJson.RootElement.GetProperty("verificationToken").GetString());
            Assert.Equal(token, verificationJson.RootElement.GetProperty("dnsRecordValue").GetString());
        }

        app.Resolver.TxtResult = DnsLookupResult.CreateSuccess(token!, "fake-http", TimeSpan.FromMilliseconds(1), ttl: 60);
        var verifyResponse = await client.PutAsJsonAsync(
            $"/api/admin/storefront/domains/{domain.Id}/verification/verify",
            new { expectedVersion = persistedAfterStart.Version });

        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        using var verifyJson = await ReadJsonAsync(verifyResponse);
        Assert.Equal("VerifyNow", verifyJson.RootElement.GetProperty("operation").GetString());
        Assert.Equal("Verified", verifyJson.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(1, app.Resolver.TxtCalls);
        Assert.Equal(1, app.Events.PublishedEvents.OfType<DomainVerifiedEvent>().Count());
        Assert.Equal(1, await app.CountVerificationsAsync(domain.Id, VerificationStatus.Success));
        Assert.Equal(DomainLifecycleState.Verified, (await app.FindDomainAsync(domain.Id))!.LifecycleState);

        var retryVerifiedResponse = await client.PutAsJsonAsync(
            $"/api/admin/storefront/domains/{domain.Id}/verification/retry",
            new { expectedVersion = (await app.FindDomainAsync(domain.Id))!.Version });

        Assert.Equal(HttpStatusCode.OK, retryVerifiedResponse.StatusCode);
    }

    [Fact]
    public async Task EveryVerificationRoute_FailsClosedForCrossTenantAccess()
    {
        await using var app = await AdminDomainHttpFixture.StartAsync();
        var tenantBDomain = await app.SeedDomainAsync(
            app.TenantB,
            "tenant-b-secret.example.com",
            DomainLifecycleState.PendingVerification,
            token: "shopixy-domain-verify-tenant-b-secret");

        using var tenantAClient = app.CreateClient(app.TenantA);
        var routes = new[]
        {
            HttpRequestMessageFactory(HttpMethod.Put, $"/api/admin/storefront/domains/{tenantBDomain.Id}/verification/start"),
            HttpRequestMessageFactory(HttpMethod.Put, $"/api/admin/storefront/domains/{tenantBDomain.Id}/verification/verify"),
            HttpRequestMessageFactory(HttpMethod.Put, $"/api/admin/storefront/domains/{tenantBDomain.Id}/verification/retry"),
            HttpRequestMessageFactory(HttpMethod.Put, $"/api/admin/storefront/domains/{tenantBDomain.Id}/verification/token"),
            HttpRequestMessageFactory(HttpMethod.Get, $"/api/admin/storefront/domains/{tenantBDomain.Id}/verification")
        };

        foreach (var request in routes)
        {
            using var response = await tenantAClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain("tenant-b-secret.example.com", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("shopixy-domain-verify-tenant-b-secret", body, StringComparison.OrdinalIgnoreCase);
        }

        var unchanged = await app.FindDomainAsync(tenantBDomain.Id);
        Assert.Equal(DomainLifecycleState.PendingVerification, unchanged!.LifecycleState);
        Assert.Equal("shopixy-domain-verify-tenant-b-secret", unchanged.VerificationToken);
    }

    [Fact]
    public async Task VerifyNow_MapsExpectedFailureInfrastructureFailureAndRateLimit()
    {
        await using var app = await AdminDomainHttpFixture.StartAsync();
        using var client = app.CreateClient(app.TenantA);

        var mismatchDomain = await app.SeedDomainAsync(
            app.TenantA,
            "txt-mismatch.example.com",
            DomainLifecycleState.PendingVerification,
            token: "shopixy-domain-verify-expected");
        app.Resolver.TxtResult = DnsLookupResult.CreateSuccess("wrong-token", "fake-http", TimeSpan.Zero);

        var mismatchResponse = await client.PutAsJsonAsync(
            $"/api/admin/storefront/domains/{mismatchDomain.Id}/verification/verify",
            new { expectedVersion = mismatchDomain.Version });

        Assert.Equal((HttpStatusCode)422, mismatchResponse.StatusCode);
        using (var mismatchJson = await ReadJsonAsync(mismatchResponse))
        {
            Assert.Equal("VerificationFailed", mismatchJson.RootElement.GetProperty("outcome").GetString());
        }

        var timeoutDomain = await app.SeedDomainAsync(
            app.TenantA,
            "timeout.example.com",
            DomainLifecycleState.PendingVerification,
            token: "shopixy-domain-verify-timeout");
        app.Resolver.TxtResult = DnsLookupResult.CreateFailure("TIMEOUT", "fake-http", TimeSpan.Zero);

        var timeoutResponse = await client.PutAsJsonAsync(
            $"/api/admin/storefront/domains/{timeoutDomain.Id}/verification/verify",
            new { expectedVersion = timeoutDomain.Version });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, timeoutResponse.StatusCode);
        using (var timeoutJson = await ReadJsonAsync(timeoutResponse))
        {
            Assert.Equal("InfrastructureFailure", timeoutJson.RootElement.GetProperty("outcome").GetString());
        }

        var limitedDomain = await app.SeedDomainAsync(
            app.TenantA,
            "rate-limited.example.com",
            DomainLifecycleState.PendingVerification,
            token: "shopixy-domain-verify-rate");
        await app.SeedFailedVerificationAttemptsAsync(limitedDomain.Id, count: 5);
        var callsBeforeRateLimit = app.Resolver.TxtCalls;

        var limitedResponse = await client.PutAsJsonAsync(
            $"/api/admin/storefront/domains/{limitedDomain.Id}/verification/verify",
            new { expectedVersion = limitedDomain.Version });

        Assert.Equal((HttpStatusCode)429, limitedResponse.StatusCode);
        Assert.True(limitedResponse.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(callsBeforeRateLimit, app.Resolver.TxtCalls);
        Assert.Equal(1, await app.CountAuditsAsync(limitedDomain.Id, "DomainVerificationRateLimited"));
        using (var limitedJson = await ReadJsonAsync(limitedResponse))
        {
            Assert.True(limitedJson.RootElement.GetProperty("retryAfterUtc").GetDateTime() > DateTime.UtcNow);
        }
    }

    [Fact]
    public async Task RefreshVerificationToken_ThroughRealHttp_RateLimitsAndDoesNotLeakFromDetailDto()
    {
        await using var app = await AdminDomainHttpFixture.StartAsync();
        var domain = await app.SeedDomainAsync(
            app.TenantA,
            "refresh-http.example.com",
            DomainLifecycleState.PendingVerification,
            token: "shopixy-domain-verify-old-token");
        using var client = app.CreateClient(app.TenantA);

        var detailResponse = await client.GetAsync($"/api/admin/storefront/domains/{domain.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detailBody = await detailResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("verificationToken", detailBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shopixy-domain-verify-old-token", detailBody, StringComparison.OrdinalIgnoreCase);

        var refreshResponse = await client.PutAsJsonAsync(
            $"/api/admin/storefront/domains/{domain.Id}/verification/token",
            new { verificationMethod = VerificationMethod.DnsTxt, expectedVersion = domain.Version });

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshed = await app.FindDomainAsync(domain.Id);
        Assert.NotEqual("shopixy-domain-verify-old-token", refreshed!.VerificationToken);
        Assert.Equal(DomainLifecycleState.PendingVerification, refreshed.LifecycleState);
        Assert.Equal(0, await app.CountVerificationsAsync(domain.Id, VerificationStatus.Failed));
        Assert.Equal(1, await app.CountAuditsAsync(domain.Id, "DomainVerificationTokenRefreshed"));

        await app.SeedTokenRefreshAuditsAsync(domain.Id, app.TenantA, count: 5);
        var limitedResponse = await client.PutAsJsonAsync(
            $"/api/admin/storefront/domains/{domain.Id}/verification/token",
            new { verificationMethod = VerificationMethod.DnsTxt, expectedVersion = refreshed.Version });

        Assert.Equal((HttpStatusCode)429, limitedResponse.StatusCode);
        Assert.True(limitedResponse.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task ConcurrentVerifyNow_ProducesOneSuccessOneEventAndOneResolverInvocation()
    {
        await using var app = await AdminDomainHttpFixture.StartAsync();
        var domain = await app.SeedDomainAsync(
            app.TenantA,
            "concurrent-verify.example.com",
            DomainLifecycleState.PendingVerification,
            token: "shopixy-domain-verify-concurrent");
        app.Resolver.TxtResult = DnsLookupResult.CreateSuccess(
            "shopixy-domain-verify-concurrent",
            "fake-http",
            TimeSpan.FromMilliseconds(1));
        app.Resolver.Delay = TimeSpan.FromMilliseconds(50);

        using var client = app.CreateClient(app.TenantA);
        var responses = await Task.WhenAll(
            client.PutAsJsonAsync($"/api/admin/storefront/domains/{domain.Id}/verification/verify", new { expectedVersion = domain.Version }),
            client.PutAsJsonAsync($"/api/admin/storefront/domains/{domain.Id}/verification/verify", new { expectedVersion = domain.Version }));

        Assert.Equal(2, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, app.Resolver.TxtCalls);
        Assert.Equal(1, app.Events.PublishedEvents.OfType<DomainVerifiedEvent>().Count());
        Assert.Equal(1, await app.CountVerificationsAsync(domain.Id, VerificationStatus.Success));
        Assert.Equal(DomainLifecycleState.Verified, (await app.FindDomainAsync(domain.Id))!.LifecycleState);
    }

    [Fact]
    public async Task ConcurrentStartRequests_AreSerializedAndCreateOnePendingTransition()
    {
        await using var app = await AdminDomainHttpFixture.StartAsync();
        var domain = await app.SeedDomainAsync(app.TenantA, "concurrent-start.example.com", DomainLifecycleState.Draft);
        using var client = app.CreateClient(app.TenantA);

        var responses = await Task.WhenAll(
            client.PutAsJsonAsync($"/api/admin/storefront/domains/{domain.Id}/verification/start", new { verificationMethod = VerificationMethod.DnsTxt, expectedVersion = domain.Version }),
            client.PutAsJsonAsync($"/api/admin/storefront/domains/{domain.Id}/verification/start", new { verificationMethod = VerificationMethod.DnsTxt, expectedVersion = domain.Version }));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.BadRequest));
        Assert.Equal(DomainLifecycleState.PendingVerification, (await app.FindDomainAsync(domain.Id))!.LifecycleState);
        Assert.Equal(1, await app.CountAuditsAsync(domain.Id, "DomainVerificationStarted"));
    }

    private static HttpRequestMessage HttpRequestMessageFactory(HttpMethod method, string route) =>
        new(method, route)
        {
            Content = method == HttpMethod.Get ? null : JsonContent.Create(new { })
        };

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static void AssertVerificationMethod(JsonElement element, VerificationMethod expected)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            Assert.Equal(expected.ToString(), element.GetString());
            return;
        }

        Assert.Equal((int)expected, element.GetInt32());
    }

    private static void AssertCertificateStatus(JsonElement element, CertificateStatus expected)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            Assert.Equal(expected.ToString(), element.GetString());
            return;
        }

        Assert.Equal((int)expected, element.GetInt32());
    }

    private static void AssertNoCertificateSecrets(string body)
    {
        Assert.DoesNotContain("verificationToken", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private key", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BEGIN CERTIFICATE", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BEGIN PRIVATE KEY", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pfx", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("acme_account", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("caddy_credentials", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("challenge-token", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("provider-secret", body, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class AdminDomainHttpFixture : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly InMemoryDatabaseRoot _databaseRoot;
        private readonly string _databaseName;

        private AdminDomainHttpFixture(
            WebApplication app,
            Uri baseAddress,
            InMemoryDatabaseRoot databaseRoot,
            string databaseName,
            DeterministicDnsResolver resolver,
            DeterministicCaddyEdgeClient edge,
            DeterministicEdgeCertificateProbe probe,
            RecordingEventPublisher events)
        {
            _app = app;
            BaseAddress = baseAddress;
            _databaseRoot = databaseRoot;
            _databaseName = databaseName;
            Resolver = resolver;
            Edge = edge;
            Probe = probe;
            Events = events;
        }

        public Guid TenantA { get; } = Guid.NewGuid();
        public Guid TenantB { get; } = Guid.NewGuid();
        public Uri BaseAddress { get; }
        public DeterministicDnsResolver Resolver { get; }
        public DeterministicCaddyEdgeClient Edge { get; }
        public DeterministicEdgeCertificateProbe Probe { get; }
        public RecordingEventPublisher Events { get; }

        public static async Task<AdminDomainHttpFixture> StartAsync()
        {
            var databaseRoot = new InMemoryDatabaseRoot();
            var databaseName = $"AdminDomainsHttp_{Guid.NewGuid():N}";
            var resolver = new DeterministicDnsResolver();
            var edge = new DeterministicCaddyEdgeClient();
            var probe = new DeterministicEdgeCertificateProbe();
            var events = new RecordingEventPublisher();

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            });
            builder.WebHost.UseUrls($"http://127.0.0.1:{GetFreeTcpPort()}");
            builder.Logging.ClearProviders();
            builder.Logging.AddDebug();

            builder.Services.AddLogging();
            builder.Services.AddControllers()
                .AddApplicationPart(typeof(AdminDomainsController).Assembly);
            builder.Services
                .AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddScoped<MutableTenantContext>();
            builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<MutableTenantContext>());
            builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
            {
                options.UseInMemoryDatabase(databaseName, databaseRoot)
                    .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            });
            builder.Services.AddScoped<IDomainRepository, DomainRepository>();
            builder.Services.AddScoped<IHostNormalizer, HostNormalizer>();
            builder.Services.AddScoped<IDomainReadService, DomainReadService>();
            builder.Services.AddScoped<IDomainWriteService, DomainWriteService>();
            builder.Services.AddScoped<IDomainVerificationService, DomainVerificationService>();
            builder.Services.AddSingleton<IDnsResolver>(resolver);
            builder.Services.Configure<CertificateProviderOptions>(options =>
            {
                options.Enabled = true;
                options.Provider = ProviderType.LetsEncrypt;
                options.Environment = CertificateProviderEnvironment.Staging;
                options.AccountReference = "le-staging-account";
                options.ConfiguredIssuer = "lets-encrypt";
                options.ContactEmail = "ops@example.com";
                options.DirectoryUrl = new Uri("https://acme-staging-v02.api.letsencrypt.org/directory");
            });
            builder.Services.Configure<CertificateRenewalSchedulerOptions>(options =>
            {
                options.Enabled = true;
                options.ScanIntervalMinutes = 60;
                options.BatchSize = 50;
                options.MaxConcurrentRenewals = 4;
                options.RenewalThresholdDays = 30;
                options.ExpiryWarningThresholdDays = 14;
                options.ExpiryUrgentThresholdDays = 7;
                options.MaxAttempts = 5;
                options.RetryBackoffSeconds = 30;
                options.MaxRetryDelayMinutes = 60;
                options.ProviderRateLimitWindowMinutes = 60;
                options.ProviderAttemptBudget = 5;
                options.StaleOperationTimeoutMinutes = 30;
                options.JitterPercent = 0;
            });
            builder.Services.AddSingleton<ICertificateProvider, FakeCertificateProvider>();
            builder.Services.AddScoped<ICertificateProviderAccountSelector, CertificateProviderAccountSelector>();
            builder.Services.AddSingleton<ICaddyEdgeClient>(edge);
            builder.Services.AddSingleton<IEdgeCertificateProbe>(probe);
            builder.Services.AddSingleton<CertificateRenewalTelemetry>();
            builder.Services.AddScoped<ICertificateProvisioningService, CertificateProvisioningService>();
            builder.Services.AddScoped<ICertificateActivationService, CertificateActivationService>();
            builder.Services.AddScoped<ICertificateRenewalService, CertificateRenewalService>();
            builder.Services.AddScoped<ICertificateRevocationService, CertificateRevocationService>();
            builder.Services.AddScoped<ICertificateAdminCommandService, CertificateAdminCommandService>();
            builder.Services.AddSingleton(events);
            builder.Services.AddSingleton<IEventPublisher>(events);

            var app = builder.Build();
            app.UseAuthentication();
            app.Use(async (context, next) =>
            {
                if (context.User.Identity?.IsAuthenticated == true &&
                    Guid.TryParse(context.User.FindFirst("tenant_id")?.Value, out var tenantId))
                {
                    context.RequestServices.GetRequiredService<MutableTenantContext>().TenantId = tenantId;
                }

                await next();
            });
            app.UseAuthorization();
            app.MapControllers();

            await app.StartAsync();
            var address = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
                .Features.Get<IServerAddressesFeature>()!
                .Addresses.Single();

            var fixture = new AdminDomainHttpFixture(app, new Uri(address), databaseRoot, databaseName, resolver, edge, probe, events);
            await fixture.SeedTenantsAsync();
            return fixture;
        }

        public HttpClient CreateClient(Guid? tenantId = null, string role = "Owner")
        {
            var client = new HttpClient { BaseAddress = BaseAddress };
            if (tenantId.HasValue)
            {
                client.DefaultRequestHeaders.Add("X-Test-Auth", "true");
                client.DefaultRequestHeaders.Add("X-Test-Tenant", tenantId.Value.ToString("D"));
                client.DefaultRequestHeaders.Add("X-Test-Role", role);
                client.DefaultRequestHeaders.Add("X-Test-User", Guid.NewGuid().ToString("D"));
            }

            return client;
        }

        public async Task<Domain> SeedDomainAsync(
            Guid tenantId,
            string hostname,
            DomainLifecycleState lifecycleState,
            string? token = null,
            DomainType domainType = DomainType.Alias)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var domain = new Domain
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                HostName = hostname,
                NormalizedHostName = hostname.ToLowerInvariant(),
                DomainType = domainType,
                LifecycleState = lifecycleState,
                VerificationMethod = VerificationMethod.DnsTxt,
                VerificationToken = token ?? string.Empty,
                RedirectType = RedirectType.None,
                CanonicalPolicy = CanonicalPolicy.CanonicalizeToPrimary,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                IsDeleted = lifecycleState == DomainLifecycleState.Deleted,
                VerifiedAtUtc = lifecycleState is DomainLifecycleState.Verified or DomainLifecycleState.SslPending or DomainLifecycleState.SslActive or DomainLifecycleState.Primary
                    ? DateTime.UtcNow.AddMinutes(-1)
                    : null
            };

            db.Domains.Add(domain);
            await db.SaveChangesAsync();
            return domain;
        }

        public async Task<SslCertificate> SeedCertificateAsync(
            Guid tenantId,
            Guid domainId,
            string normalizedHostName,
            CertificateStatus status,
            bool isAuthoritative = false,
            int generation = 1,
            DateTime? notAfterUtc = null)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var certificate = new SslCertificate
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DomainId = domainId,
                NormalizedHostName = normalizedHostName,
                Provider = ProviderType.LetsEncrypt,
                ProviderImplementation = "http-test",
                ProviderAccountReference = "le-staging-account",
                ProviderEnvironment = CertificateProviderEnvironment.Staging.ToString(),
                Status = status,
                OperationPhase = status == CertificateStatus.Active
                    ? CertificateOperationPhase.ActivationProbe
                    : CertificateOperationPhase.MetadataSync,
                Generation = generation,
                ProviderRequestReference = $"test-provider:{Guid.NewGuid():N}",
                EdgeCertificateReference = $"test-edge:{Guid.NewGuid():N}",
                Issuer = "Fake CA",
                Subject = $"CN={normalizedHostName}",
                SubjectAlternativeNamesJson = JsonSerializer.Serialize(new[] { normalizedHostName }),
                Thumbprint = $"THUMBPRINT_{generation}",
                Fingerprint = $"FINGERPRINT_{generation}",
                SerialNumber = $"SERIAL_{generation}",
                SignatureAlgorithm = "sha256RSA",
                NotBeforeUtc = DateTime.UtcNow.AddDays(-1),
                NotAfterUtc = notAfterUtc ?? DateTime.UtcNow.AddDays(90),
                ActivatedAtUtc = status == CertificateStatus.Active ? DateTime.UtcNow.AddDays(-1) : null,
                NextRenewalUtc = status == CertificateStatus.Active ? DateTime.UtcNow.AddDays(30) : null,
                RenewalEligible = status == CertificateStatus.Active,
                Retryable = status == CertificateStatus.Failed,
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow,
                IsDeleted = false,
            };

            db.SslCertificates.Add(certificate);
            if (isAuthoritative)
            {
                var domain = await db.Domains.IgnoreQueryFilters().SingleAsync(d => d.Id == domainId);
                domain.SslCertificateId = certificate.Id;
                domain.LifecycleState = domain.DomainType == DomainType.Primary
                    ? DomainLifecycleState.Primary
                    : DomainLifecycleState.SslActive;
                domain.SslActivatedAtUtc = DateTime.UtcNow.AddDays(-1);
            }

            await db.SaveChangesAsync();
            return certificate;
        }

        public async Task<SslCertificateOperation> SeedCertificateOperationAsync(
            Guid tenantId,
            Guid domainId,
            Guid? certificateId,
            CertificateOperationType operationType,
            string idempotencyKey,
            CertificateOperationStatus status,
            CertificateOperationPhase phase,
            bool retryable,
            DateTime? retryAfterUtc)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var domain = await db.Domains.IgnoreQueryFilters().SingleAsync(d => d.Id == domainId);
            var certificate = certificateId.HasValue
                ? await db.SslCertificates.IgnoreQueryFilters().SingleAsync(c => c.Id == certificateId.Value)
                : null;
            var requestHash = operationType == CertificateOperationType.Provisioning && certificate is not null
                ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    $"{tenantId:N}|{domainId:N}|{domain.NormalizedHostName}|{certificate.Generation}|{idempotencyKey}|http-01,tls-alpn-01"))).ToLowerInvariant()
                : "test-request-hash";
            var operation = new SslCertificateOperation
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DomainId = domainId,
                CertificateId = certificateId,
                NormalizedHostName = domain.NormalizedHostName,
                OperationType = operationType,
                Status = status,
                Phase = phase,
                OperationId = Guid.NewGuid(),
                IdempotencyKey = idempotencyKey,
                RequestHash = requestHash,
                FencingToken = 1,
                LockOwnerReference = Guid.NewGuid(),
                LeaseExpiresAtUtc = status is CertificateOperationStatus.Pending or CertificateOperationStatus.Running
                    ? DateTime.UtcNow.AddMinutes(10)
                    : null,
                AttemptCount = 1,
                Retryable = retryable,
                RetryAfterUtc = retryAfterUtc,
                FailureCode = status == CertificateOperationStatus.Failed ? "TEST_RETRYABLE_FAILURE" : null,
                SafeFailureReason = status == CertificateOperationStatus.Failed ? "Safe retryable failure." : null,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                IsDeleted = false,
            };
            operation.OperationId = operation.Id;
            operation.LockOwnerReference = operation.Id;
            db.SslCertificateOperations.Add(operation);
            await db.SaveChangesAsync();
            return operation;
        }

        public async Task<Domain?> FindDomainAsync(Guid domainId)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.Domains.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(d => d.Id == domainId);
        }

        public async Task<int> CountAuditsAsync(Guid domainId, string action)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.DomainAuditEntries.IgnoreQueryFilters().CountAsync(a => a.DomainId == domainId && a.Action == action);
        }

        public async Task<int> CountVerificationsAsync(Guid domainId, VerificationStatus status)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await db.DomainVerifications.IgnoreQueryFilters().CountAsync(v => v.DomainId == domainId && v.Status == status);
        }

        public async Task SeedFailedVerificationAttemptsAsync(Guid domainId, int count)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var i = 0; i < count; i++)
            {
                db.DomainVerifications.Add(new DomainVerification
                {
                    Id = Guid.NewGuid(),
                    DomainId = domainId,
                    Method = VerificationMethod.DnsTxt,
                    Token = "redacted-test-token",
                    Status = VerificationStatus.Failed,
                    AttemptedAtUtc = DateTime.UtcNow.AddMinutes(-i),
                    FailureReason = DomainVerificationFailureReasons.TokenMismatch,
                    Attempt = i + 1,
                    CreatedAt = DateTime.UtcNow.AddMinutes(-i),
                    IsDeleted = false
                });
            }

            await db.SaveChangesAsync();
        }

        public async Task SeedTokenRefreshAuditsAsync(Guid domainId, Guid tenantId, int count)
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var i = 0; i < count; i++)
            {
                db.DomainAuditEntries.Add(new DomainAuditEntry
                {
                    Id = Guid.NewGuid(),
                    DomainId = domainId,
                    TenantId = tenantId,
                    Action = "DomainVerificationTokenRefreshed",
                    ActorType = ActorType.Merchant,
                    BeforeSnapshotJson = "{}",
                    AfterSnapshotJson = "{}",
                    TimestampUtc = DateTime.UtcNow.AddMinutes(-i)
                });
            }

            await db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _ = _databaseRoot;
            _ = _databaseName;
        }

        private async Task SeedTenantsAsync()
        {
            await using var scope = _app.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Tenants.AddRange(CreateTenant(TenantA), CreateTenant(TenantB));
            await db.SaveChangesAsync();
        }

        private static Tenant CreateTenant(Guid tenantId) => new()
        {
            Id = tenantId,
            Name = $"Tenant {tenantId:N}",
            IsActive = true,
            Status = TenantStatus.Active,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            DeletedAtUtc = null
        };

        private static int GetFreeTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";

        public TestAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-Auth"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var tenant = Request.Headers["X-Test-Tenant"].SingleOrDefault();
            var role = Request.Headers["X-Test-Role"].SingleOrDefault() ?? "Owner";
            var user = Request.Headers["X-Test-User"].SingleOrDefault() ?? Guid.NewGuid().ToString("D");
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user),
                new("sub", user),
                new(ClaimTypes.Role, role)
            };

            if (!string.IsNullOrWhiteSpace(tenant))
            {
                claims.Add(new Claim("tenant_id", tenant));
            }

            var identity = new ClaimsIdentity(claims, SchemeName);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    public sealed class DeterministicDnsResolver : IDnsResolver
    {
        private int _txtCalls;
        private int _cnameCalls;
        private int _httpCalls;

        public DnsLookupResult TxtResult { get; set; } =
            DnsLookupResult.CreateFailure("NXDOMAIN", "fake-http", TimeSpan.Zero);

        public DnsLookupResult CnameResult { get; set; } =
            DnsLookupResult.CreateFailure("NXDOMAIN", "fake-http", TimeSpan.Zero);

        public HttpLookupResult HttpResult { get; set; } =
            HttpLookupResult.CreateFailure("HTTP_404", 404, TimeSpan.Zero);

        public TimeSpan Delay { get; set; }

        public int TxtCalls => _txtCalls;
        public int CnameCalls => _cnameCalls;
        public int HttpCalls => _httpCalls;

        public async Task<DnsLookupResult> ResolveTxtAsync(string hostname, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _txtCalls);
            await DelayIfConfiguredAsync(ct);
            return TxtResult;
        }

        public async Task<DnsLookupResult> ResolveCnameAsync(string hostname, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _cnameCalls);
            await DelayIfConfiguredAsync(ct);
            return CnameResult;
        }

        public async Task<HttpLookupResult> ResolveHttpFileAsync(string url, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _httpCalls);
            await DelayIfConfiguredAsync(ct);
            return HttpResult;
        }

        private Task DelayIfConfiguredAsync(CancellationToken ct) =>
            Delay > TimeSpan.Zero ? Task.Delay(Delay, ct) : Task.CompletedTask;
    }

    public sealed class DeterministicCaddyEdgeClient : ICaddyEdgeClient
    {
        public bool RouteExists { get; set; } = true;
        public bool RouteMatchesDesiredState { get; set; } = true;
        public int EnsureCallCount { get; private set; }
        public int StateCallCount { get; private set; }

        public Task<CaddyDomainRouteResult> EnsureDomainRouteAsync(
            CaddyDomainRouteRequest request,
            CancellationToken cancellationToken = default)
        {
            EnsureCallCount++;
            var state = new CaddyDomainRouteState(
                true,
                true,
                request.NormalizedHostName,
                "fake-edge",
                "127.0.0.1:5010",
                null,
                null);

            return Task.FromResult(CaddyDomainRouteResult.Success(state, EnsureCallCount == 1, "fake-edge"));
        }

        public Task<CaddyDomainRouteState> GetDomainRouteStateAsync(
            CaddyDomainRouteQuery query,
            CancellationToken cancellationToken = default)
        {
            StateCallCount++;
            return Task.FromResult(RouteExists
                ? new CaddyDomainRouteState(
                    true,
                    RouteMatchesDesiredState,
                    query.NormalizedHostName,
                    "fake-edge",
                    "127.0.0.1:5010",
                    null,
                    null)
                : CaddyDomainRouteState.Missing(query.NormalizedHostName));
        }

        public Task<CaddyDomainRouteResult> RemoveDomainRouteAsync(
            CaddyDomainRouteQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CaddyDomainRouteResult.Success(CaddyDomainRouteState.Missing(query.NormalizedHostName), true, "fake-edge"));
    }

    public sealed class DeterministicEdgeCertificateProbe : IEdgeCertificateProbe
    {
        private int _probeCallCount;

        public bool ShouldSucceed { get; set; } = true;
        public int ProbeCallCount => _probeCallCount;

        public Task<EdgeCertificateProbeResult> ProbeAsync(
            EdgeCertificateProbeRequest request,
            CancellationToken cancellationToken = default)
        {
            var call = Interlocked.Increment(ref _probeCallCount);
            if (!ShouldSucceed)
            {
                return Task.FromResult(new EdgeCertificateProbeResult(
                    false,
                    true,
                    request.NormalizedHostName,
                    "SNI_PROBE_FAILED",
                    "External HTTPS/SNI certificate proof failed.",
                    null));
            }

            var thumbprint = request.ExpectedThumbprint ?? $"OBSERVED_THUMBPRINT_{call}";
            var fingerprint = request.ExpectedFingerprint ?? thumbprint;
            var serial = request.ExpectedSerialNumber ?? $"OBSERVED_SERIAL_{call}";
            var issuer = request.ExpectedIssuer ?? "Fake CA";
            var notAfterUtc = request.ExpectedNotAfterUtc ?? DateTime.UtcNow.AddDays(90 + call);

            return Task.FromResult(new EdgeCertificateProbeResult(
                true,
                false,
                request.NormalizedHostName,
                null,
                null,
                new EdgeCertificateIdentity(
                    $"CN={request.NormalizedHostName}",
                    issuer,
                    Array.AsReadOnly(new[] { request.NormalizedHostName }),
                    thumbprint,
                    fingerprint,
                    serial,
                    "sha256RSA",
                    DateTime.UtcNow.AddDays(-1),
                    notAfterUtc)));
        }
    }

    public sealed class RecordingEventPublisher : IEventPublisher
    {
        public List<object> PublishedEvents { get; } = new();

        public Task PublishAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            lock (PublishedEvents)
            {
                PublishedEvents.Add(@event!);
            }

            return Task.CompletedTask;
        }

        public Task PublishBatchAsync<T>(IEnumerable<T> events, CancellationToken cancellationToken = default)
            where T : IntegrationEvent
        {
            lock (PublishedEvents)
            {
                PublishedEvents.AddRange(events.Cast<object>());
            }

            return Task.CompletedTask;
        }

        public Task PublishSensitiveAsync<T>(T @event, CancellationToken cancellationToken = default)
            where T : IntegrationEvent, ISensitiveIntegrationEvent
        {
            lock (PublishedEvents)
            {
                PublishedEvents.Add(@event!);
            }

            return Task.CompletedTask;
        }
    }
}
