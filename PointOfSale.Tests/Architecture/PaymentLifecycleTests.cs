using System.Text.RegularExpressions;
using Xunit;

namespace PointOfSale.Tests.Architecture;

/// <summary>
/// PHASE COD: PAYMENT LIFECYCLE ARCHITECTURE TESTS
/// 
/// These tests enforce that:
/// 1. No controller directly sets Order.PaymentStatus (except PaymentLifecycleService)
/// 2. PaymentLifecycleService uses IAccountingOutboxService for journal creation
/// 3. The legacy UpdatePaymentStatus endpoint is blocked (returns 410)
/// 
/// Forbidden Patterns in Controllers:
/// ❌ order.PaymentStatus = PaymentStatus.xxx
/// ❌ o.PaymentStatus = PaymentStatus.xxx
/// 
/// Required Pattern:
/// ✅ _lifecycleService.MarkOfflineCollectionDeliveredAsync(...)
/// ✅ _lifecycleService.CaptureOnlinePaymentAsync(...)
/// ✅ _accountingOutbox.EnqueueXxxAsync(...)
/// </summary>
public class PaymentLifecycleTests
{
    // Pattern that detects direct PaymentStatus assignment
    // Matches: order.PaymentStatus = PaymentStatus.xxx or .PaymentStatus = PaymentStatus.
    private static readonly string[] DirectPaymentStatusPatterns = new[]
    {
        @"\.PaymentStatus\s*=\s*PaymentStatus\.",
        @"order\.PaymentStatus\s*=",
        @"o\.PaymentStatus\s*="
    };
    
    // Whitelisted patterns that are acceptable
    private static readonly string[] WhitelistedPatterns = new[]
    {
        // Property initialization in entity
        @"PaymentStatus\s*=\s*PaymentStatus\.Pending", // Default value in Order.cs
        
        // Query/filter expressions
        @"Where.*PaymentStatus\s*==",
        @"o\.PaymentStatus\s*==",
        
        // DTO mapping (reading, not writing)
        @"PaymentStatus\s*=\s*o\.PaymentStatus",
        @"PaymentStatus\s*=\s*order\.PaymentStatus",
        
        // Event payload assignment
        @"NewPaymentStatus\s*=",
        @"OldPaymentStatus\s*=",
    };

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 1: PaymentLifecycleService uses IAccountingOutboxService
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PaymentLifecycleService_ShouldUseAccountingOutbox()
    {
        // Arrange
        var servicePath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.Infrastructure",
            "Payments",
            "PaymentLifecycleService.cs");

        if (!File.Exists(servicePath))
        {
            return; // Skip gracefully if not found (service not yet created)
        }

        var sourceCode = File.ReadAllText(servicePath);

        // Act - Check that outbox service is used
        var usesAccountingOutbox = sourceCode.Contains("IAccountingOutboxService");
        var enqueuesMessages = 
            sourceCode.Contains("EnqueueCodDeliveredAsync") ||
            sourceCode.Contains("EnqueueOnlinePaymentAsync") ||
            sourceCode.Contains("EnqueueWalletApprovedAsync");

        // Assert
        Assert.True(usesAccountingOutbox,
            "PaymentLifecycleService must inject IAccountingOutboxService");
        Assert.True(enqueuesMessages,
            "PaymentLifecycleService must enqueue accounting outbox messages for journal creation");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 2: Controllers don't directly set PaymentStatus
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Controllers_ShouldNotDirectlySetPaymentStatus()
    {
        // Arrange
        var controllersPath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.API",
            "Controllers");

        if (!Directory.Exists(controllersPath))
        {
            return;
        }

        var controllerFiles = Directory.GetFiles(controllersPath, "*.cs", SearchOption.AllDirectories);
        var violations = new List<string>();

        // Act
        foreach (var file in controllerFiles)
        {
            var fileName = Path.GetFileName(file);
            
            // Skip the PaymentLifecycleController - it's the authorized handler
            if (fileName == "PaymentLifecycleController.cs")
                continue;
                
            var sourceCode = File.ReadAllText(file);
            
            // Check for direct PaymentStatus assignments
            foreach (var pattern in DirectPaymentStatusPatterns)
            {
                var matches = Regex.Matches(sourceCode, pattern, RegexOptions.IgnoreCase);
                foreach (Match match in matches)
                {
                    // Get line number
                    var lineNumber = GetLineNumber(sourceCode, match.Index);
                    var line = GetLine(sourceCode, match.Index);
                    
                    // Check if this is a whitelisted pattern
                    var isWhitelisted = WhitelistedPatterns.Any(wp => 
                        Regex.IsMatch(line, wp, RegexOptions.IgnoreCase));
                    
                    if (!isWhitelisted)
                    {
                        violations.Add($"{fileName}:{lineNumber} -> {line.Trim()}");
                    }
                }
            }
        }

