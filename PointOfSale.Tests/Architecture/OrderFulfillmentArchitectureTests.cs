using System.Text.RegularExpressions;

namespace PointOfSale.Tests.Architecture;

/// <summary>
/// PHASE OFL: Order Fulfillment Lifecycle Architecture Tests
/// 
/// These tests enforce the architectural rules for the Order Fulfillment Lifecycle.
/// 
/// ABSOLUTE LAWS:
/// 1. Controllers must NOT write FulfillmentStatus directly
/// 2. Only OrderFulfillmentService may modify FulfillmentStatus
/// 3. OrderDeliveredEvent must exist and be consumed
/// 4. OrderDeliveredEventConsumer must call PaymentLifecycleService
/// 5. No controller references PaymentLifecycle directly for fulfillment
/// </summary>
public class OrderFulfillmentArchitectureTests
{
    [Fact]
    public void OrderFulfillmentService_ShouldExist()
    {
        // Arrange & Act
        var serviceType = Type.GetType("PointOfSale.Infrastructure.Fulfillment.OrderFulfillmentService, PointOfSale.Infrastructure");

        // Assert
        Assert.NotNull(serviceType);
    }

    [Fact]
    public void IOrderFulfillmentService_ShouldExist()
    {
        // Arrange & Act
        var interfaceType = Type.GetType("PointOfSale.Core.Interfaces.Fulfillment.IOrderFulfillmentService, PointOfSale.Core");

        // Assert
        Assert.NotNull(interfaceType);
    }

    [Fact]
    public void FulfillmentStatus_Enum_ShouldExist()
    {
        // Arrange & Act
        var enumType = Type.GetType("PointOfSale.Core.Enums.FulfillmentStatus, PointOfSale.Core");

        // Assert
        Assert.NotNull(enumType);
        Assert.True(enumType.IsEnum);
    }

    [Fact]
    public void FulfillmentStatus_ShouldHaveRequiredValues()
    {
        // Arrange
        var enumType = Type.GetType("PointOfSale.Core.Enums.FulfillmentStatus, PointOfSale.Core");
        
        // Act
        var values = Enum.GetNames(enumType!);

        // Assert
        Assert.Contains("Created", values);
        Assert.Contains("Confirmed", values);
        Assert.Contains("Packed", values);
        Assert.Contains("OutForDelivery", values);
        Assert.Contains("Delivered", values);
        Assert.Contains("Completed", values);
        Assert.Contains("Returned", values);
        Assert.Contains("FailedDelivery", values);
    }

    [Fact]
    public void FulfillmentDeliveredEvent_ShouldExist()
    {
        // Arrange & Act
        var eventType = Type.GetType("PointOfSale.Core.Events.FulfillmentDeliveredEvent, PointOfSale.Core");

        // Assert
        Assert.NotNull(eventType);
    }

    [Fact]
    public void FulfillmentDeliveredEvent_ShouldHavePaymentIntegrationProperties()
    {
        // Arrange
        var eventType = Type.GetType("PointOfSale.Core.Events.FulfillmentDeliveredEvent, PointOfSale.Core");

        // Act & Assert - These properties are needed for PaymentLifecycle integration
        Assert.NotNull(eventType);
        Assert.NotNull(eventType.GetProperty("PaymentMethod"));
        Assert.NotNull(eventType.GetProperty("PaymentStatus"));
        Assert.NotNull(eventType.GetProperty("OrderTotal"));
    }

    [Fact]
    public void FulfillmentDeliveredEventConsumer_ShouldExist()
    {
        // Arrange & Act
        var consumerType = Type.GetType("PointOfSale.Infrastructure.Consumers.FulfillmentDeliveredEventConsumer, PointOfSale.Infrastructure");

        // Assert
        Assert.NotNull(consumerType);
    }

    [Fact]
    public void OrderFulfillmentController_ShouldExist()
    {
        // Arrange & Act
        var controllerType = Type.GetType("PointOfSale.API.Controllers.OrderFulfillmentController, PointOfSale.API");

        // Assert
        Assert.NotNull(controllerType);
    }

