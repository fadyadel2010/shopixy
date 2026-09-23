using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Storefront;
using PointOfSale.API.DTOs.Users;
using PointOfSale.API.Models;
using PointOfSale.API.Services.Auth;
using PointOfSale.API.Services.Users;
using PointOfSale.API.Security;
using PointOfSale.API.SuperAdmin.Infrastructure;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Security;

namespace PointOfSale.Tests.Security;

/// <summary>
/// Thin-controller contract tests: status codes, error codes, ETag/If-Match
/// plumbing, and the generic password-reset response. Lifecycle behavior is
/// covered by PosUserManagementTests.
/// </summary>
public sealed class PosUserManagementApiTests
{
    [Fact]
    public async Task List_MapsServiceResult()
    {
        var service = new Mock<IPosUserManagementService>();
        service.Setup(s => s.ListAsync(It.IsAny<Guid>(), It.IsAny<PosUserListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PosUserListResultDto { TotalCount = 1 });
        var controller = Controller(service.Object);

        var result = Assert.IsType<OkObjectResult>(await controller.List(new PosUserListQuery(), CancellationToken.None));
        Assert.Equal(StatusCodes.Status200OK, result.StatusCode);
    }

    [Fact]
    public async Task Get_MapsNotFoundTo404WithCode()
    {
        var service = new Mock<IPosUserManagementService>();
        service.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(PosUserManagementException.NotFound());
        var controller = Controller(service.Object);

        var result = Assert.IsType<ObjectResult>(
            await controller.Get(Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(StatusCodes.Status404NotFound, result.StatusCode);
        Assert.Equal("USER_NOT_FOUND", ((ApiErrorResponse)result.Value!).Code);
    }

    [Fact]
    public async Task Invite_Returns202AndSendsEmailAfterCommit()
    {
        var service = new Mock<IPosUserManagementService>();
        var email = new Mock<IEmailSender>();
        var detail = new PosUserDetailDto { Id = Guid.NewGuid(), Email = "u@example.com", Version = "7" };
        service.Setup(s => s.InviteAsync(It.IsAny<Guid>(), It.IsAny<InvitePosUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((detail, "raw-token"));
        var controller = Controller(service.Object, email.Object);

        var result = Assert.IsType<ObjectResult>(await controller.Invite(
            new InvitePosUserRequest
            {
                FirstName = "F",
                LastName = "L",
                Username = "u",
                Email = "u@example.com",
                Role = "Cashier",
            }, CancellationToken.None));

        Assert.Equal(StatusCodes.Status202Accepted, result.StatusCode);
        Assert.Equal("\"7\"", controller.Response.Headers.ETag.ToString());
        email.Verify(e => e.SendEmailAsync(
            "u@example.com",
            It.IsAny<string>(),
            It.Is<string>(body => body.Contains(
                "href=\"http://localhost:5173/accept-invitation?token=raw-token\"",
                StringComparison.Ordinal)),
            It.Is<string>(body => body.Contains(
                "http://localhost:5173/accept-invitation?token=raw-token",
                StringComparison.Ordinal))), Times.Once);
    }

    [Fact]
    public async Task IssuePasswordReset_ReturnsGeneric202WithoutToken()
    {
        var service = new Mock<IPosUserManagementService>();
        var email = new Mock<IEmailSender>();
        service.Setup(s => s.IssuePasswordResetAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("raw-token");
        service.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PosUserDetailDto { Email = "u@example.com", FirstName = "F", Version = "1" });
        var controller = Controller(service.Object, email.Object);

        var result = Assert.IsType<ObjectResult>(
            await controller.IssuePasswordReset(Guid.NewGuid(), "\"1\"", CancellationToken.None));

        Assert.Equal(StatusCodes.Status202Accepted, result.StatusCode);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("raw-token", json, StringComparison.Ordinal);
        email.Verify(e => e.SendEmailAsync(
            "u@example.com",
            It.IsAny<string>(),
            It.Is<string>(body => body.Contains(
                "href=\"http://localhost:5173/reset-password?token=raw-token\"",
                StringComparison.Ordinal)),
            It.Is<string>(body => body.Contains(
                "http://localhost:5173/reset-password?token=raw-token",
                StringComparison.Ordinal))), Times.Once);
    }

    [Fact]
    public async Task VersionConflict_MapsTo412WithCode()
    {
        var service = new Mock<IPosUserManagementService>();
        service.Setup(s => s.ChangeRoleAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<ChangePosUserRoleRequest>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(PosUserManagementException.VersionConflict());
        var controller = Controller(service.Object);

        var result = Assert.IsType<ObjectResult>(await controller.ChangeRole(
            Guid.NewGuid(), new ChangePosUserRoleRequest { Role = "Manager" }, "\"1\"", CancellationToken.None));

        Assert.Equal(StatusCodes.Status412PreconditionFailed, result.StatusCode);
        Assert.Equal("USER_VERSION_CONFLICT", ((ApiErrorResponse)result.Value!).Code);
    }

    [Fact]
    public async Task OwnershipTransfer_MapsForbidden()
    {
        var service = new Mock<IPosUserManagementService>();
        service.Setup(s => s.TransferOwnershipAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PosUserManagementException(
                "POS_PERMISSION_FORBIDDEN", StatusCodes.Status403Forbidden, "Access denied."));
        var controller = Controller(service.Object);

        var result = Assert.IsType<ObjectResult>(await controller.TransferOwnership(
            new TransferPosOwnershipRequest { TargetUserId = Guid.NewGuid(), TargetVersion = "1" },
            "\"1\"", CancellationToken.None));

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task AcceptInvitation_MapsExpiredTo410()
    {
        var service = new Mock<IPosUserManagementService>();
        service.Setup(s => s.AcceptInvitationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(PosUserManagementException.TokenExpired());
        var controller = AuthController(service.Object);

        var result = Assert.IsType<ObjectResult>(await controller.AcceptUserInvitation(
            new AcceptPosUserInvitationRequest { Token = "t", Password = "LongEnough1!" }, CancellationToken.None));

        Assert.Equal(StatusCodes.Status410Gone, result.StatusCode);
    }

    [Fact]
    public async Task AcceptInvitation_SucceedsWithGenericMessage()
    {
        var service = new Mock<IPosUserManagementService>();
        var controller = AuthController(service.Object);

        var result = Assert.IsType<OkObjectResult>(await controller.AcceptUserInvitation(
            new AcceptPosUserInvitationRequest { Token = "t", Password = "LongEnough1!" }, CancellationToken.None));

        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("cookie", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PermissionDenial_ReturnsBoundedPosErrorEnvelope()
    {
        var handler = new SuperAdminAuthorizationMiddlewareResultHandler(
            NullLogger<SuperAdminAuthorizationMiddlewareResultHandler>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var policy = new AuthorizationPolicyBuilder()
            .AddRequirements(new PosPermissionRequirement(PosPermissions.UsersView))
            .Build();

        await handler.HandleAsync(
            _ => Task.CompletedTask,
            context,
            policy,
            PolicyAuthorizationResult.Forbid());

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Contains("POS_PERMISSION_FORBIDDEN", body, StringComparison.Ordinal);
    }

    [Fact]
    public void SecuritySensitiveDtos_RejectUnknownJsonProperties()
    {
        Assert.Throws<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<AcceptPosUserInvitationRequest>(
                """{"token":"t","password":"LongEnough1!","tenantId":"forged"}"""));
        Assert.Throws<System.Text.Json.JsonException>(() =>
            System.Text.Json.JsonSerializer.Deserialize<InvitePosUserRequest>(
                """{"firstName":"F","lastName":"L","username":"u","email":"u@example.com","role":"Cashier","isActive":true}"""));
    }

    private static AdminUsersController Controller(
        IPosUserManagementService service, IEmailSender? email = null)
    {
        var controller = new AdminUsersController(
            service,
            email ?? Mock.Of<IEmailSender>(),
            Options.Create(new PosUserManagementOptions
            {
                FrontendBaseUrl = "http://localhost:5173",
            }),
            NullLogger<AdminUsersController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = AuthenticatedHttp() },
        };
        return controller;
    }

    private static AuthController AuthController(IPosUserManagementService service)
    {
        var controller = new AuthController(
            null!,
            Options.Create(new JwtSettings
            {
                Secret = "test-secret-key-must-be-long-enough-123456",
                Issuer = "Test",
                Audience = "Test",
                AccessTokenExpirationMinutes = 15,
                RefreshTokenExpirationDays = 7,
            }),
            Mock.Of<PointOfSale.Core.Interfaces.ITenantContext>(),
            new PasswordHasher<User>(),
            Mock.Of<IEmailVerificationService>(),
            Mock.Of<PointOfSale.API.Services.Audit.IAuditService>(),
            Mock.Of<PointOfSale.API.Services.Governance.IUserGovernanceService>(),
            Mock.Of<PointOfSale.API.Services.CommerceHub.Finance.ICommerceHubVendorDebtService>(),
            NullLogger<AuthController>.Instance,
            service)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return controller;
    }

    private static DefaultHttpContext AuthenticatedHttp()
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D"))], "Test"));
        return http;
    }
}