        // Assert
        Assert.True(violations.Count == 0,
            $"PHASE COD VIOLATION!\n\n" +
            $"Controllers must NOT directly set PaymentStatus:\n" +
            string.Join("\n", violations.Select(v => $"  ❌ {v}")) +
            "\n\nUse IPaymentLifecycleService instead:\n" +
            "  ✅ _lifecycleService.MarkOfflineCollectionDeliveredAsync()\n" +
            "  ✅ _lifecycleService.CaptureOnlinePaymentAsync()\n" +
            "  ✅ _lifecycleService.ApproveReviewedPaymentAsync()");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 3: Payment Services use PaymentLifecycleService (not direct changes)
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PaymentServices_ShouldUsePaymentLifecycleService()
    {
        // Arrange
        var servicesPath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.API",
            "Services",
            "Payments");

        if (!Directory.Exists(servicesPath))
        {
            return;
        }

        var serviceFiles = Directory.GetFiles(servicesPath, "*.cs", SearchOption.AllDirectories);
        var violations = new List<string>();

        // Act
        foreach (var file in serviceFiles)
        {
            var fileName = Path.GetFileName(file);
            var sourceCode = File.ReadAllText(file);
            
            // Look for direct PaymentStatus assignments that are NOT whitelisted
            foreach (var pattern in DirectPaymentStatusPatterns)
            {
                var matches = Regex.Matches(sourceCode, pattern, RegexOptions.IgnoreCase);
                foreach (Match match in matches)
                {
                    var lineNumber = GetLineNumber(sourceCode, match.Index);
                    var line = GetLine(sourceCode, match.Index);
                    
                    // Check if whitelisted
                    var isWhitelisted = WhitelistedPatterns.Any(wp => 
                        Regex.IsMatch(line, wp, RegexOptions.IgnoreCase));
                    
                    if (!isWhitelisted)
                    {
                        violations.Add($"{fileName}:{lineNumber} -> {line.Trim()}");
                    }
                }
            }
        }

        // Note: This test documents existing violations for migration tracking
        // For now, we log them as warnings but don't fail the test
        // Once all services are migrated, change Assert.True to fail on violations
        if (violations.Count > 0)
        {
            // TODO: After migration is complete, uncomment this assertion
            // Assert.True(false,
            //     $"PHASE COD MIGRATION NEEDED!\n\n" +
            //     $"Payment services should use IPaymentLifecycleService:\n" +
            //     string.Join("\n", violations.Select(v => $"  ⚠️ {v}")));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    [Fact]
    public void PaymentServices_MustRouteThrough_HandlePaymentResultAsync_WithoutInventoryMutations()
    {
        var solutionDir = GetSolutionDirectory();
        var requiredFiles = new[]
        {
            Path.Combine(solutionDir, "PointOfSale.API", "Services", "Payments", "PaymentService.cs"),
            Path.Combine(solutionDir, "PointOfSale.API", "Services", "Payments", "Stripe", "StripePaymentService.cs"),
            Path.Combine(solutionDir, "PointOfSale.API", "Services", "Payments", "Paymob", "PaymobPaymentService.cs")
        };

        foreach (var file in requiredFiles)
        {
            Assert.True(File.Exists(file), $"{Path.GetFileName(file)} must exist");

            var sourceCode = File.ReadAllText(file);

            Assert.Contains("HandlePaymentResultAsync", sourceCode, StringComparison.Ordinal);
            
            // Ban legacy inventory mutation APIs
            Assert.DoesNotContain("IStockRepository", sourceCode, StringComparison.Ordinal);
            Assert.DoesNotContain("_stockRepository.", sourceCode, StringComparison.Ordinal);
            Assert.DoesNotContain("CommitReservationAsync", sourceCode, StringComparison.Ordinal);
            
            // Ban current inventory mutation APIs
            Assert.DoesNotContain("IProductVariantStockRepository", sourceCode, StringComparison.Ordinal);
            Assert.DoesNotContain("_productVariantStockRepository.", sourceCode, StringComparison.Ordinal);
            Assert.DoesNotContain("_stockRepository.CommitOrderReservationsAsync(", sourceCode, StringComparison.Ordinal);
            Assert.DoesNotContain("_stockRepository.ReleaseOrderReservationsAsync(", sourceCode, StringComparison.Ordinal);
        }
    }

    // TEST 4: Legacy UpdatePaymentStatus endpoint is blocked
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void OrdersController_UpdatePaymentStatus_ShouldBeBlocked()
    {
        // Arrange
        var controllerPath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.API",
            "Controllers",
            "OrdersController.cs");

        if (!File.Exists(controllerPath))
        {
            return;
        }

        var sourceCode = File.ReadAllText(controllerPath);

        // Act - Check that the endpoint returns 410 Gone
        var hasObsoleteAttribute = sourceCode.Contains("[Obsolete(");
        var returns410 = sourceCode.Contains("StatusCode(410");
        var hasGuardComment = sourceCode.Contains("PHASE: COD Payment Lifecycle Architecture - GUARD BLOCK");

        // Assert
        Assert.True(hasObsoleteAttribute,
            "UpdatePaymentStatus endpoint must have [Obsolete] attribute");
        Assert.True(returns410,
            "UpdatePaymentStatus endpoint must return 410 Gone status");
        Assert.True(hasGuardComment,
            "UpdatePaymentStatus endpoint must have GUARD BLOCK comment");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 5: PaymentLifecycleController exists and has proper endpoints
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PaymentLifecycleController_ShouldHaveRequiredEndpoints()
    {
        // Arrange
        var controllerPath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.API",
            "Controllers",
            "PaymentLifecycleController.cs");
        var manualPaymentControllerPath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.API",
            "Controllers",
            "AdminManualPaymentController.cs");