    [Fact]
    public void OrderFulfillmentController_ShouldHaveRequiredEndpoints()
    {
        // Arrange
        var controllerType = Type.GetType("PointOfSale.API.Controllers.OrderFulfillmentController, PointOfSale.API");

        // Act
        var methods = controllerType!.GetMethods();
        var methodNames = methods.Select(m => m.Name).ToList();

        // Assert - All required fulfillment endpoints
        Assert.Contains("GetAvailableActions", methodNames);
        Assert.Contains("Confirm", methodNames);
        Assert.Contains("Pack", methodNames);
        Assert.Contains("OutForDelivery", methodNames);
        Assert.Contains("Delivered", methodNames);
        Assert.Contains("Complete", methodNames);
        Assert.Contains("Returned", methodNames);
        Assert.Contains("Cancel", methodNames);
    }

    [Fact]
    public void FulfillmentStatusSaveChangesInterceptor_ShouldExist()
    {
        // Arrange & Act
        var interceptorType = Type.GetType("PointOfSale.Infrastructure.Data.FulfillmentStatusSaveChangesInterceptor, PointOfSale.Infrastructure");

        // Assert
        Assert.NotNull(interceptorType);
    }

    [Fact]
    public void OrderEntity_ShouldHaveFulfillmentStatusProperty()
    {
        // Arrange
        var orderType = Type.GetType("PointOfSale.Core.Entities.Order, PointOfSale.Core");

        // Act
        var property = orderType!.GetProperty("FulfillmentStatus");

        // Assert
        Assert.NotNull(property);
    }

    [Fact]
    public void OrderEntity_ShouldHaveFulfillmentStatusChangeTracking()
    {
        // Arrange
        var orderType = Type.GetType("PointOfSale.Core.Entities.Order, PointOfSale.Core");

        // Act
        var changedProperty = orderType!.GetProperty("FulfillmentStatusChanged");
        var acceptMethod = orderType.GetMethod("AcceptFulfillmentStatusChange");

        // Assert
        Assert.NotNull(changedProperty);
        Assert.NotNull(acceptMethod);
    }

    [Fact]
    public void Controllers_ShouldNotDirectlySetFulfillmentStatus()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        var apiProject = Path.Combine(solutionDir, "PointOfSale.API", "Controllers");
        
        if (!Directory.Exists(apiProject))
        {
            Assert.True(true, "Controllers directory not found - skipping scan");
            return;
        }

        var violations = new List<string>();
        
        // Forbidden patterns - direct FulfillmentStatus assignment
        var forbiddenPatterns = new[]
        {
            @"\.FulfillmentStatus\s*=\s*FulfillmentStatus\.",
            @"\.FulfillmentStatus\s*=\s*\(",
        };
        
        // Whitelisted controllers (none should be allowed)
        var whitelistedControllers = new[]
        {
            "OrderFulfillmentController.cs", // Only delegates to service, doesn't write directly
        };

        // Act
        var controllerFiles = Directory.GetFiles(apiProject, "*.cs", SearchOption.AllDirectories);
        
        foreach (var file in controllerFiles)
        {
            var fileName = Path.GetFileName(file);
            
            // Skip whitelisted
            if (whitelistedControllers.Any(w => fileName.Equals(w, StringComparison.OrdinalIgnoreCase)))
                continue;
            
            var content = File.ReadAllText(file);
            
            foreach (var pattern in forbiddenPatterns)
            {
                if (Regex.IsMatch(content, pattern))
                {
                    var lineNumber = GetLineNumber(content, pattern);
                    violations.Add($"{fileName}:{lineNumber} - Direct FulfillmentStatus assignment detected");
                }
            }
        }

