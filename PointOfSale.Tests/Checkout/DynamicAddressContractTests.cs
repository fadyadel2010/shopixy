using System.ComponentModel.DataAnnotations;
using System.Reflection;
using PointOfSale.API.DTOs.Checkout;
using PointOfSale.API.Services.Checkout;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;

namespace PointOfSale.Tests.Checkout;

public sealed class DynamicAddressContractTests
{
    [Fact]
    public void AddressDto_DoesNotStaticallyRequireMerchantConfiguredSystemFields()
    {
        var address = new AddressDto { Country = "EG" };
        var validationResults = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(
            address,
            new ValidationContext(address),
            validationResults,
            validateAllProperties: true);

        Assert.True(valid);
        Assert.Empty(validationResults);
        Assert.Equal((null, null), address.Validate());
    }

    [Fact]
    public void DynamicTextAdmission_EnforcesAbsoluteStorageBoundWithoutConfiguredMaximum()
    {
        var exception = InvokeDynamicValueValidation(new string('x', 1001));

        Assert.Equal("CUSTOM_FIELD_INVALID", exception.ErrorCode);
    }

    [Fact]
    public void DynamicTextAdmission_RejectsPostgresIncompatibleNullCharacter()
    {
        var exception = InvokeDynamicValueValidation("valid\0invalid");

        Assert.Equal("CUSTOM_FIELD_INVALID_CHARACTER", exception.ErrorCode);
    }

    private static CheckoutDynamicFieldValidationException InvokeDynamicValueValidation(string value)
    {
        var method = typeof(CheckoutService).GetMethod(
            "ValidateCustomFieldValue",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var definition = new CheckoutFieldDefinition
        {
            Id = Guid.NewGuid(),
            FieldKind = CheckoutFieldKind.Custom,
            FieldType = CheckoutFieldType.Text,
            Label = "Text",
            IsEnabled = true,
            ConfigurationJson = "{\"schemaVersion\":1}"
        };

        var invocation = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object?[] { definition, value }));
        return Assert.IsType<CheckoutDynamicFieldValidationException>(invocation.InnerException);
    }
}