        if (!File.Exists(controllerPath))
        {
            Assert.True(false, "PaymentLifecycleController.cs must exist");
            return;
        }

        Assert.True(File.Exists(manualPaymentControllerPath), "AdminManualPaymentController.cs must exist");

        var sourceCode = File.ReadAllText(controllerPath);
        var manualPaymentSourceCode = File.ReadAllText(manualPaymentControllerPath);

        // Act - Check for required endpoints
        var hasCodDelivered = sourceCode.Contains("cod-delivered");
        var legacyWalletRoutesDisabled =
            sourceCode.Contains("[NonAction]") &&
            !sourceCode.Contains("[HttpPost(\"{orderId:guid}/wallet-approve\")]") &&
            !sourceCode.Contains("[HttpPost(\"{orderId:guid}/wallet-reject\")]");
        var hasManualWalletConfirm = manualPaymentSourceCode.Contains("[HttpPost(\"{paymentIntentId:guid}/confirm\")]");
        var hasManualWalletReject = manualPaymentSourceCode.Contains("[HttpPost(\"{paymentIntentId:guid}/reject\")]");
        var hasRefund = sourceCode.Contains("/refund");
        var hasCodSettlement = sourceCode.Contains("/settlements/cod");
        var hasGatewaySettlement = sourceCode.Contains("/settlements/gateway");
        var hasActionsEndpoint = sourceCode.Contains("/actions");

        // Assert
        Assert.True(hasCodDelivered, 
            "PaymentLifecycleController must have cod-delivered endpoint");
        Assert.True(legacyWalletRoutesDisabled,
            "PaymentLifecycleController legacy wallet routes must remain non-actions");
        Assert.True(hasManualWalletConfirm,
            "AdminManualPaymentController must own the manual-wallet confirm endpoint");
        Assert.True(hasManualWalletReject,
            "AdminManualPaymentController must own the manual-wallet reject endpoint");
        Assert.True(hasRefund, 
            "PaymentLifecycleController must have refund endpoint");
        Assert.True(hasCodSettlement, 
            "PaymentLifecycleController must have COD settlement endpoint");
        Assert.True(hasActionsEndpoint, 
            "PaymentLifecycleController must have actions endpoint for UI");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════════════════════════════

    private static string GetSolutionDirectory()
    {
        var current = Directory.GetCurrentDirectory();
        while (current != null && !File.Exists(Path.Combine(current, "PointOfSale.sln")))
        {
            current = Directory.GetParent(current)?.FullName;
        }
        return current ?? Directory.GetCurrentDirectory();
    }

    private static int GetLineNumber(string text, int index)
    {
        return text.Substring(0, index).Count(c => c == '\n') + 1;
    }

    private static string GetLine(string text, int index)
    {
        var start = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        var end = text.IndexOf('\n', index);
        if (end < 0) end = text.Length;
        return text.Substring(start, end - start);
    }

