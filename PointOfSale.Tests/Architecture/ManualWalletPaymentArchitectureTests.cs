using System.Text.RegularExpressions;

namespace PointOfSale.Tests.Architecture;

/// <summary>
/// PHASE: Manual Wallet Payment Ledger-First Architecture Tests
/// 
/// ABSOLUTE LAWS:
/// 1. AdminManualPaymentController MUST NOT reference PaymentService for mutations
/// 2. AdminManualPaymentController MUST reference IPaymentLifecycleService
/// 3. PaymentService.ConfirmManualPaymentAsync MUST be marked [Obsolete]
/// 4. PaymentService.RejectManualPaymentAsync MUST be marked [Obsolete]
/// 5. PaymentLifecycleService.HandlePaymentResultAsync MUST enqueue outbox message for wallet approvals
/// 6. PaymentLifecycleService.HandlePaymentResultAsync MUST NOT directly write PaymentStatus outside authorized context
/// 7. No controller may directly set PaymentStatus for wallet payments
/// 
/// NON-NEGOTIABLE: If money is recognized without a ledger journal → the system is broken
/// </summary>
public class ManualWalletPaymentArchitectureTests
{
    #region Controller Architecture Tests

    [Fact]
    public void AdminManualPaymentController_MustReference_IPaymentLifecycleService()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        var controllerPath = Path.Combine(solutionDir,
            "PointOfSale.API", "Controllers", "AdminManualPaymentController.cs");

        if (!File.Exists(controllerPath))
        {
            Assert.True(false, "AdminManualPaymentController.cs not found");
            return;
        }

        var sourceCode = File.ReadAllText(controllerPath);

