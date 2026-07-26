using System.Diagnostics.Metrics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers.Admin;
using PointOfSale.API.DTOs.Admin;
using PointOfSale.Core.Interfaces;
using PointOfSale.Infrastructure.Instrumentation;

namespace PointOfSale.Tests.Admin;

public sealed class AdminAbandonedCartRecoveryScopeTests
{
    [Fact]
    public async Task LogRecoveryResult_RejectsAuthenticatedAdminBodyTenantOutsideTrustedContext()
    {
        var trustedTenantId = Guid.NewGuid();
        var requestedTenantId = Guid.NewGuid();
        var settingsService = new Mock<ITenantAutomationSettingsService>();
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(context => context.TenantId).Returns(trustedTenantId);
        var authFailures = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();

        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == AbandonedCartRecoveryTelemetry.MeterName &&
                    instrument.Name == "abandoned_cart.callback.auth_failure_total")
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            authFailures.Add((measurement, tags.ToArray()));
        });
        listener.Start();

        var controller = new AdminAbandonedCartRecoveryController(
            tenantContext.Object,
            settingsService.Object,
            Mock.Of<IAbandonedCartEligibilityService>(),
            Mock.Of<IAbandonedCartRecoveryTelemetryService>(),
            new AbandonedCartRecoveryTelemetry(),
            NullLogger<AdminAbandonedCartRecoveryController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                        [
                            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
                            new Claim(ClaimTypes.Role, "Admin")
                        ], "test"))
                }
            }
        };

        var result = await controller.LogRecoveryResult(new AbandonedCartRecoveryLogRequest
        {
            TenantId = requestedTenantId,
            AbandonedCartId = Guid.NewGuid()
        }, CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        var payload = Assert.IsType<AbandonedCartRecoveryLogResponse>(forbidden.Value);
        Assert.False(payload.Success);
        Assert.Equal("Tenant scope mismatch", payload.Message);
        settingsService.Verify(
            service => service.GetSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        var authFailure = Assert.Single(authFailures);
        Assert.Equal(1, authFailure.Value);
        Assert.Contains(authFailure.Tags, tag => tag.Key == "tenant_bucket" && tag.Value?.ToString()?.StartsWith("bucket_", StringComparison.Ordinal) == true);
        Assert.Contains(authFailure.Tags, tag => tag.Key == "reason" && Equals(tag.Value, "admin_tenant_scope_mismatch"));
    }
}