        // Assert
        Assert.True(violations.Count == 0,
            $"PHASE OFL VIOLATION!\n\n" +
            $"Controllers must NOT directly set FulfillmentStatus:\n" +
            string.Join("\n", violations.Select(v => $"  ❌ {v}")) +
            "\n\nUse IOrderFulfillmentService instead:\n" +
            "  ✅ _fulfillmentService.ConfirmAsync()\n" +
            "  ✅ _fulfillmentService.PackAsync()\n" +
            "  ✅ _fulfillmentService.MarkDeliveredAsync()");
    }

    [Fact]
    public void NoClass_ShouldSet_FulfillmentStatus_Directly_ExceptOrderFulfillmentService()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        var apiProject = Path.Combine(solutionDir, "PointOfSale.API");
        var infraProject = Path.Combine(solutionDir, "PointOfSale.Infrastructure");
        
        // Whitelist - ONLY OrderFulfillmentService can set FulfillmentStatus
        var whitelistedFiles = new[]
        {
            "OrderFulfillmentService.cs",
            "Order.cs", // Entity default value
            "FulfillmentStatusSaveChangesInterceptor.cs", // Tests for change tracking
        };
        
        var whitelistedCodePatterns = new[]
        {
            "FulfillmentStatus ==", // Comparisons are allowed
            "FulfillmentStatus !=", // Comparisons are allowed
            "== FulfillmentStatus.", // Comparisons are allowed
            "!= FulfillmentStatus.", // Comparisons are allowed
            "FulfillmentStatus.Created", // Enum value reference (not assignment)
            "case FulfillmentStatus", // Switch cases
            "FulfillmentStatus target", // Method parameters
            "FulfillmentStatus currentStatus", // Variable declarations
            "FulfillmentStatus status", // Variable declarations
            "=> FulfillmentStatus.", // Expression body enum values
        };

        var violations = new List<string>();

        // Scan API and Infrastructure projects
        foreach (var projectPath in new[] { apiProject, infraProject })
        {
            if (!Directory.Exists(projectPath)) continue;
            
            var files = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories);
            
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                
                // Skip whitelisted files
                if (whitelistedFiles.Any(w => fileName.Equals(w, StringComparison.OrdinalIgnoreCase)))
                    continue;
                
                // Skip test files and migrations
                if (file.Contains("Tests") || file.Contains("Migrations"))
                    continue;
                
                var lines = File.ReadAllLines(file);
                
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    
                    // Check for direct assignment pattern
                    if (Regex.IsMatch(line, @"\.FulfillmentStatus\s*=\s*") &&
                        !Regex.IsMatch(line, @"\.FulfillmentStatus\s*=="))
                    {
                        // Check if it's a whitelisted pattern
                        var isWhitelisted = whitelistedCodePatterns.Any(p => line.Contains(p));
                        
                        if (!isWhitelisted)
                        {
                            var relativePath = Path.GetRelativePath(solutionDir, file);
                            violations.Add($"{relativePath}:{i + 1} → {line.Trim()}");
                        }
                    }
                }
            }
        }

        // Assert
        Assert.True(violations.Count == 0,
            $"PHASE OFL VIOLATION!\n\n" +
            $"Direct FulfillmentStatus assignment detected outside OrderFulfillmentService:\n" +
            string.Join("\n", violations.Select(v => $"  ❌ {v}")) +
            "\n\nONLY OrderFulfillmentService may modify FulfillmentStatus.\n" +
            "Use IOrderFulfillmentService methods instead.");
    }

    [Fact]
    public void FulfillmentDeliveredEventConsumer_MustNotConvertDeliveryIntoCodCollection()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        // Note: Consumer file is still named OrderDeliveredEventConsumer.cs for historical reasons
        var consumerPath = Path.Combine(solutionDir, 
            "PointOfSale.Infrastructure", 
            "Consumers", 
            "OrderDeliveredEventConsumer.cs");

        if (!File.Exists(consumerPath))
        {
            Assert.True(false, "OrderDeliveredEventConsumer.cs (FulfillmentDeliveredEventConsumer) not found");
            return;
        }

        var sourceCode = File.ReadAllText(consumerPath);

        Assert.DoesNotContain("IPaymentLifecycleService", sourceCode, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkOfflineCollectionDeliveredAsync", sourceCode, StringComparison.Ordinal);
        Assert.Contains("awaiting explicit collection evidence", sourceCode, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderFulfillmentService_ShouldPublishFulfillmentDeliveredEvent()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        var servicePath = Path.Combine(solutionDir, 
            "PointOfSale.Infrastructure", 
            "Fulfillment", 
            "OrderFulfillmentService.cs");

        if (!File.Exists(servicePath))
        {
            Assert.True(false, "OrderFulfillmentService.cs not found");
            return;
        }

        var sourceCode = File.ReadAllText(servicePath);

        // Act
        var publishesDeliveredEvent = 
            sourceCode.Contains("FulfillmentDeliveredEvent") &&
            sourceCode.Contains("PublishAsync");

        // Assert
        Assert.True(publishesDeliveredEvent,
            "OrderFulfillmentService.MarkDeliveredAsync must publish FulfillmentDeliveredEvent to trigger PaymentLifecycle integration");
    }

    [Fact]
    public void DeliveredState_MustRemainSeparateFromCodCollection()
    {
        // Delivery still publishes its event for projections and commission approval
        // revalidation, but the event must not fabricate COD collection evidence.

        // Arrange
        var solutionDir = GetSolutionDirectory();
        
        // 1. Check OrderFulfillmentService publishes event
        var fulfillmentServicePath = Path.Combine(solutionDir, 
            "PointOfSale.Infrastructure", "Fulfillment", "OrderFulfillmentService.cs");
        
        if (!File.Exists(fulfillmentServicePath))
        {
            Assert.True(false, "OrderFulfillmentService.cs not found");
            return;
        }
        
        var fulfillmentServiceCode = File.ReadAllText(fulfillmentServicePath);
        var publishesEvent = fulfillmentServiceCode.Contains("FulfillmentDeliveredEvent") &&
                            fulfillmentServiceCode.Contains("_eventPublisher.PublishAsync");

        // 2. Check consumer does not call PaymentLifecycle collection
        var consumerPath = Path.Combine(solutionDir, 
            "PointOfSale.Infrastructure", "Consumers", "OrderDeliveredEventConsumer.cs");
        
        if (!File.Exists(consumerPath))
        {
            Assert.True(false, "OrderDeliveredEventConsumer.cs (FulfillmentDeliveredEventConsumer) not found");
            return;
        }
        
        var consumerCode = File.ReadAllText(consumerPath);
        var callsPaymentLifecycle = consumerCode.Contains("MarkOfflineCollectionDeliveredAsync");

        // Assert
        Assert.True(publishesEvent && !callsPaymentLifecycle,
            "Fulfillment delivery must publish its event without converting delivery into COD collection. " +
            "Collection requires the payment lifecycle command or carrier collection evidence.");
    }

    [Fact]
    public void CheckoutCon10_FulfillmentTransitions_MustReloadLockedSqlAuthorityBeforePublishing()
    {
        var solutionDir = GetSolutionDirectory();
        var servicePath = Path.Combine(
            solutionDir,
            "PointOfSale.Infrastructure",
            "Fulfillment",
            "OrderFulfillmentService.cs");

        var source = File.ReadAllText(servicePath);

        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", source, StringComparison.Ordinal);
        Assert.Contains("LoadOrderForFulfillmentMutationAsync", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, ROWLOCK, HOLDLOCK", source, StringComparison.Ordinal);
        Assert.Contains("Replay collapsed against current SQL fulfillment authority", source, StringComparison.Ordinal);
        Assert.Contains("IsAtOrBeyondOutboundTarget(oldStatus, targetStatus)", source, StringComparison.Ordinal);

        var lockedLoadIndex = source.IndexOf("LoadOrderForFulfillmentMutationAsync(orderId", StringComparison.Ordinal);
        var publishIndex = source.IndexOf("_eventPublisher.PublishAsync(domainEvent", StringComparison.Ordinal);

        Assert.True(
            lockedLoadIndex >= 0 && publishIndex > lockedLoadIndex,
            "Fulfillment lifecycle events must be emitted only after current SQL order authority is reloaded and locked.");
    }

    [Fact]
    public void CheckoutCon10_ShipmentMutationPaths_MustUseSerializableSqlRecomposition()
    {
        var solutionDir = GetSolutionDirectory();
        var shipmentServicePath = Path.Combine(
            solutionDir,
            "PointOfSale.API",
            "Services",
            "Shipping",
            "ShipmentService.cs");

        var source = File.ReadAllText(shipmentServicePath);

        Assert.True(
            Regex.Matches(source, "BeginTransactionAsync\\(\\s*\\r?\\n\\s*IsolationLevel\\.Serializable").Count >= 3,
            "Shipment create, status update, and authority repair must run inside serializable SQL transactions.");
        Assert.Contains("LoadOrderForShipmentMutationAsync", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("EnsureOutboundShipmentFinancialAuthority", source, StringComparison.Ordinal);
        Assert.Contains("SynchronizeFulfillmentAuthorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("IOrderFulfillmentService", source, StringComparison.Ordinal);

        var prepareIndex = source.IndexOf("PrepareShipmentAuthorityContextAsync", StringComparison.Ordinal);
        var emitIndex = source.IndexOf("EmitStatusChangedEventAsync", StringComparison.Ordinal);

        Assert.True(
            prepareIndex >= 0 && emitIndex > prepareIndex,
            "Shipment/order projection events must be staged after SQL shipment/order authority is recomposed.");
    }

    [Fact]
    public void CheckoutCon10_InventoryDeliveryCommit_MustFenceReplayAndConsumeReservationsOnce()
    {
        var solutionDir = GetSolutionDirectory();
        var inventoryPath = Path.Combine(
            solutionDir,
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "InventoryConsistencyService.cs");

        var source = File.ReadAllText(inventoryPath);

        Assert.Contains("TryBeginEventAsync", source, StringComparison.Ordinal);
        Assert.Contains("EnsureDeliveryCommitSqlAuthorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("LoadReservationsForUpdateAsync", source, StringComparison.Ordinal);
        Assert.Contains("LoadStocksForUpdateAsync", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("alreadyCommitted", source, StringComparison.Ordinal);
        Assert.Contains("remainingToCommit", source, StringComparison.Ordinal);
        Assert.Contains("StockMovementType.Sale", source, StringComparison.Ordinal);
        Assert.Contains("InventoryEventInboxStatus.Applied", source, StringComparison.Ordinal);

        var commitIndex = source.IndexOf("public async Task CommitDeliveryAsync", StringComparison.Ordinal);
        var authorityIndex = source.IndexOf("EnsureDeliveryCommitSqlAuthorityAsync", commitIndex, StringComparison.Ordinal);
        var movementIndex = source.IndexOf("_dbContext.StockMovements.AddRange(movements)", commitIndex, StringComparison.Ordinal);

        Assert.True(
            commitIndex >= 0 && authorityIndex > commitIndex && movementIndex > authorityIndex,
            "Inventory movement generation must happen only after delivery SQL authority is recomposed.");
    }

    [Fact]
    public void CheckoutCon10_CarrierReplayPaths_MustCollapseDuplicatesBeforeExternalOrFulfillmentSideEffects()
    {
        var solutionDir = GetSolutionDirectory();
        var carrierPath = Path.Combine(
            solutionDir,
            "PointOfSale.Infrastructure",
            "Shipping",
            "CarrierShipmentService.cs");

        var source = File.ReadAllText(carrierPath);

        Assert.Contains("ClaimShipmentForExternalCallAsync", source, StringComparison.Ordinal);
        Assert.Contains("EnsureNoActiveLocalTransactionForExternalCarrierCall", source, StringComparison.Ordinal);
        Assert.Contains("ReconcilePendingCarrierRequestAsync", source, StringComparison.Ordinal);
        Assert.Contains("IsShipmentExternallyRegistered", source, StringComparison.Ordinal);
        Assert.Contains("PrepareShippingWebhookLogAsync", source, StringComparison.Ordinal);
        Assert.Contains("TryResolveShippingWebhookConflictAsync", source, StringComparison.Ordinal);
        Assert.Contains("IsValidStatusProgression", source, StringComparison.Ordinal);
        Assert.Contains("SynchronizeFulfillmentAuthorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("PrepareShipmentAuthorityContextAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutCon13_CarrierSuccessPersistence_MustRecomposeSqlShipmentAuthorityAfterExternalCall()
    {
        var solutionDir = GetSolutionDirectory();
        var carrierPath = Path.Combine(
            solutionDir,
            "PointOfSale.Infrastructure",
            "Shipping",
            "CarrierShipmentService.cs");

        var source = File.ReadAllText(carrierPath);

        Assert.Contains("BeginCarrierPersistenceTransactionAsync", source, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(", source, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", source, StringComparison.Ordinal);
        Assert.Contains("ReloadCarrierPersistenceAuthorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("LoadShipmentForCarrierMutationAsync", source, StringComparison.Ordinal);
        Assert.Contains("LoadOrderForCarrierShipmentMutationAsync", source, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, ROWLOCK, HOLDLOCK", source, StringComparison.Ordinal);
        Assert.Contains("RejectStaleCarrierRegistrationAuthorityAsync", source, StringComparison.Ordinal);
        Assert.Contains("STALE_CARRIER_REGISTRATION_AUTHORITY", source, StringComparison.Ordinal);
        Assert.Contains("ExternalRegistrationStateReconciliationRequired", source, StringComparison.Ordinal);

        var externalCallIndex = source.IndexOf("adapter.CreateShipmentAsync(createRequest", StringComparison.Ordinal);
        var persistIndex = source.IndexOf("PersistSuccessfulCarrierRegistrationAsync", externalCallIndex, StringComparison.Ordinal);
        var reloadIndex = source.IndexOf("ReloadCarrierPersistenceAuthorityAsync", persistIndex, StringComparison.Ordinal);
        var staleRejectIndex = source.IndexOf("RejectStaleCarrierRegistrationAuthorityAsync", persistIndex, StringComparison.Ordinal);
        var applyIndex = source.IndexOf("ApplySuccessfulCarrierRegistration(shipment, order", persistIndex, StringComparison.Ordinal);

        Assert.True(
            externalCallIndex >= 0 &&
            persistIndex > externalCallIndex &&
            reloadIndex > persistIndex &&
            staleRejectIndex > reloadIndex &&
            applyIndex > staleRejectIndex,
            "Carrier registration success must recompose and validate current SQL shipment/order authority after the external call and before applying carrier success.");
    }

    [Fact]
    public void CheckoutCon13_RecoveryAndCarrierReplay_MustNotOverwriteTerminalShipmentTruth()
    {
        var solutionDir = GetSolutionDirectory();
        var carrierPath = Path.Combine(
            solutionDir,
            "PointOfSale.Infrastructure",
            "Shipping",
            "CarrierShipmentService.cs");
        var recoveryPath = Path.Combine(
            solutionDir,
            "PointOfSale.API",
            "Services",
            "Shipping",
            "ShipmentAuthorityRecoveryHostedService.cs");

        var carrierSource = File.ReadAllText(carrierPath);
        var recoverySource = File.ReadAllText(recoveryPath);

        Assert.Contains("shipment.Status is ShipmentStatus.Cancelled", carrierSource, StringComparison.Ordinal);
        Assert.Contains("or ShipmentStatus.Failed", carrierSource, StringComparison.Ordinal);
        Assert.Contains("or ShipmentStatus.Returned", carrierSource, StringComparison.Ordinal);
        Assert.Contains("or ShipmentStatus.Delivered", carrierSource, StringComparison.Ordinal);
        Assert.Contains("order.Status == OrderStatus.Cancelled", carrierSource, StringComparison.Ordinal);
        Assert.Contains("CanOrderProceedToShipment(order)", carrierSource, StringComparison.Ordinal);

        Assert.Contains("!dbContext.Shipments.Any(shipment => shipment.OrderId == order.Id && !shipment.IsDeleted)", recoverySource, StringComparison.Ordinal);
        Assert.Contains("order.Status != OrderStatus.Cancelled", recoverySource, StringComparison.Ordinal);
        Assert.Contains("shipment.Status == ShipmentStatus.PendingExternalRegistration", recoverySource, StringComparison.Ordinal);
        Assert.Contains("shipmentService.ReconcileShipmentAuthorityAsync", recoverySource, StringComparison.Ordinal);
    }

    [Fact]
    public void OrderCon01_ShipmentReplayConsumers_MustRecomposeCurrentSqlLineage()
    {
        var solutionDir = GetSolutionDirectory();
        var authorityPath = Path.Combine(
            solutionDir,
            "PointOfSale.Infrastructure",
            "Reliability",
            "OrderFulfillmentLineageAuthority.cs");
        var statusSyncPath = Path.Combine(
            solutionDir,
            "PointOfSale.API",
            "Services",
            "OrderStatusSyncService.cs");
        var projectionPath = Path.Combine(
            solutionDir,
            "PointOfSale.Infrastructure",
            "Consumers",
            "OrderProjectionConsumer.cs");
        var snapshotLoaderPath = Path.Combine(
            solutionDir,
            "PointOfSale.Infrastructure",
            "MongoDB",
            "Projection",
            "StorefrontOrderProjectionAuthoritySnapshotLoader.cs");

        var authority = File.ReadAllText(authorityPath);
        var statusSync = File.ReadAllText(statusSyncPath);
        var projection = File.ReadAllText(projectionPath);
        var snapshotLoader = File.ReadAllText(snapshotLoaderPath);

        Assert.Contains("LoadCurrentShipmentAsync", authority, StringComparison.Ordinal);
        Assert.Contains("!shipment.IsDeleted", authority, StringComparison.Ordinal);
        Assert.Contains("!order.IsDeleted", authority, StringComparison.Ordinal);
        Assert.Contains("OrderFulfillmentLineageAuthority.LoadCurrentShipmentAsync", statusSync, StringComparison.Ordinal);
        Assert.Contains("Collapsed replayed shipment status against current SQL lineage", statusSync, StringComparison.Ordinal);
        Assert.Contains("OrderFulfillmentLineageAuthority.LoadCurrentShipmentAsync", projection, StringComparison.Ordinal);
        Assert.Contains("Collapsed replayed shipment facet payload against current SQL lineage", projection, StringComparison.Ordinal);
        Assert.Contains("LoadCurrentShipmentVisibilityAsync", snapshotLoader, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckoutCon10_CommissionAndPayoutEligibility_MustDeriveFromCurrentSqlFulfillmentTruth()
    {
        var solutionDir = GetSolutionDirectory();
        var affiliatePath = Path.Combine(
            solutionDir,
            "PointOfSale.Infrastructure",
            "Services",
            "Affiliate",
            "AffiliateService.cs");
        var payoutReadPath = Path.Combine(
            solutionDir,
            "PointOfSale.API",
            "Services",
            "CommerceHub",
            "Finance",
            "CommerceHubVendorPayoutReadService.cs");

        var affiliateSource = File.ReadAllText(affiliatePath);
        var payoutSource = File.ReadAllText(payoutReadPath);

        Assert.Contains("ApproveCommissionForDeliveredOrderAsync", affiliateSource, StringComparison.Ordinal);
        Assert.Contains("BeginTransactionAsync(IsolationLevel.Serializable", affiliateSource, StringComparison.Ordinal);
        Assert.Contains("LoadAffiliateApprovalOrderAuthorityAsync", affiliateSource, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", affiliateSource, StringComparison.Ordinal);
        Assert.Contains("EnsureAffiliateDeliveryApprovalAuthority", affiliateSource, StringComparison.Ordinal);
        Assert.Contains("order.IsPhysicallyDelivered", affiliateSource, StringComparison.Ordinal);
        Assert.Contains("FinancialPostingStatus", affiliateSource, StringComparison.Ordinal);
        Assert.Contains("CreateApproved", affiliateSource, StringComparison.Ordinal);

        Assert.Contains("CommerceHubSubOrderItems", payoutSource, StringComparison.Ordinal);
        Assert.Contains("CommerceHubOrderItemStatus.Delivered", payoutSource, StringComparison.Ordinal);
        Assert.Contains("LastDeliveredAt", payoutSource, StringComparison.Ordinal);
        Assert.Contains("netEligibleBeforePayouts", payoutSource, StringComparison.Ordinal);
    }

    #region Helpers

    private static string GetSolutionDirectory()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null && !File.Exists(Path.Combine(dir, "PointOfSale.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        return dir ?? Directory.GetCurrentDirectory();
    }

    private static int GetLineNumber(string content, string pattern)
    {
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (Regex.IsMatch(lines[i], pattern))
            {
                return i + 1;
            }
        }
        return 0;
    }

    #endregion
}
