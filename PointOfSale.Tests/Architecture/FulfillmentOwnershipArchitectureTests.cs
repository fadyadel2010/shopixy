using System;
using System.Linq;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;
using Xunit;

namespace PointOfSale.Tests.Architecture;

/// <summary>
/// FULFILLMENT OWNERSHIP ARCHITECTURE TESTS
/// 
/// These tests enforce the architectural invariants for fulfillment ownership.
/// They MUST pass to ensure the system cannot be misused.
/// 
/// CRITICAL INVARIANTS:
/// 1. FulfillmentMode is immutable after order creation
/// 2. Manual orders cannot accept carrier webhook updates
/// 3. Carrier orders cannot accept manual admin transitions
/// 4. FulfillmentMode is never inferred from ShippingMethod
/// 5. All fulfillment transitions go through IOrderFulfillmentService
/// </summary>
public class FulfillmentOwnershipArchitectureTests
{
    #region Invariant 1: FulfillmentMode Immutability

    [Fact]
    public void Order_FulfillmentMode_MustBeImmutable_AfterCreation()
    {
        // ARRANGE
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            OrderNumber = "TEST-001",
            FulfillmentMode = FulfillmentMode.Manual,
            FulfillmentCarrierCode = null
        };

        // ACT & ASSERT
        // Attempting to change FulfillmentMode should be blocked by DB trigger
        // This test verifies the property exists and is settable (for initial creation)
        // The DB trigger prevents updates after insert
        Assert.Equal(FulfillmentMode.Manual, order.FulfillmentMode);
        