        // Act & Assert
        Assert.True(
            sourceCode.Contains("IPaymentLifecycleService"),
            "AdminManualPaymentController MUST reference IPaymentLifecycleService. " +
            "All wallet payment approvals/rejections MUST go through the lifecycle service " +
            "to ensure ledger journal creation (DEBIT 1140 Wallet Clearing, CREDIT 4020 Revenue).");
    }

    [Fact]
    public void AdminManualPaymentController_MustHave_LifecycleServiceField()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        var controllerPath = Path.Combine(solutionDir,
            "PointOfSale.API", "Controllers", "AdminManualPaymentController.cs");

        if (!File.Exists(controllerPath))
        {
            Assert.True(false, "AdminManualPaymentController.cs not found");
            return;
        }

        var sourceCode = File.ReadAllText(controllerPath);

        // Act
        var hasField = sourceCode.Contains("_paymentLifecycleService");

        // Assert
        Assert.True(hasField,
            "AdminManualPaymentController MUST have a _paymentLifecycleService field. " +
            "This is the ONLY authorized path for wallet payment mutations.");
    }

    [Fact]
    public void AdminManualPaymentController_MustCall_HandlePaymentResultAsync_ForConfirm()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        var controllerPath = Path.Combine(solutionDir,
            "PointOfSale.API", "Controllers", "AdminManualPaymentController.cs");

        if (!File.Exists(controllerPath))
        {
            Assert.True(false, "AdminManualPaymentController.cs not found");
            return;
        }

        var sourceCode = File.ReadAllText(controllerPath);

        var confirmBlock = ExtractMethodBlock(sourceCode, "ConfirmPayment(");
        var callsLifecycle = confirmBlock.Contains("HandlePaymentResultAsync");
        var usesSuccessOutcome = confirmBlock.Contains("PaymentLifecycleOutcome.Succeeded");
        var usesWalletApprovalSource = confirmBlock.Contains("PaymentLifecycleSource.PaymentReviewApproval");

        // Assert
        Assert.True(callsLifecycle && usesSuccessOutcome && usesWalletApprovalSource,
            "AdminManualPaymentController MUST call IPaymentLifecycleService.HandlePaymentResultAsync() " +
            "with Outcome=Succeeded and Source=WalletApproval for manual payment confirmations. " +
            "Direct PaymentStatus writes are FORBIDDEN.");
    }

    [Fact]
    public void AdminManualPaymentController_MustCall_HandlePaymentResultAsync_ForReject()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        var controllerPath = Path.Combine(solutionDir,
            "PointOfSale.API", "Controllers", "AdminManualPaymentController.cs");

        if (!File.Exists(controllerPath))
        {
            Assert.True(false, "AdminManualPaymentController.cs not found");
            return;
        }

        var sourceCode = File.ReadAllText(controllerPath);

        var rejectBlock = ExtractMethodBlock(sourceCode, "RejectPayment(");
        var callsLifecycle = rejectBlock.Contains("HandlePaymentResultAsync");
        var usesRejectedOutcome = rejectBlock.Contains("PaymentLifecycleOutcome.Rejected");
        var usesWalletRejectionSource = rejectBlock.Contains("PaymentLifecycleSource.PaymentReviewRejection");

        // Assert
        Assert.True(callsLifecycle && usesRejectedOutcome && usesWalletRejectionSource,
            "AdminManualPaymentController MUST call IPaymentLifecycleService.HandlePaymentResultAsync() " +
            "with Outcome=Rejected and Source=WalletRejection for manual payment rejections.");
    }

    [Fact]
    public void AdminManualPaymentController_MustNotCall_ConfirmManualPaymentAsync()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        var controllerPath = Path.Combine(solutionDir,
            "PointOfSale.API", "Controllers", "AdminManualPaymentController.cs");

        if (!File.Exists(controllerPath))
        {
            Assert.True(false, "AdminManualPaymentController.cs not found");
            return;
        }

        var sourceCode = File.ReadAllText(controllerPath);

        // Act - Check for forbidden legacy call
        var callsLegacyConfirm = sourceCode.Contains("ConfirmManualPaymentAsync");

        // Assert
        Assert.False(callsLegacyConfirm,
            "LEDGER-FIRST VIOLATION! AdminManualPaymentController MUST NOT call " +
            "PaymentService.ConfirmManualPaymentAsync(). This legacy method directly mutates " +
            "PaymentStatus without creating a ledger journal. " +
            "Use IPaymentLifecycleService.HandlePaymentResultAsync() instead.");
    }

    [Fact]
    public void AdminManualPaymentController_MustNotCall_RejectManualPaymentAsync_Legacy()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        var controllerPath = Path.Combine(solutionDir,
            "PointOfSale.API", "Controllers", "AdminManualPaymentController.cs");

        if (!File.Exists(controllerPath))
        {
            Assert.True(false, "AdminManualPaymentController.cs not found");
            return;
        }

        var sourceCode = File.ReadAllText(controllerPath);

        // Act - Check for forbidden legacy call
        var callsLegacyReject = sourceCode.Contains("RejectManualPaymentAsync");

        // Assert
        Assert.False(callsLegacyReject,
            "LEDGER-FIRST VIOLATION! AdminManualPaymentController MUST NOT call " +
            "PaymentService.RejectManualPaymentAsync(). " +
            "Use IPaymentLifecycleService.HandlePaymentResultAsync() instead.");
    }

    [Fact]
    public void AdminManualPaymentController_MustNotUse_AsNoTracking_ForConfirmOrRejectMutations()
    {
        var solutionDir = GetSolutionDirectory();
        var controllerPath = Path.Combine(solutionDir,
            "PointOfSale.API", "Controllers", "AdminManualPaymentController.cs");

        if (!File.Exists(controllerPath))
        {
            Assert.True(false, "AdminManualPaymentController.cs not found");
            return;
        }

        var sourceCode = File.ReadAllText(controllerPath);
        var confirmBlock = ExtractMethodBlock(sourceCode, "ConfirmPayment(");
        var rejectBlock = ExtractMethodBlock(sourceCode, "RejectPayment(");

        Assert.DoesNotContain(".AsNoTracking()", confirmBlock, StringComparison.Ordinal);
        Assert.DoesNotContain(".AsNoTracking()", rejectBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminManualPaymentController_MustHandle_DbUpdateConcurrencyException()
    {
        var solutionDir = GetSolutionDirectory();
        var controllerPath = Path.Combine(solutionDir,
            "PointOfSale.API", "Controllers", "AdminManualPaymentController.cs");

        if (!File.Exists(controllerPath))
        {
            Assert.True(false, "AdminManualPaymentController.cs not found");
            return;
        }

        var sourceCode = File.ReadAllText(controllerPath);
        var confirmBlock = ExtractMethodBlock(sourceCode, "ConfirmPayment(");
        var rejectBlock = ExtractMethodBlock(sourceCode, "RejectPayment(");

        Assert.Contains("DbUpdateConcurrencyException", confirmBlock, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_CONCURRENCY_CONFLICT", confirmBlock, StringComparison.Ordinal);

        Assert.Contains("DbUpdateConcurrencyException", rejectBlock, StringComparison.Ordinal);
        Assert.Contains("PAYMENT_CONCURRENCY_CONFLICT", rejectBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminManualPaymentController_MustNotFallbackToSystemAdminIdentity()
    {
        var solutionDir = GetSolutionDirectory();
        var controllerPath = Path.Combine(solutionDir,
            "PointOfSale.API", "Controllers", "AdminManualPaymentController.cs");

        if (!File.Exists(controllerPath))
        {
            Assert.True(false, "AdminManualPaymentController.cs not found");
            return;
        }

        var sourceCode = File.ReadAllText(controllerPath);

        Assert.DoesNotContain("\"system-admin\"", sourceCode, StringComparison.Ordinal);
        Assert.Contains("throw new UnauthorizedAccessException()", sourceCode, StringComparison.Ordinal);
    }

    #endregion

    #region PaymentService Legacy Guard Tests

    [Fact]
    public void PaymentService_ConfirmManualPaymentAsync_MustBeObsolete()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        var servicePath = Path.Combine(solutionDir,
            "PointOfSale.API", "Services", "Payments", "PaymentService.cs");

        if (!File.Exists(servicePath))
        {
            Assert.True(false, "PaymentService.cs not found");
            return;
        }

        var sourceCode = File.ReadAllText(servicePath);

        // Find the [Obsolete] attribute near ConfirmManualPaymentAsync
        var lines = sourceCode.Split('\n');
        bool foundObsolete = false;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("ConfirmManualPaymentAsync") && 
                lines[i].Contains("public"))
            {
                // Check if [Obsolete] is in preceding 10 lines
                for (int j = Math.Max(0, i - 10); j < i; j++)
                {
                    if (lines[j].Contains("[Obsolete"))
                    {
                        foundObsolete = true;
                        break;
                    }
                }
                break;
            }
        }

        // Assert
        Assert.True(foundObsolete,
            "PaymentService.ConfirmManualPaymentAsync() MUST be marked [Obsolete]. " +
            "This method directly sets PaymentStatus = Collected without creating a ledger journal. " +
            "It violates the ledger-first architecture.");
    }

    [Fact]
    public void PaymentService_RejectManualPaymentAsync_MustBeObsolete()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        var servicePath = Path.Combine(solutionDir,
            "PointOfSale.API", "Services", "Payments", "PaymentService.cs");

        if (!File.Exists(servicePath))
        {
            Assert.True(false, "PaymentService.cs not found");
            return;
        }

        var sourceCode = File.ReadAllText(servicePath);

        // Find the [Obsolete] attribute near RejectManualPaymentAsync
        var lines = sourceCode.Split('\n');
        bool foundObsolete = false;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("RejectManualPaymentAsync") && 
                lines[i].Contains("public"))
            {
                // Check if [Obsolete] is in preceding 10 lines
                for (int j = Math.Max(0, i - 10); j < i; j++)
                {
                    if (lines[j].Contains("[Obsolete"))
                    {
                        foundObsolete = true;
                        break;
                    }
                }
                break;
            }
        }

        // Assert
        Assert.True(foundObsolete,
            "PaymentService.RejectManualPaymentAsync() MUST be marked [Obsolete]. " +
            "This method directly sets PaymentStatus without going through PaymentLifecycleService.");
    }

    #endregion

    #region PaymentLifecycleService Ledger-First Tests

    [Fact]
    public void PaymentLifecycleService_ApproveReviewedPaymentAsync_MustEnqueueOutbox()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        var servicePath = Path.Combine(solutionDir,
            "PointOfSale.Infrastructure", "Payments", "PaymentLifecycleService.cs");

        if (!File.Exists(servicePath))
        {
            Assert.True(false, "PaymentLifecycleService.cs not found");
            return;
        }

        var sourceCode = File.ReadAllText(servicePath);

        // Act
        var enqueuesWallet = sourceCode.Contains("EnqueueWalletApprovedAsync");

        // Assert
        Assert.True(enqueuesWallet,
            "LEDGER-FIRST VIOLATION! PaymentLifecycleService.ApproveReviewedPaymentAsync() " +
            "MUST call EnqueueWalletApprovedAsync to create a ledger journal via the accounting outbox. " +
            "Expected journal: DEBIT 1140 Wallet Clearing, CREDIT 4020 E-commerce Revenue. " +
            "If money is recognized without a ledger journal → the system is broken.");
    }

    [Fact]
    public void PaymentLifecycleService_ApproveWallet_MustUseAuthorizedContext()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        var servicePath = Path.Combine(solutionDir,
            "PointOfSale.Infrastructure", "Payments", "PaymentLifecycleService.cs");

        if (!File.Exists(servicePath))
        {
            Assert.True(false, "PaymentLifecycleService.cs not found");
            return;
        }

        var sourceCode = File.ReadAllText(servicePath);

        // Act
        var usesAuthorizedContext =
            sourceCode.Contains("PaymentStatusSaveChangesInterceptor.EnableAuthorizedContext()") &&
            sourceCode.Contains("PaymentStatusSaveChangesInterceptor.DisableAuthorizedContext()");

        // Assert
        Assert.True(usesAuthorizedContext,
            "PaymentLifecycleService MUST use PaymentStatusSaveChangesInterceptor authorized context " +
            "to modify PaymentStatus. This is the ONLY way to change PaymentStatus without triggering " +
            "the security violation.");
    }

    [Fact]
    public void PaymentLifecycleService_ApproveWallet_MustPublishDomainEvent()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        var servicePath = Path.Combine(solutionDir,
            "PointOfSale.Infrastructure", "Payments", "PaymentLifecycleService.cs");

        if (!File.Exists(servicePath))
        {
            Assert.True(false, "PaymentLifecycleService.cs not found");
            return;
        }

        var sourceCode = File.ReadAllText(servicePath);

        // Act
        var publishesEvent = sourceCode.Contains("ReviewedPaymentApprovedEvent");

        // Assert
        Assert.True(publishesEvent,
            "PaymentLifecycleService MUST publish ReviewedPaymentApprovedEvent after review approval " +
            "to enable downstream consumers (WhatsApp notifications, projections, etc.).");
    }

    #endregion

    #region Outbox Idempotency Tests

    [Fact]
    public void AccountingOutboxService_WalletApproved_UsesCorrectIdempotencyKey()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        var outboxPath = Path.Combine(solutionDir,
            "PointOfSale.Infrastructure", "Accounting", "AccountingOutboxService.cs");

        if (!File.Exists(outboxPath))
        {
            Assert.True(false, "AccountingOutboxService.cs not found");
            return;
        }

        var sourceCode = File.ReadAllText(outboxPath);

        // Act
        var hasCorrectKey = sourceCode.Contains("WALLET_APPROVED:{tenantId}:{orderId}");

        // Assert
        Assert.True(hasCorrectKey,
            "AccountingOutboxService.EnqueueWalletApprovedAsync() MUST use idempotency key " +
            "format WALLET_APPROVED:{TenantId}:{OrderId} to prevent duplicate journal entries.");
    }

    #endregion

    #region Cross-Cutting Architecture Scan

    [Fact]
    public void NoController_ShouldDirectly_SetPaymentStatus_ForWalletPayments()
    {
        // Arrange
        var solutionDir = GetSolutionDirectory();
        var controllersDir = Path.Combine(solutionDir, "PointOfSale.API", "Controllers");

        if (!Directory.Exists(controllersDir))
        {
            Assert.True(true, "Controllers directory not found - skipping scan");
            return;
        }

        var violations = new List<string>();
        var controllerFiles = Directory.GetFiles(controllersDir, "*.cs", SearchOption.AllDirectories);

        foreach (var file in controllerFiles)
        {
            var content = File.ReadAllText(file);
            var fileName = Path.GetFileName(file);

            // Check for direct PaymentStatus assignment patterns
            if (Regex.IsMatch(content, @"\.PaymentStatus\s*=\s*PaymentStatus\.(Collected|Settled|Paid)"))
            {
                violations.Add($"{fileName} - Direct PaymentStatus assignment to money-recognizing state");
            }
        }

        // Assert
        Assert.True(violations.Count == 0,
            "LEDGER-FIRST VIOLATION!\n\n" +
            "Controllers MUST NOT directly set PaymentStatus to money-recognizing states:\n" +
            string.Join("\n", violations.Select(v => $"  ❌ {v}")) +
            "\n\nUse IPaymentLifecycleService instead:\n" +
            "  ✅ _paymentLifecycleService.ApproveReviewedPaymentAsync()\n" +
            "  ✅ _paymentLifecycleService.CaptureOnlinePaymentAsync()\n" +
            "  ✅ _paymentLifecycleService.MarkOfflineCollectionDeliveredAsync()");
    }

    #endregion

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

    #endregion
}
