using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using PointOfSale.API.SuperAdmin.Infrastructure;
using PointOfSale.API.SuperAdmin.Models;

namespace PointOfSale.Tests.SuperAdmin;

public class SuperAdminExceptionMapperTests
{
    [Fact]
    public void Map_UnauthorizedAccessException_Returns_AuthUnauthorized()
    {
        var descriptor = SuperAdminExceptionMapper.Map(new UnauthorizedAccessException("denied"));

        Assert.Equal(StatusCodes.Status401Unauthorized, descriptor.StatusCode);
        Assert.Equal("AUTH_UNAUTHORIZED", descriptor.Code);
    }

    [Fact]
    public void Map_SuperAdminForbiddenException_Returns_AuthForbidden()
    {
        var descriptor = SuperAdminExceptionMapper.Map(new SuperAdminForbiddenException("forbidden"));

        Assert.Equal(StatusCodes.Status403Forbidden, descriptor.StatusCode);
        Assert.Equal("AUTH_FORBIDDEN", descriptor.Code);
        Assert.Equal("forbidden", descriptor.Message);
    }

    [Fact]
    public void Map_ValidationException_Returns_ValidationError()
    {
        var descriptor = SuperAdminExceptionMapper.Map(new ValidationException("invalid payload"));

        Assert.Equal(StatusCodes.Status400BadRequest, descriptor.StatusCode);
        Assert.Equal("VALIDATION_ERROR", descriptor.Code);
        Assert.Equal("invalid payload", descriptor.Message);
    }

    [Fact]
    public void Map_InvalidOperationException_Returns_BusinessRuleViolation()
    {
        var descriptor = SuperAdminExceptionMapper.Map(new InvalidOperationException("business rule"));

        Assert.Equal(StatusCodes.Status409Conflict, descriptor.StatusCode);
        Assert.Equal("BUSINESS_RULE_VIOLATION", descriptor.Code);
        Assert.Equal("business rule", descriptor.Message);
    }
}
