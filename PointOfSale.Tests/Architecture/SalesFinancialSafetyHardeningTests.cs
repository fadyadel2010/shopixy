using System.Reflection;
using PointOfSale.API.Controllers;
using PointOfSale.Core.Enums;

namespace PointOfSale.Tests.Architecture;

public sealed class SalesFinancialSafetyHardeningTests
{
    [Fact]
    public void SalesController_MustRequireAndClaimPaymentAttemptId_BeforeMutation()
    {
        var controllerPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Controllers",
            "SalesController.cs");

        var source = File.ReadAllText(Path.GetFullPath(controllerPath));

        Assert.Contains("X-Payment-Attempt-Id", source);
        Assert.Contains("Invalid idempotency key", source);
        Assert.Contains("MaxSalesIdempotencyKeyLength = 100", source);
        Assert.Contains("^[A-Za-z0-9-]+$", source);
        Assert.Contains("BuildCreateSaleIdempotencyPayload(request)", source);
        Assert.Contains("BuildUpdateSaleIdempotencyPayload(request)", source);
        Assert.Contains("BuildSalesRequestHash(\"UPDATE\", new", source);
        Assert.Contains("Request = BuildUpdateSaleIdempotencyPayload(request)", source);
        Assert.Contains("X-Pos-Cart-Id", source);
        Assert.Contains("ValidateOptionalSalesIdempotencyKey(posCartId)", source);
        Assert.Contains("var createIdempotencyKey = normalizedPosCartId ?? normalizedPaymentAttemptId", source);
        Assert.Contains("TryClaimSalesIdempotencyAsync(\"CREATE\", createIdempotencyKey, requestHash)", source);
        Assert.Contains("TryClaimSalesIdempotencyAsync(\"UPDATE\", normalizedPaymentAttemptId, requestHash)", source);
        Assert.Contains("Idempotency key reuse with different payload", source);
        Assert.Contains("BuildStoredSaleResponse", source);
        Assert.Contains("CompleteSalesIdempotencyAsync", source);
        Assert.Contains("EnqueueSaleReversalAsync", source);
        Assert.Contains("Idempotency NEW key", source);
        Assert.Contains("Idempotency HIT key", source);
        Assert.Contains("TryRecoverSalesUpdateIdempotencyByRequestHashAsync", source);
        Assert.Contains("Idempotency HASH HIT", source);
        Assert.Contains("same payload is already being processed", source);
        Assert.Contains("Idempotency CONFLICT key", source);
        Assert.Contains("stale claim detected", source);
        Assert.Contains("DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull", source);
        Assert.Contains("PropertyNamingPolicy = JsonNamingPolicy.CamelCase", source);
    }

    [Fact]
    public void SalesAccountingMutationVersion_MustGuardAgainstStaleOutboxMessages()
    {
        var controllerPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Controllers",
            "SalesController.cs");
        var processorPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.Infrastructure",
            "Accounting",
            "AccountingOutboxProcessor.cs");
        var migrationPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.Infrastructure",
            "Migrations",
            "20260424150000_S28_PosSalesAccountingMutationVersion.sql");

        var controllerSource = File.ReadAllText(Path.GetFullPath(controllerPath));
        var processorSource = File.ReadAllText(Path.GetFullPath(processorPath));
        var saleReversalProcessorSource = ExtractMethodSource(processorSource, "private async Task<PostingResult> ProcessSaleReversalAsync");
        var migrationSql = File.ReadAllText(Path.GetFullPath(migrationPath));

        Assert.Contains("AccountingMutationVersion++", controllerSource, StringComparison.Ordinal);
        Assert.Contains("invoice.AccountingMutationVersion", controllerSource, StringComparison.Ordinal);
        Assert.Contains("TryGetMutationVersion", processorSource, StringComparison.Ordinal);
        Assert.Contains("invoice.AccountingMutationVersion != mutationVersion.Value", processorSource, StringComparison.Ordinal);
        Assert.Contains("Skipping stale POS sale accounting message", processorSource, StringComparison.Ordinal);
        Assert.Contains("Skipping stale sale mutation accounting message", processorSource, StringComparison.Ordinal);
        Assert.Contains("LoadSalesInvoiceForAccountingPostAsync", processorSource, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", processorSource, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, ROWLOCK, HOLDLOCK", processorSource, StringComparison.Ordinal);
        Assert.Contains("await accounting.PostPosSaleAsync(invoice, ct, operationKey)", saleReversalProcessorSource, StringComparison.Ordinal);
        Assert.Contains("LoadSalesInvoiceForAccountingPostAsync(db, tenantId, invoiceId, ct)", saleReversalProcessorSource, StringComparison.Ordinal);
        Assert.Contains("await transaction.CommitAsync(ct);", saleReversalProcessorSource, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"AccountingMutationVersion\" integer NOT NULL DEFAULT 0", migrationSql, StringComparison.Ordinal);
        Assert.Contains("20260424150000_S28_PosSalesAccountingMutationVersion", migrationSql, StringComparison.Ordinal);
    }

    [Fact]
    public void PurchaseAccountingMutationVersion_MustGuardAgainstStaleOutboxMessages()
    {
        var controllerPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Controllers",
            "PurchasesController.cs");
        var entityPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.Core",
            "Entities",
            "PurchaseInvoice.cs");
        var processorPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.Infrastructure",
            "Accounting",
            "AccountingOutboxProcessor.cs");
        var migrationPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.Infrastructure",
            "Migrations",
            "20260508170000_S78_PosPurchaseAccountingMutationVersion.sql");

        var controllerSource = File.ReadAllText(Path.GetFullPath(controllerPath));
        var entitySource = File.ReadAllText(Path.GetFullPath(entityPath));
        var processorSource = File.ReadAllText(Path.GetFullPath(processorPath));
        var purchaseReversalProcessorSource = ExtractMethodSource(processorSource, "private async Task<PostingResult> ProcessPurchaseReversalAsync");
        var migrationSql = File.ReadAllText(Path.GetFullPath(migrationPath));

        Assert.Contains("public int AccountingMutationVersion", entitySource, StringComparison.Ordinal);
        Assert.Contains("LoadPurchaseInvoiceForMutationAsync", controllerSource, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", controllerSource, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK, ROWLOCK, HOLDLOCK", controllerSource, StringComparison.Ordinal);
        Assert.Contains("IsExpectedPurchaseMutationVersion", controllerSource, StringComparison.Ordinal);
        Assert.Contains("request.ExpectedMutationVersion", controllerSource, StringComparison.Ordinal);
        Assert.Contains("invoice.AccountingMutationVersion++", controllerSource, StringComparison.Ordinal);
        Assert.Contains("invoice.AccountingMutationVersion", controllerSource, StringComparison.Ordinal);
        Assert.Contains("candidate.AggregateVersion == mutationVersion", controllerSource, StringComparison.Ordinal);
        Assert.Contains("Skipping stale POS purchase accounting message", processorSource, StringComparison.Ordinal);
        Assert.Contains("Skipping stale purchase mutation accounting message", processorSource, StringComparison.Ordinal);
        Assert.Contains("LoadPurchaseInvoiceForAccountingPostAsync", processorSource, StringComparison.Ordinal);
        Assert.Contains("await accounting.PostPosPurchaseAsync(", purchaseReversalProcessorSource, StringComparison.Ordinal);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"AccountingMutationVersion\" integer NOT NULL DEFAULT 0", migrationSql, StringComparison.Ordinal);
        Assert.Contains("20260508170000_S78_PosPurchaseAccountingMutationVersion", migrationSql, StringComparison.Ordinal);
    }

    [Fact]
    public void SalesController_SynchronousAccountingPost_MustCompleteMatchingOutboxRowBeforeCommit()
    {
        var controllerPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Controllers",
            "SalesController.cs");

        var source = File.ReadAllText(Path.GetFullPath(controllerPath));
        var createMethod = ExtractMethodSource(source, "public async Task<ActionResult<SaleResponse>> CreateSale(");
        var updateMethod = ExtractMethodSource(source, "public async Task<ActionResult<SaleResponse>> UpdateSale(");
        var helperMethod = ExtractMethodSource(source, "private async Task MarkSynchronousSaleAccountingOutboxProcessedAsync");

        Assert.Contains("background worker remains a recovery path, not a second financial authority", createMethod, StringComparison.Ordinal);
        Assert.Contains("await MarkSynchronousSaleAccountingOutboxProcessedAsync(", createMethod, StringComparison.Ordinal);
        Assert.Contains("BuildBaseSaleAccountingOutboxIdempotencyKey", createMethod, StringComparison.Ordinal);
        Assert.Contains("await MarkSynchronousSaleAccountingOutboxProcessedAsync(", updateMethod, StringComparison.Ordinal);
        Assert.Contains("BuildSaleMutationAccountingOutboxIdempotencyKey", updateMethod, StringComparison.Ordinal);
        AssertContainsInOrder(
            createMethod,
            "await _accountingOutbox.EnqueuePosSaleAsync",
            "await _commerceAccounting.PostPosSaleAsync(invoice)",
            "await MarkSynchronousSaleAccountingOutboxProcessedAsync",
            "CompleteSalesIdempotencyAsync");
        AssertContainsInOrder(
            updateMethod,
            "await _accountingOutbox.EnqueueSaleReversalAsync",
            "await PostUpdatedSaleAccountingSynchronouslyAsync",
            "await MarkSynchronousSaleAccountingOutboxProcessedAsync",
            "CompleteSalesIdempotencyAsync");
        Assert.Contains("candidate.IdempotencyKey == idempotencyKey", helperMethod, StringComparison.Ordinal);
        Assert.Contains("candidate.AggregateVersion == mutationVersion", helperMethod, StringComparison.Ordinal);
        Assert.Contains("message.MarkProcessed(journalId.Value)", helperMethod, StringComparison.Ordinal);
        Assert.Contains("already processed without a journal", helperMethod, StringComparison.Ordinal);
        Assert.Contains("failed or claimed", helperMethod, StringComparison.Ordinal);
        Assert.Contains("ResolveCustomerLedgerPaymentAmount(invoice)", createMethod, StringComparison.Ordinal);
        Assert.Contains("ResolveCustomerLedgerPaymentAmount(invoice)", updateMethod, StringComparison.Ordinal);
        Assert.Contains("Math.Min(paidAmount, totalAmount)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SalesController_RevertSaleEffects_MustNotDirectlyMutateTreasuryBalances()
    {
        var controllerPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Controllers",
            "SalesController.cs");

        var source = File.ReadAllText(Path.GetFullPath(controllerPath));
        var methodSource = ExtractMethodSource(source, "private async Task RevertSaleEffects");

        Assert.Contains("_saleFinancialIntegrityService.RevertLegacyTreasuryEffectsAsync(", methodSource, StringComparison.Ordinal);
        Assert.Contains("journalPolicy", methodSource, StringComparison.Ordinal);
        Assert.Contains("DO NOT modify balances directly", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("cashbox.CurrentBalance -= tx.Amount", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("cashbox.ExpectedBalance -= tx.Amount", methodSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Guid.Parse(\"11111111-1111-1111-1111-111111111111\")", methodSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SalesController_DeleteSale_MustFailFastWhenPostedSaleJournalsStillExist()
    {
        var controllerPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Controllers",
            "SalesController.cs");

        var source = File.ReadAllText(Path.GetFullPath(controllerPath));
        
        // Extract the DeleteSale method body using the helper
        var deleteSaleBody = ExtractMethodSource(source, "public async Task<ActionResult> DeleteSale(");
        
        Assert.Contains("await RevertSaleEffects(", deleteSaleBody, StringComparison.Ordinal);
        Assert.Contains("journalPolicy: JournalPolicy.RejectExisting", deleteSaleBody, StringComparison.Ordinal);
    }

    [Fact]
    public void SaleFinancialIntegrityService_MustUseLockedLedgerFirstReversalFlow()
    {
        var servicePath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Services",
            "SaleFinancialIntegrityService.cs");

        var source = File.ReadAllText(Path.GetFullPath(servicePath));

        Assert.Contains("FOR UPDATE", source, StringComparison.Ordinal);
        Assert.Contains("UPDLOCK", source, StringComparison.Ordinal);
        Assert.Contains("AccountingOutboxMessages", source, StringComparison.Ordinal);
        Assert.Contains("PostedJournalsExistException", source, StringComparison.Ordinal);
        Assert.Contains("SaleReversalEventType", source, StringComparison.Ordinal);
        Assert.Contains("RemoveRange(pendingOutboxMessages)", source, StringComparison.Ordinal);
        Assert.Contains("JournalPolicy.RejectExisting", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetByCodeAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AccountCodes.VisaClearing", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PostPosSaleAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReverseJournalAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CashboxTransactions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("VisaTransactions", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TreasuryTransactions", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PurchasesController_MustUseDatabaseSequenceForInvoiceNumbers()
    {
        var controllerPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.API",
            "Controllers",
            "PurchasesController.cs");

        var source = File.ReadAllText(Path.GetFullPath(controllerPath));

        Assert.Contains("PurchaseInvoiceSequenceName = \"purchase_invoice_seq\"", source);
        Assert.Contains("GenerateInvoiceNumberAsync()", source);
        Assert.Contains("GetNextPurchaseInvoiceSequenceValueAsync()", source);
        Assert.Contains("command.CommandText = $\"SELECT nextval('{PurchaseInvoiceSequenceName}')\"", source);
        Assert.Contains("FormatInvoiceNumber", source);
    }

    [Fact]
    public void SalesIdempotencySqlMigration_MustCreateTenantScopedUniqueIndex_AndHistoryEntry()
    {
        var migrationPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.Infrastructure",
            "Migrations",
            "20260407134756_AddSalesRequestIdempotency.sql");

        var migrationSql = File.ReadAllText(Path.GetFullPath(migrationPath));

        Assert.Contains("CREATE TABLE IF NOT EXISTS \"SalesRequestIdempotency\"", migrationSql);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS \"IX_SalesRequestIdempotency_TenantId_Key_Operation\"", migrationSql);
        Assert.Contains("\"Key\" character varying(100) NOT NULL", migrationSql);
        Assert.Contains("__EFMigrationsHistory", migrationSql);
        Assert.Contains("20260407134756_AddSalesRequestIdempotency", migrationSql);
    }

    [Fact]
    public void SalesUpdateIdempotencyRequestHashRecoveryMigration_MustCreateLookupIndex()
    {
        var migrationPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.Infrastructure",
            "Migrations",
            "20260424153000_S29_SalesUpdateIdempotencyRequestHashRecovery.sql");

        var migrationSql = File.ReadAllText(Path.GetFullPath(migrationPath));

        Assert.Contains("CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_SalesRequestIdempotency_TenantId_Operation_RequestHash_IsDeleted\"", migrationSql);
        Assert.Contains("ON \"SalesRequestIdempotency\" (\"TenantId\", \"Operation\", \"RequestHash\", \"IsDeleted\")", migrationSql);
    }

    [Fact]
    public void PurchaseInvoiceSequenceMigration_MustCreateAndBackfillSequence()
    {
        var migrationPath = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "PointOfSale.Infrastructure",
            "Migrations",
            "20260408103000_AddPurchaseInvoiceSequence.sql");

        var migrationSql = File.ReadAllText(Path.GetFullPath(migrationPath));

        Assert.Contains("CREATE SEQUENCE IF NOT EXISTS purchase_invoice_seq", migrationSql);
        Assert.Contains("START WITH 1000", migrationSql);
        Assert.Contains("setval('purchase_invoice_seq'", migrationSql);
        Assert.Contains("20260408103000_AddPurchaseInvoiceSequence", migrationSql);
    }

    [Fact]
    public void SalesRequestHash_MustBeDeterministic_AndChangeWhenPayloadChanges()
    {
        var method = typeof(SalesController).GetMethod(
            "BuildSalesRequestHash",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var baselineRequest = new CreateSaleRequest
        {
            WarehouseId = Guid.NewGuid(),
            PaymentMethod = PaymentMethod.Cash,
            PaidAmount = 100m,
            Items =
            [
                new SaleItemRequest
                {
                    ProductId = Guid.NewGuid(),
                    Quantity = 1m,
                    UnitPrice = 100m,
                    TaxRate = 0m,
                }
            ]
        };

        var samePayloadRequest = new CreateSaleRequest
        {
            WarehouseId = baselineRequest.WarehouseId,
            PaymentMethod = baselineRequest.PaymentMethod,
            PaidAmount = baselineRequest.PaidAmount,
            Items =
            [
                new SaleItemRequest
                {
                    ProductId = baselineRequest.Items[0].ProductId,
                    Quantity = baselineRequest.Items[0].Quantity,
                    UnitPrice = baselineRequest.Items[0].UnitPrice,
                    TaxRate = baselineRequest.Items[0].TaxRate,
                }
            ]
        };

        var changedPayloadRequest = new CreateSaleRequest
        {
            WarehouseId = baselineRequest.WarehouseId,
            PaymentMethod = baselineRequest.PaymentMethod,
            PaidAmount = baselineRequest.PaidAmount,
            Items =
            [
                new SaleItemRequest
                {
                    ProductId = baselineRequest.Items[0].ProductId,
                    Quantity = 2m,
                    UnitPrice = baselineRequest.Items[0].UnitPrice,
                    TaxRate = baselineRequest.Items[0].TaxRate,
                }
            ]
        };

        var baselineHash = (string)method!.Invoke(null, ["CREATE", baselineRequest])!;
        var sameHash = (string)method.Invoke(null, ["CREATE", samePayloadRequest])!;
        var changedHash = (string)method.Invoke(null, ["CREATE", changedPayloadRequest])!;

        Assert.Equal(baselineHash, sameHash);
        Assert.NotEqual(baselineHash, changedHash);
    }

    private static string ExtractMethodSource(string source, string signature)
    {
        var startIndex = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Could not locate method signature '{signature}'.");

        // Find the opening brace of the method body
        var braceIndex = source.IndexOf('{', startIndex);
        if (braceIndex < 0)
        {
            return source[startIndex..source.Length];
        }

        // Walk forward counting braces to find the matching closing brace
        var braceCount = 1;
        var index = braceIndex + 1;
        var inString = false;
        var inVerbatimString = false;
        var inRawString = false;
        var rawStringQuoteCount = 0;
        var inChar = false;
        var inSingleLineComment = false;
        var inMultiLineComment = false;

        while (index < source.Length && braceCount > 0)
        {
            var c = source[index];
            var prev = index > 0 ? source[index - 1] : '\0';

            // Handle escape sequences in regular strings and chars (not verbatim)
            if ((inString || inChar) && !inVerbatimString && prev == '\\')
            {
                index++;
                continue;
            }

            // Handle single-line comments
            if (!inString && !inVerbatimString && !inRawString && !inChar && !inMultiLineComment && c == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                inSingleLineComment = true;
                index += 2;
                continue;
            }

            if (inSingleLineComment && c == '\n')
            {
                inSingleLineComment = false;
                index++;
                continue;
            }

            // Handle multi-line comments
            if (!inString && !inVerbatimString && !inRawString && !inChar && !inSingleLineComment && c == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                inMultiLineComment = true;
                index += 2;
                continue;
            }

            if (inMultiLineComment && c == '*' && index + 1 < source.Length && source[index + 1] == '/')
            {
                inMultiLineComment = false;
                index += 2;
                continue;
            }

            // Skip if in comment
            if (inSingleLineComment || inMultiLineComment)
            {
                index++;
                continue;
            }

            // Handle raw string literals (""" or more quotes)
            if (!inString && !inVerbatimString && !inRawString && !inChar && c == '"')
            {
                // Count consecutive quotes
                var quoteCount = 0;
                var tempIndex = index;
                while (tempIndex < source.Length && source[tempIndex] == '"')
                {
                    quoteCount++;
                    tempIndex++;
                }

                if (quoteCount >= 3)
                {
                    // Entering raw string literal
                    inRawString = true;
                    rawStringQuoteCount = quoteCount;
                    index = tempIndex;
                    continue;
                }
            }

            // Handle exiting raw string literals
            if (inRawString && c == '"')
            {
                // Count consecutive quotes
                var quoteCount = 0;
                var tempIndex = index;
                while (tempIndex < source.Length && source[tempIndex] == '"')
                {
                    quoteCount++;
                    tempIndex++;
                }

                if (quoteCount >= rawStringQuoteCount)
                {
                    // Exiting raw string literal
                    inRawString = false;
                    rawStringQuoteCount = 0;
                    index = tempIndex;
                    continue;
                }
            }

            // Handle verbatim strings (@"..." or $@"..." or @$"...")
            if (!inString && !inVerbatimString && !inRawString && !inChar && c == '"')
            {
                // Check for verbatim/interpolated prefix
                var isVerbatim = false;
                if (index >= 1 && source[index - 1] == '@')
                {
                    isVerbatim = true;
                }
                else if (index >= 2 && source[index - 1] == '@' && source[index - 2] == '$')
                {
                    isVerbatim = true;
                }
                else if (index >= 2 && source[index - 1] == '$' && source[index - 2] == '@')
                {
                    isVerbatim = true;
                }

                if (isVerbatim)
                {
                    inVerbatimString = true;
                    inString = true;
                    index++;
                    continue;
                }
            }

            // Handle escaped quotes in verbatim strings ("" = escaped quote)
            if (inVerbatimString && c == '"')
            {
                if (index + 1 < source.Length && source[index + 1] == '"')
                {
                    // Escaped quote in verbatim string, skip both
                    index += 2;
                    continue;
                }
                else
                {
                    // End of verbatim string
                    inVerbatimString = false;
                    inString = false;
                    index++;
                    continue;
                }
            }

            // Handle regular strings
            if (c == '"' && !inChar && !inVerbatimString && !inRawString)
            {
                inString = !inString;
            }

            // Handle chars
            if (c == '\'' && !inString && !inVerbatimString && !inRawString)
            {
                inChar = !inChar;
            }

            // Count braces only when not in string, char, or comment
            if (!inString && !inVerbatimString && !inRawString && !inChar)
            {
                if (c == '{')
                {
                    braceCount++;
                }
                else if (c == '}')
                {
                    braceCount--;
                }
            }

            index++;
        }

        // Return from signature start through the closing brace
        var endIndex = braceCount == 0 ? index : source.Length;
        return source[startIndex..endIndex];
    }

    private static void AssertContainsInOrder(string source, params string[] expectedFragments)
    {
        var currentIndex = 0;
        foreach (var fragment in expectedFragments)
        {
            var foundIndex = source.IndexOf(fragment, currentIndex, StringComparison.Ordinal);
            Assert.True(
                foundIndex >= 0,
                $"Expected to find '{fragment}' after index {currentIndex}.");
            currentIndex = foundIndex + fragment.Length;
        }
    }
}