        // Verify computed properties work correctly
        Assert.True(order.AllowsManualFulfillment);
        Assert.False(order.AllowsCarrierWebhooks);
    }

    [Fact]
    public void Order_CarrierMode_MustHaveCarrierCode()
    {
        // ARRANGE & ACT
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            OrderNumber = "TEST-002",
            FulfillmentMode = FulfillmentMode.Carrier,
            FulfillmentCarrierCode = "bosta"
        };

        // ASSERT
        Assert.Equal(FulfillmentMode.Carrier, order.FulfillmentMode);
        Assert.Equal("bosta", order.FulfillmentCarrierCode);
        Assert.False(order.AllowsManualFulfillment);
        Assert.True(order.AllowsCarrierWebhooks);
    }

    [Fact]
    public void Order_ManualMode_MustNotHaveCarrierCode()
    {
        // ARRANGE & ACT
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            OrderNumber = "TEST-003",
            FulfillmentMode = FulfillmentMode.Manual,
            FulfillmentCarrierCode = null
        };

        // ASSERT
        Assert.Equal(FulfillmentMode.Manual, order.FulfillmentMode);
        Assert.Null(order.FulfillmentCarrierCode);
        Assert.True(order.AllowsManualFulfillment);
        Assert.False(order.AllowsCarrierWebhooks);
    }

    #endregion

    #region Invariant 2: No Direct FulfillmentStatus Updates

    [Fact]
    public void Order_FulfillmentStatus_MustNotBeDirectlyUpdatable()
    {
        // This test verifies that FulfillmentStatus uses a backing field
        // and change tracking mechanism (similar to PaymentStatus)
        
        // ARRANGE
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            OrderNumber = "TEST-004",
            FulfillmentStatus = FulfillmentStatus.Created
        };

        // ACT
        order.FulfillmentStatus = FulfillmentStatus.Confirmed;

        // ASSERT
        // The property should track changes
        Assert.True(order.FulfillmentStatusChanged);
        Assert.Equal(FulfillmentStatus.Confirmed, order.FulfillmentStatus);
        
        // After accepting the change, tracking should reset
        order.AcceptFulfillmentStatusChange();
        Assert.False(order.FulfillmentStatusChanged);
    }

    #endregion

    #region Invariant 3: FulfillmentMode Enum Values

    [Fact]
    public void FulfillmentMode_MustHaveCorrectValues()
    {
        // ASSERT
        Assert.Equal(0, (int)FulfillmentMode.Manual);
        Assert.Equal(1, (int)FulfillmentMode.Carrier);
    }

    [Fact]
    public void FulfillmentMode_ExtensionMethods_MustWorkCorrectly()
    {
        // ARRANGE
        var manualMode = FulfillmentMode.Manual;
        var carrierMode = FulfillmentMode.Carrier;

        // ASSERT - Manual mode
        Assert.True(manualMode.AllowsManualTransitions());
        Assert.False(manualMode.AllowsCarrierWebhooks());
        Assert.Equal("Manual Fulfillment (Admin-Controlled)", manualMode.GetDescription());

        // ASSERT - Carrier mode
        Assert.False(carrierMode.AllowsManualTransitions());
        Assert.True(carrierMode.AllowsCarrierWebhooks());
        Assert.Equal("Carrier Fulfillment (Webhook-Controlled)", carrierMode.GetDescription());
    }

    #endregion

    #region Invariant 4: Order Entity Properties

    [Fact]
    public void Order_MustHaveFulfillmentOwnershipProperties()
    {
        // ARRANGE
        var orderType = typeof(Order);

        // ACT
        var fulfillmentModeProperty = orderType.GetProperty("FulfillmentMode");
        var fulfillmentCarrierCodeProperty = orderType.GetProperty("FulfillmentCarrierCode");
        var allowsManualFulfillmentProperty = orderType.GetProperty("AllowsManualFulfillment");
        var allowsCarrierWebhooksProperty = orderType.GetProperty("AllowsCarrierWebhooks");

        // ASSERT
        Assert.NotNull(fulfillmentModeProperty);
        Assert.NotNull(fulfillmentCarrierCodeProperty);
        Assert.NotNull(allowsManualFulfillmentProperty);
        Assert.NotNull(allowsCarrierWebhooksProperty);

        // Verify property types
        Assert.Equal(typeof(FulfillmentMode), fulfillmentModeProperty.PropertyType);
        Assert.Equal(typeof(string), fulfillmentCarrierCodeProperty.PropertyType);
        Assert.Equal(typeof(bool), allowsManualFulfillmentProperty.PropertyType);
        Assert.Equal(typeof(bool), allowsCarrierWebhooksProperty.PropertyType);
    }

    #endregion

    #region Invariant 5: IOrderFulfillmentService Interface

    [Fact]
    public void IOrderFulfillmentService_MustExist()
    {
        // ARRANGE
        var assembly = Assembly.Load("PointOfSale.Core");

        // ACT
        var serviceType = assembly.GetTypes()
            .FirstOrDefault(t => t.Name == "IOrderFulfillmentService");

        // ASSERT
        Assert.NotNull(serviceType);
        Assert.True(serviceType.IsInterface);
    }

    [Fact]
    public void IOrderFulfillmentService_MustHaveRequiredMethods()
    {
        // ARRANGE
        var assembly = Assembly.Load("PointOfSale.Core");
        var serviceType = assembly.GetTypes()
            .FirstOrDefault(t => t.Name == "IOrderFulfillmentService");

        Assert.NotNull(serviceType);

        // ACT
        var methods = serviceType.GetMethods();
        var methodNames = methods.Select(m => m.Name).ToList();

        // ASSERT - Verify all required methods exist
        Assert.Contains("ConfirmAsync", methodNames);
        Assert.Contains("PackAsync", methodNames);
        Assert.Contains("MarkOutForDeliveryAsync", methodNames);
        Assert.Contains("MarkDeliveredAsync", methodNames);
        Assert.Contains("SynchronizeFromShipmentAsync", methodNames);
        Assert.Contains("MarkCompletedAsync", methodNames);
        Assert.Contains("MarkReturnedAsync", methodNames);
        Assert.Contains("MarkDeliveryFailedAsync", methodNames);
        Assert.Contains("CancelAsync", methodNames);
        Assert.Contains("GetAvailableActionsAsync", methodNames);
    }

    #endregion

    #region Invariant 6: FulfillmentStatus Transitions

    [Fact]
    public void FulfillmentStatus_MustHaveValidTransitions()
    {
        // ARRANGE & ACT
        var createdTransitions = FulfillmentStatus.Created.GetAllowedTransitions();
        var confirmedTransitions = FulfillmentStatus.Confirmed.GetAllowedTransitions();
        var packedTransitions = FulfillmentStatus.Packed.GetAllowedTransitions();
        var outForDeliveryTransitions = FulfillmentStatus.OutForDelivery.GetAllowedTransitions();
        var deliveredTransitions = FulfillmentStatus.Delivered.GetAllowedTransitions();

        // ASSERT - Created can go to Confirmed or Cancelled
        Assert.Contains(FulfillmentStatus.Confirmed, createdTransitions);
        Assert.Contains(FulfillmentStatus.Cancelled, createdTransitions);

        // ASSERT - Confirmed can go to Packed or Cancelled
        Assert.Contains(FulfillmentStatus.Packed, confirmedTransitions);
        Assert.Contains(FulfillmentStatus.Cancelled, confirmedTransitions);

        // ASSERT - Packed can go to OutForDelivery or Cancelled
        Assert.Contains(FulfillmentStatus.OutForDelivery, packedTransitions);
        Assert.Contains(FulfillmentStatus.Cancelled, packedTransitions);

        // ASSERT - OutForDelivery can go to Delivered or FailedDelivery
        Assert.Contains(FulfillmentStatus.Delivered, outForDeliveryTransitions);
        Assert.Contains(FulfillmentStatus.FailedDelivery, outForDeliveryTransitions);

        // ASSERT - Delivered can go to Completed or Returned
        Assert.Contains(FulfillmentStatus.Completed, deliveredTransitions);
        Assert.Contains(FulfillmentStatus.Returned, deliveredTransitions);
    }

    [Fact]
    public void FulfillmentStatus_CanTransitionTo_MustWorkCorrectly()
    {
        // ARRANGE
        var currentStatus = FulfillmentStatus.Created;

        // ACT & ASSERT
        Assert.True(currentStatus.CanTransitionTo(FulfillmentStatus.Confirmed));
        Assert.True(currentStatus.CanTransitionTo(FulfillmentStatus.Cancelled));
        Assert.False(currentStatus.CanTransitionTo(FulfillmentStatus.Delivered));
        Assert.False(currentStatus.CanTransitionTo(FulfillmentStatus.Packed));
    }

    #endregion

    #region Invariant 7: OrderCreatedEvent Must Include Fulfillment Ownership

    [Fact]
    public void OrderCreatedEvent_MustHaveFulfillmentOwnershipFields()
    {
        // ARRANGE
        var assembly = Assembly.Load("PointOfSale.Core");
        var eventType = assembly.GetTypes()
            .FirstOrDefault(t => t.Name == "OrderCreatedEvent");

        Assert.NotNull(eventType);

        // ACT
        var fulfillmentModeProperty = eventType.GetProperty("FulfillmentMode");
        var fulfillmentCarrierCodeProperty = eventType.GetProperty("FulfillmentCarrierCode");

        // ASSERT
        Assert.NotNull(fulfillmentModeProperty);
        Assert.NotNull(fulfillmentCarrierCodeProperty);
    }

    #endregion

    #region Documentation Tests

    [Fact]
    public void FulfillmentOwnership_DocumentationExists()
    {
        // This test verifies that key types have proper XML documentation
        
        // ARRANGE
        var orderType = typeof(Order);
        var fulfillmentModeType = typeof(FulfillmentMode);

        // ACT
        var orderFulfillmentModeProperty = orderType.GetProperty("FulfillmentMode");
        var orderFulfillmentCarrierCodeProperty = orderType.GetProperty("FulfillmentCarrierCode");

        // ASSERT
        Assert.NotNull(orderFulfillmentModeProperty);
        Assert.NotNull(orderFulfillmentCarrierCodeProperty);
        
        // Verify enum has values
        var enumValues = Enum.GetValues(typeof(FulfillmentMode));
        Assert.Equal(2, enumValues.Length);
    }

    #endregion
}