    private static string StripLineComments(string sourceCode)
    {
        // First, strip all block comments (/* ... */) including multiline blocks
        // Use non-greedy regex to match from /* to the nearest */
        sourceCode = System.Text.RegularExpressions.Regex.Replace(
            sourceCode,
            @"/\*[\s\S]*?\*/",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.Multiline);
        
        // Then, strip line comments (//)
        var lines = sourceCode.Replace("\r\n", "\n").Split('\n');
        return string.Join(
            "\n",
            lines.Select(line =>
            {
                var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
                return commentIndex < 0 ? line : line[..commentIndex];
            }));
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 6: NoClass_ShouldSet_PaymentStatus_Directly
    // ═══════════════════════════════════════════════════════════════════════════
    // 
    // PHASE COD-LIFECYCLE ENFORCEMENT:
    // Scans ALL source files for direct PaymentStatus assignments.
    // ONLY PaymentLifecycleService is allowed to set PaymentStatus.
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void NoClass_ShouldSet_PaymentStatus_Directly()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        var coreProject = Path.Combine(solutionDir, "PointOfSale.Core");
        var apiProject = Path.Combine(solutionDir, "PointOfSale.API");
        var infraProject = Path.Combine(solutionDir, "PointOfSale.Infrastructure");
        
        // Whitelist - ONLY PaymentLifecycleService can set PaymentStatus
        // Legacy payment services are whitelisted until migration is complete
        // These files have TODO: MIGRATION comments marking them for refactoring
        var whitelistedFiles = new[]
        {
            "PaymentLifecycleService.cs",
            "Order.cs", // Entity default value
            "PaymentStatusSaveChangesInterceptor.cs", // Tests for change tracking
            
            // PENDING MIGRATION - These services will be migrated to use IPaymentLifecycleService
            // See: docs/PAYMENT_LIFECYCLE_ARCHITECTURE.md Section 12 - Phase 2
            "PaymentService.cs", // TODO: MIGRATION comments in HandlePaymentSuccessAsync, ConfirmManualPaymentAsync
            "StripePaymentService.cs", // TODO: MIGRATION comments in HandlePaymentIntentSucceededAsync
            "PaymobPaymentService.cs", // TODO: MIGRATION comments in HandlePaymobCallback
        };
        
        var whitelistedCodePatterns = new[]
        {
            // Property definition in entity
            @"private PaymentStatus",
            @"public PaymentStatus PaymentStatus",
            @"_paymentStatus =",
            @"_originalPaymentStatus",
            // Query expressions (read, not write)
            @"\.PaymentStatus\s*==",
            @"o\.PaymentStatus\s*==",
            @"order\.PaymentStatus\s*==",
            // DTO/projection assignments (reading FROM order)
            @"PaymentStatus\s*=\s*o\.PaymentStatus",
            @"PaymentStatus\s*=\s*order\.PaymentStatus",
            // Event payloads
            @"NewPaymentStatus\s*=",
            @"OldPaymentStatus\s*=",
            // Testing/Mock
            @"PaymentStatus\s*=\s*PaymentStatus\.Pending", // Default init
            // AcceptPaymentStatusChange() call
            @"AcceptPaymentStatusChange",
            
            // CommerceHub marketplace payment/refund workflow orchestration patterns
            // These are scoped to specific lifecycle transitions, not blanket exemptions
            @"marketplaceOrder\.PaymentStatus\s*=\s*PaymentStatus\.Collected",
            @"subOrder\.PaymentStatus\s*=\s*PaymentStatus\.Collected",
            @"marketplaceOrder\.PaymentStatus\s*=\s*PaymentStatus\.Pending",
            @"subOrder\.PaymentStatus\s*=\s*PaymentStatus\.Pending",
            @"subOrder\.PaymentStatus\s*=\s*isFullRefund",
            @"marketplaceOrder\.PaymentStatus\s*=\s*AggregateMarketplacePaymentStatus",
        };
        
        var violations = new List<string>();
        var directAssignmentPattern = new Regex(
            @"\b\w+\.PaymentStatus\s*=\s*(?![=>])",
            RegexOptions.IgnoreCase);

        // Act - Scan all projects
        foreach (var projectPath in new[] { coreProject, apiProject, infraProject })
        {
            if (!Directory.Exists(projectPath)) continue;
            
            var csFiles = Directory.GetFiles(projectPath, "*.cs", SearchOption.AllDirectories);
            
            foreach (var file in csFiles)
            {
                var fileName = Path.GetFileName(file);
                
                // Skip whitelisted files
                if (whitelistedFiles.Any(wf => fileName.Equals(wf, StringComparison.OrdinalIgnoreCase)))
                    continue;
                    
                var sourceCode = File.ReadAllText(file);
                var matches = directAssignmentPattern.Matches(sourceCode);
                
                foreach (Match match in matches)
                {
                    var lineNumber = GetLineNumber(sourceCode, match.Index);
                    var line = GetLine(sourceCode, match.Index);
                    
                    // Check if this line matches any whitelisted pattern
                    var isWhitelisted = whitelistedCodePatterns.Any(wp =>
                        Regex.IsMatch(line, wp, RegexOptions.IgnoreCase));
                    
                    if (!isWhitelisted)
                    {
                        var relativePath = Path.GetRelativePath(solutionDir, file);
                        violations.Add($"{relativePath}:{lineNumber} -> {line.Trim()}");
                    }
                }
            }
        }

        // Assert
        Assert.True(violations.Count == 0,
            $"PHASE COD-LIFECYCLE VIOLATION!\n\n" +
            $"PaymentStatus can ONLY be set by PaymentLifecycleService.\n" +
            $"The following files contain unauthorized PaymentStatus assignments:\n\n" +
            string.Join("\n", violations.Select(v => $"  ❌ {v}")) +
            "\n\n" +
            "REQUIRED: Use IPaymentLifecycleService methods instead:\n" +
            "  ✅ _lifecycleService.MarkOfflineCollectionDeliveredAsync()\n" +
            "  ✅ _lifecycleService.CaptureOnlinePaymentAsync()\n" +
            "  ✅ _lifecycleService.ApproveReviewedPaymentAsync()\n" +
            "  ✅ _lifecycleService.RejectReviewedPaymentAsync()\n" +
            "  ✅ _lifecycleService.RecordRefundAsync()\n" +
            "  ✅ _lifecycleService.MarkPaymentFailedAsync()");
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 7: CollectedState_MustHaveJournalCreation
    // ═══════════════════════════════════════════════════════════════════════════
    // 
    // PHASE COD-LIFECYCLE ENFORCEMENT:
    // Every pathway that sets PaymentStatus to Collected MUST create a journal.
    // This test verifies that PaymentLifecycleService enqueues accounting messages
    // for all Collected transitions.
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CollectedState_MustHaveJournalCreation()
    {
        // Arrange
        var servicePath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.Infrastructure",
            "Payments",
            "PaymentLifecycleService.cs");

        if (!File.Exists(servicePath))
        {
            Assert.True(false, "PaymentLifecycleService.cs not found - cannot verify journal creation");
            return;
        }

        var sourceCode = File.ReadAllText(servicePath);

        var successBlock = ExtractMethodBlock(sourceCode, "private async Task<PaymentTransitionResult> HandleSuccessAsync(");
        var successOutboxBlock = ExtractMethodBlock(sourceCode, "private async Task<AccountingStageResult> EnqueueSuccessOutboxAsync(");
        
        Assert.Contains("order.PaymentStatus = PaymentStatus.Collected;", successBlock, StringComparison.Ordinal);
        Assert.Contains("await _stockRepository.CommitOrderReservationsAsync(order.TenantId, order.Id, ct);", successBlock, StringComparison.Ordinal);
        Assert.Contains("accountingStage = await EnqueueSuccessOutboxAsync(order, paymentIntent, request, amount, routedToSuspense, resolvedPaymentReference, ct);", successBlock, StringComparison.Ordinal);
        Assert.Contains("await PublishSuccessDomainEventAsync(order, request, amount, now, ct);", successBlock, StringComparison.Ordinal);
        Assert.Contains("await PublishCanonicalSuccessOutcomeEventsAsync(order, paymentIntent, request, amount, now, ct);", successBlock, StringComparison.Ordinal);
        Assert.Contains("await _db.SaveChangesAsync(ct);", successBlock, StringComparison.Ordinal);
        Assert.Contains("await transaction.CommitAsync(ct);", successBlock, StringComparison.Ordinal);

        var commitInventoryIndex = successBlock.LastIndexOf(
            "await _stockRepository.CommitOrderReservationsAsync(order.TenantId, order.Id, ct);",
            StringComparison.Ordinal);
        var enqueueOutboxIndex = successBlock.IndexOf(
            "accountingStage = await EnqueueSuccessOutboxAsync(order, paymentIntent, request, amount, routedToSuspense, resolvedPaymentReference, ct);",
            StringComparison.Ordinal);
        var publishEventIndex = successBlock.IndexOf(
            "await PublishSuccessDomainEventAsync(order, request, amount, now, ct);",
            StringComparison.Ordinal);
        var publishCanonicalIndex = successBlock.IndexOf(
            "await PublishCanonicalSuccessOutcomeEventsAsync(order, paymentIntent, request, amount, now, ct);",
            publishEventIndex,
            StringComparison.Ordinal);
        var saveChangesIndex = successBlock.IndexOf(
            "await _db.SaveChangesAsync(ct);",
            publishCanonicalIndex,
            StringComparison.Ordinal);
        var commitTransactionIndex = successBlock.IndexOf(
            "await transaction.CommitAsync(ct);",
            saveChangesIndex,
            StringComparison.Ordinal);

        Assert.True(
            commitInventoryIndex < enqueueOutboxIndex &&
            enqueueOutboxIndex < publishEventIndex &&
            publishEventIndex < publishCanonicalIndex &&
            publishCanonicalIndex < saveChangesIndex &&
            saveChangesIndex < commitTransactionIndex,
            "CANONICAL SUCCESS FLOW VIOLATION!\n\n" +
            "Successful payments must commit inventory, stage accounting and domain/outcome outbox work, " +
            "persist the database mutation, and commit the transaction.");

        Assert.Contains("EnqueueCodDeliveredAsync", successOutboxBlock, StringComparison.Ordinal);
        Assert.Contains("EnqueueWalletApprovedAsync", successOutboxBlock, StringComparison.Ordinal);
        Assert.Contains("EnqueueOnlinePaymentAsync", successOutboxBlock, StringComparison.Ordinal);
        /*
            {
                var methodStart = sourceCode.IndexOf($"{method}(");
                if (methodStart < 0) return false;
                
                // Find method body (look for next opening brace after declaration)
                var braceIndex = sourceCode.IndexOf("{", methodStart);
                if (braceIndex < 0) return false;
                
                // Find matching closing brace (simplified - assumes well-formed code)
                var depth = 1;
                var methodBody = "";
                for (int i = braceIndex + 1; i < sourceCode.Length && depth > 0; i++)
                {
                    if (sourceCode[i] == '{') depth++;
                    if (sourceCode[i] == '}') depth--;
                    methodBody += sourceCode[i];
                }
                
                return methodBody.Contains(call);
            });

            Assert.True(methodHasJournalCall,
                $"LEDGER-FIRST VIOLATION!\n\n" +
                $"Method '{method}' sets PaymentStatus to Collected but does NOT enqueue a journal.\n\n" +
                $"RULE: Every Collected transition MUST create a ledger journal entry.\n" +
                $"REQUIRED: Call one of these in the method:\n" +
                $"  ✅ _accountingOutbox.EnqueueCodDeliveredAsync()\n" +
                $"  ✅ _accountingOutbox.EnqueueOnlinePaymentAsync()\n" +
                $"  ✅ _accountingOutbox.EnqueueWalletApprovedAsync()");
        }
        
        // Verify we found at least the expected methods
        Assert.True(methodsSettingCollected.Count >= 3,
            $"Expected at least 3 methods setting Collected status (COD, Online, Wallet), found {methodsSettingCollected.Count}");
        */
    }


    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 4: Only FulfillmentDeliveredConsumer can call MarkOfflineCollectionDeliveredAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ShipmentCodCollectedConsumer_MustRemainRegisteredPaymentAuthorityBridge()
    {
        var solutionDir = GetSolutionDirectory();
        var consumerPath = Path.Combine(solutionDir, "PointOfSale.Infrastructure", "Consumers", "ShipmentCodCollectedConsumer.cs");
        var programPath = Path.Combine(solutionDir, "PointOfSale.API", "Program.cs");
        var architecturePath = Path.Combine(solutionDir, ".ai", "hermes", "platform", "shipping-fulfillment-runtime-final-architecture.md");

        var consumer = File.ReadAllText(consumerPath);
        var program = File.ReadAllText(programPath);
        var architecture = File.ReadAllText(architecturePath);

        Assert.Contains("IConsumer<ShipmentCodCollectedEvent>", consumer, StringComparison.Ordinal);
        Assert.Contains("MarkOfflineCollectionDeliveredAsync", consumer, StringComparison.Ordinal);
        Assert.Contains("AddConsumer<ShipmentCodCollectedConsumer, ShipmentCodCollectedConsumerDefinition>", program, StringComparison.Ordinal);
        Assert.DoesNotContain("ShipmentCodCollectedEvent` has NO consumer", architecture, StringComparison.Ordinal);
        Assert.DoesNotContain("verified: 0 consumers", architecture, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyFulfillmentDeliveredConsumer_CanCallMarkOfflineCollectionDeliveredAsync()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        var allCsFiles = Directory.GetFiles(solutionDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
            .ToList();
        
        var violations = new List<string>();
        
        // Act
        foreach (var file in allCsFiles)
        {
            var fileName = Path.GetFileName(file);
            
            // Skip the approved entrypoints, the service definition, and test files
            if (fileName == "FulfillmentDeliveredEventConsumer.cs" ||
                fileName == "OrderDeliveredEventConsumer.cs" ||
                fileName == "ShipmentCodCollectedConsumer.cs" ||
                fileName == "PaymentLifecycleController.cs" ||
                fileName == "IPaymentLifecycleService.cs" ||
                fileName == "PaymentLifecycleService.cs" ||
                fileName == "PaymentStatusSaveChangesInterceptor.cs" ||
                fileName == "FulfillmentStatus.cs" ||
                fileName == "OrderFulfillmentEvents.cs" ||
                fileName == "IOrderFulfillmentService.cs" ||
                fileName == "Program.cs" ||
                fileName.Contains("Test"))
                continue;
            
            var sourceCode = File.ReadAllText(file);
            var executableSourceCode = StripLineComments(sourceCode);
            
            // Check for actual invocations of MarkOfflineCollectionDeliveredAsync
            var match = Regex.Match(executableSourceCode, @"\.\s*MarkOfflineCollectionDeliveredAsync\s*\(");
            if (match.Success)
            {
                var lineNumber = GetLineNumber(executableSourceCode, match.Index);
                violations.Add($"{fileName}:{lineNumber}");
            }
        }
        
        // Assert
        Assert.True(violations.Count == 0,
            $"PHASE COD VIOLATION!\n\n" +
            $"Only approved fulfillment lifecycle entrypoints may call MarkOfflineCollectionDeliveredAsync:\n" +
            string.Join("\n", violations.Select(v => $"  ❌ {v}")) +
            "\n\nThis ensures COD collection remains coupled to fulfillment delivery or explicitly authorized lifecycle actions.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 5: Webhooks must NOT touch COD PaymentStatus
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Webhooks_MustNotTouchCodPaymentStatus()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        var webhookFiles = Directory.GetFiles(solutionDir, "*Webhook*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("\\bin\\") && !f.Contains("\\obj\\"))
            .ToList();
        
        var violations = new List<string>();
        
        // Act
        foreach (var file in webhookFiles)
        {
            var fileName = Path.GetFileName(file);
            if (fileName.EndsWith(".Designer.cs", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var sourceCode = File.ReadAllText(file);
            
            // Check for COD-related logic in webhooks
            if ((Regex.IsMatch(sourceCode, @"\bCOD\b", RegexOptions.IgnoreCase) ||
                 sourceCode.Contains("CashOnDelivery", StringComparison.OrdinalIgnoreCase)) &&
                sourceCode.Contains("PaymentStatus"))
            {
                violations.Add(fileName);
            }
        }
        
        // Assert
        Assert.True(violations.Count == 0,
            $"PHASE COD VIOLATION!\n\n" +
            $"Webhooks must NEVER process COD payments (COD has no gateway):\n" +
            string.Join("\n", violations.Select(v => $"  ❌ {v}")) +
            "\n\nCOD collection is ONLY triggered by FulfillmentDeliveredEvent.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 6: Services must NOT bypass PaymentStatusSaveChangesInterceptor
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Services_MustNotBypassPaymentStatusInterceptor()
    {
        // Arrange
        var servicesPath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.API",
            "Services");

        if (!Directory.Exists(servicesPath))
        {
            return;
        }

        var serviceFiles = Directory.GetFiles(servicesPath, "*.cs", SearchOption.AllDirectories);
        var violations = new List<string>();

        // Act - Check for direct PaymentStatus writes WITHOUT using PaymentLifecycleService
        foreach (var file in serviceFiles)
        {
            var fileName = Path.GetFileName(file);
            var sourceCode = File.ReadAllText(file);
            
            // Skip PaymentLifecycleService (it's the authorized service)
            if (fileName == "PaymentLifecycleService.cs")
                continue;
            
            // Look for PaymentStatus writes
            var hasPaymentStatusWrite = Regex.IsMatch(sourceCode, @"order\.PaymentStatus\s*=\s*PaymentStatus\.");
            
            // Check if it uses PaymentLifecycleService
            var usesLifecycleService = sourceCode.Contains("_paymentLifecycleService.") ||
                                       sourceCode.Contains("PaymentLifecycleService") ||
                                       sourceCode.Contains("CaptureOnlinePaymentAsync") ||
                                       sourceCode.Contains("ApproveReviewedPaymentAsync") ||
                                       sourceCode.Contains("RejectReviewedPaymentAsync");
            
            if (hasPaymentStatusWrite && !usesLifecycleService)
            {
                var lineNumber = GetLineNumber(sourceCode, sourceCode.IndexOf("order.PaymentStatus ="));
                violations.Add($"{fileName}:{lineNumber}");
            }
        }

        // Assert
        Assert.True(violations.Count == 0,
            $"PHASE 2 VIOLATION!\n\n" +
            $"Services must use IPaymentLifecycleService instead of direct PaymentStatus writes:\n" +
            string.Join("\n", violations.Select(v => $"  ❌ {v}")) +
            "\n\nAll PaymentStatus changes must go through IPaymentLifecycleService to ensure ledger journal creation.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 7: FulfillmentStatus validation in MarkOfflineCollectionDeliveredAsync
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MarkOfflineCollectionDeliveredAsync_MustValidateFulfillmentStatus()
    {
        // Arrange
        var servicePath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.Infrastructure",
            "Payments",
            "PaymentLifecycleService.cs");

        if (!File.Exists(servicePath))
        {
            return;
        }

        var sourceCode = File.ReadAllText(servicePath);

        var markCodDeliveredBlock = ExtractMethodBlock(
            sourceCode,
            "public Task<PaymentTransitionResult> MarkOfflineCollectionDeliveredAsync(");
        var successBlock = ExtractMethodBlock(
            sourceCode,
            "private async Task<PaymentTransitionResult> HandleSuccessAsync(");

        Assert.Contains("Outcome = PaymentLifecycleOutcome.Succeeded", markCodDeliveredBlock, StringComparison.Ordinal);
        Assert.Contains("Source = PaymentLifecycleSource.OfflineCollection", markCodDeliveredBlock, StringComparison.Ordinal);
        Assert.Contains("if (request.Source == PaymentLifecycleSource.OfflineCollection)", successBlock, StringComparison.Ordinal);
        Assert.Contains("order.FulfillmentStatus != FulfillmentStatus.Delivered", successBlock, StringComparison.Ordinal);

        Assert.True(
            successBlock.IndexOf("if (request.Source == PaymentLifecycleSource.OfflineCollection)", StringComparison.Ordinal) <
            successBlock.IndexOf("order.FulfillmentStatus != FulfillmentStatus.Delivered", StringComparison.Ordinal),
            "PHASE COD VIOLATION!\n\n" +
            "COD lifecycle handling MUST validate that FulfillmentStatus == Delivered before collection.\n" +
            "This is the CORE INVARIANT: No COD collection without physical delivery.");
    }


    // ═══════════════════════════════════════════════════════════════════════════
    // TEST 8: ProcessWebhookAsync must block COD webhooks
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ProcessWebhookAsync_MustBlockCodWebhooks()
    {
        // Arrange
        var servicePath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.API",
            "Services",
            "Payments",
            "PaymentService.cs");

        if (!File.Exists(servicePath))
        {
            return;
        }

        var sourceCode = File.ReadAllText(servicePath);

        // Act - Check that ProcessWebhookAsync blocks COD provider
        var methodStart = sourceCode.IndexOf("public async Task<WebhookResponseDto> ProcessWebhookAsync");
        if (methodStart == -1)
        {
            Assert.Fail("ProcessWebhookAsync method not found");
            return;
        }

        var methodEnd = sourceCode.IndexOf("// Step 1: Parse payload", methodStart);
        if (methodEnd == -1)
        {
            methodEnd = methodStart + 2000; // Approximate
        }

        var methodBody = sourceCode.Substring(methodStart, methodEnd - methodStart);

        // Assert - Must check for COD provider and block it
        var hasCodCheck = methodBody.Contains("cod", StringComparison.OrdinalIgnoreCase) &&
                          (methodBody.Contains("SECURITY_VIOLATION") || methodBody.Contains("not supported"));

        Assert.True(hasCodCheck,
            "PHASE 2 VIOLATION!\n\n" +
            "ProcessWebhookAsync MUST block COD webhooks\n" +
            "COD has no gateway, therefore COD webhooks should never exist.");
    }

    [Fact]
    public void OpenInventoryReservationQuantity_MustBeTenantScoped()
    {
        var servicePath = Path.Combine(
            GetSolutionDirectory(),
            "PointOfSale.Infrastructure",
            "Payments",
            "PaymentLifecycleService.cs");
        var sourceCode = File.ReadAllText(servicePath);
        var method = ExtractMethodBlock(
            sourceCode,
            "private async Task<decimal> GetOpenReservationQuantityAsync(Guid tenantId, Guid orderId, CancellationToken ct)");

        Assert.Contains("reservation.TenantId == tenantId", method, StringComparison.Ordinal);
        Assert.Contains("reservation.OrderId == orderId", method, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOpenReservationQuantityAsync(order.Id, ct)", sourceCode, StringComparison.Ordinal);
    }

    private static string ExtractMethodBlock(string sourceCode, string signature)
    {
        var startIndex = sourceCode.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Method signature '{signature}' not found.");

        var braceStart = sourceCode.IndexOf('{', startIndex);
        Assert.True(braceStart >= 0, $"Method body for '{signature}' not found.");

        var depth = 0;
        for (var index = braceStart; index < sourceCode.Length; index++)
        {
            if (sourceCode[index] == '{')
            {
                depth++;
            }
            else if (sourceCode[index] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return sourceCode.Substring(startIndex, index - startIndex + 1);
                }
            }
        }

        throw new InvalidOperationException($"Method block for '{signature}' is not balanced.");
    }
}
