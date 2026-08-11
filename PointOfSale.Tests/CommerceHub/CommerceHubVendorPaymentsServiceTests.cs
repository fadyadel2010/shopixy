using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.API.Configuration;
using PointOfSale.API.DTOs.CommerceHub;
using PointOfSale.API.Services;
using PointOfSale.API.Services.CommerceHub;
using PointOfSale.API.Services.CommerceHub.Finance;
using PointOfSale.API.Services.CommerceHub.FinancialLedger;
using PointOfSale.API.Services.CommerceHub.Wallet;
using PointOfSale.API.SuperAdmin.DTOs;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Entities.CommerceHubFinance;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Enums.CommerceHubFinance;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Tests.TestDoubles;

namespace PointOfSale.Tests.CommerceHub;

public sealed class CommerceHubVendorPaymentsServiceTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CommerceHubVendorPaymentsService _service;
    private readonly CommerceHubWalletService _walletService;
    private readonly CommerceHubVendorDebtService _debtService;
    private readonly Guid _adminId = Guid.NewGuid();

    public CommerceHubVendorPaymentsServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _walletService = new CommerceHubWalletService(_dbContext);
        _debtService = new CommerceHubVendorDebtService(
            _dbContext,
            Options.Create(new CommerceHubVendorDebtOptions()));
        var ledgerService = new CommerceHubFinancialLedgerService(
            _dbContext,
            NullLogger<CommerceHubFinancialLedgerService>.Instance);
        var financialSafetyGuardService = new CommerceHubFinancialSafetyGuardService(_dbContext);

        _service = new CommerceHubVendorPaymentsService(
            _dbContext,
            new FakeR2StorageService(),
            new FileValidationService(),
            _debtService,
            ledgerService,
            financialSafetyGuardService,
            new NoOpUserGovernanceService(),
            NullLogger<CommerceHubVendorPaymentsService>.Instance);
    }

    [Fact]
    public async Task ApprovePaymentRequestAsync_ShouldReduceDebt_AndIncreasePlatformBalance()
    {
        var vendorId = await SeedVendorAsync("Manual Settlement Vendor");
        var paymentMethod = await CreatePaymentMethodAsync();

        var debtTransaction = SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            1000m,
            CommerceHubFinancialReferenceType.Order,
            Guid.NewGuid());
        SeedPostedCommerceHubJournal(vendorId, debtTransaction);
        SeedHealthyReconciliationReport(vendorId);

        await _dbContext.SaveChangesAsync();

        var paymentRequest = await _service.CreateVendorPaymentRequestAsync(
            vendorId,
            CreatePaymentRequestForm(paymentMethod.Id, 300m, "payment-create-1", "TX-001"));

        var platformBefore = await _walletService.GetPlatformBalanceAsync();

        var approved = await _service.ApprovePaymentRequestAsync(
            paymentRequest.PaymentRequestId,
            new SuperAdminApproveVendorPaymentRequestDto
            {
                Reason = "Verified manual transfer."
            },
            _adminId);

        var debtSnapshot = await _debtService.GetVendorDebtSnapshotAsync(vendorId);
        var platformAfter = await _walletService.GetPlatformBalanceAsync();
        var ledgerEntries = await _dbContext.CommerceHubFinancialTransactions
            .Where(entry => entry.ReferenceId == paymentRequest.PaymentRequestId)
            .OrderBy(entry => entry.EntityType)
            .ToListAsync();
        var operationAudit = await _dbContext.CommerceHubFinancialOperationAudits
            .SingleAsync(entry => entry.ReferenceId == paymentRequest.PaymentRequestId);

        Assert.Equal("Approved", approved.Status);
        Assert.Equal(700m, debtSnapshot.AmountOwedByVendor);
        Assert.Equal(platformBefore.Balance + 300m, platformAfter.Balance);
        Assert.Equal(2, ledgerEntries.Count);
        Assert.Equal(0m, operationAudit.BeforePlatformCash);
        Assert.Equal(1000m, operationAudit.BeforeVendorDebt);
        Assert.Equal(300m, operationAudit.AfterPlatformCash);
        Assert.Equal(700m, operationAudit.AfterVendorDebt);
        Assert.All(ledgerEntries, entry => Assert.Equal(paymentRequest.PaymentRequestId.ToString("D"), entry.CorrelationId));
        Assert.Contains(ledgerEntries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Vendor &&
            entry.Direction == CommerceHubFinancialTransactionDirection.Credit &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Settlement &&
            entry.Amount == 300m);
        Assert.Contains(ledgerEntries, entry =>
            entry.EntityType == CommerceHubFinancialEntityType.Platform &&
            entry.Direction == CommerceHubFinancialTransactionDirection.Credit &&
            entry.TransactionType == CommerceHubFinancialTransactionType.Settlement &&
            entry.Amount == 300m);
    }

    [Fact]
    public async Task ApprovePaymentRequestAsync_ShouldRecognizeInwardCash_WhenOutwardReadinessNotReady()
    {
        var vendorId = await SeedVendorAsync("Blocked Vendor");
        var paymentMethod = await CreatePaymentMethodAsync();

        // Seed vendor debt
        var debtTransaction = SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            500m,
            CommerceHubFinancialReferenceType.Order,
            Guid.NewGuid());
        SeedPostedCommerceHubJournal(vendorId, debtTransaction);

        // Seed a failed critical accounting outbox message to trigger financial readiness gate denial
        _dbContext.Set<AccountingOutboxMessage>().Add(new AccountingOutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = vendorId,
            EventType = "PosSale",
            BusinessEntityId = Guid.NewGuid(),
            OccurredAtUtc = DateTime.UtcNow.AddMinutes(-20),
            IsFailed = true,
            ProcessedAtUtc = null,
            PayloadJson = "{}",
            IdempotencyKey = "test-idempotency-key"
        });

        await _dbContext.SaveChangesAsync();

        var paymentRequest = await _service.CreateVendorPaymentRequestAsync(
            vendorId,
            CreatePaymentRequestForm(paymentMethod.Id, 200m, "payment-create-blocked", "TX-BLOCKED"));

        var approved = await _service.ApprovePaymentRequestAsync(
            paymentRequest.PaymentRequestId,
            new SuperAdminApproveVendorPaymentRequestDto
            {
                Reason = "Recognize already received vendor cash."
            },
            _adminId);

        Assert.Equal("Approved", approved.Status);

        var ledgerEntries = await _dbContext.CommerceHubFinancialTransactions
            .Where(entry => entry.ReferenceId == paymentRequest.PaymentRequestId)
            .ToListAsync();

        Assert.Equal(2, ledgerEntries.Count);

        var paymentRequestAfter = await _dbContext.Set<CommerceHubVendorPaymentRequest>()
            .FirstAsync(pr => pr.Id == paymentRequest.PaymentRequestId);

        Assert.Equal(CommerceHubVendorPaymentRequestStatus.Approved, paymentRequestAfter.Status);
    }

    [Fact]
    public async Task ApprovePaymentRequestAsync_SameAdminReplay_ShouldRemainIdempotent()
    {
        var vendorId = await SeedVendorAsync("Replay Vendor");
        var paymentMethod = await CreatePaymentMethodAsync();

        var debtTransaction = SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            450m,
            CommerceHubFinancialReferenceType.Order,
            Guid.NewGuid());
        SeedPostedCommerceHubJournal(vendorId, debtTransaction);
        SeedHealthyReconciliationReport(vendorId);

        await _dbContext.SaveChangesAsync();

        var paymentRequest = await _service.CreateVendorPaymentRequestAsync(
            vendorId,
            CreatePaymentRequestForm(paymentMethod.Id, 200m, "payment-create-2", "TX-002"));

        var first = await _service.ApprovePaymentRequestAsync(
            paymentRequest.PaymentRequestId,
            new SuperAdminApproveVendorPaymentRequestDto
            {
                Reason = "Verified Vodafone Cash receipt."
            },
            _adminId);

        var replay = await _service.ApprovePaymentRequestAsync(
            paymentRequest.PaymentRequestId,
            new SuperAdminApproveVendorPaymentRequestDto
            {
                Reason = "Verified Vodafone Cash receipt."
            },
            _adminId);

        var ledgerEntries = await _dbContext.CommerceHubFinancialTransactions
            .Where(entry => entry.ReferenceId == paymentRequest.PaymentRequestId)
            .ToListAsync();

        Assert.Equal(first.PaymentRequestId, replay.PaymentRequestId);
        Assert.Equal("Approved", replay.Status);
        Assert.Equal(2, ledgerEntries.Count);
    }

    [Fact]
    public async Task CreateVendorPaymentRequestAsync_ShouldRejectAmountGreaterThanDebt()
    {
        var vendorId = await SeedVendorAsync("Debt Validation Vendor");
        var paymentMethod = await CreatePaymentMethodAsync();

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            120m,
            CommerceHubFinancialReferenceType.Order,
            Guid.NewGuid());

        await _dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CommerceHubVendorFinancialControlException>(() =>
            _service.CreateVendorPaymentRequestAsync(
                vendorId,
                CreatePaymentRequestForm(paymentMethod.Id, 200m, "payment-create-3", "TX-003")));

        Assert.Equal("PAYMENT_AMOUNT_EXCEEDS_DEBT", exception.Code);
        Assert.Equal(400, exception.StatusCode);
    }

    [Fact]
    public async Task RejectPaymentRequestAsync_ShouldNotWriteSettlementLedgerEntries()
    {
        var vendorId = await SeedVendorAsync("Rejected Vendor");
        var paymentMethod = await CreatePaymentMethodAsync();

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            180m,
            CommerceHubFinancialReferenceType.Order,
            Guid.NewGuid());

        await _dbContext.SaveChangesAsync();

        var paymentRequest = await _service.CreateVendorPaymentRequestAsync(
            vendorId,
            CreatePaymentRequestForm(paymentMethod.Id, 100m, "payment-create-4", "TX-004"));

        var rejected = await _service.RejectPaymentRequestAsync(
            paymentRequest.PaymentRequestId,
            new SuperAdminRejectVendorPaymentRequestDto
            {
                Reason = "Proof did not match transfer details."
            },
            _adminId);

        var ledgerEntries = await _dbContext.CommerceHubFinancialTransactions
            .Where(entry => entry.ReferenceId == paymentRequest.PaymentRequestId)
            .ToListAsync();

        Assert.Equal("Rejected", rejected.Status);
        Assert.Empty(ledgerEntries);
    }

    [Fact]
    public async Task GetVendorDashboardAsync_WhenPersistedVendorCurrencyMissing_ShouldReturnServerError()
    {
        var vendorId = await SeedVendorAsync("Missing Currency Vendor", string.Empty);

        var exception = await Assert.ThrowsAsync<CommerceHubVendorFinancialControlException>(() =>
            _service.GetVendorDashboardAsync(vendorId));

        Assert.Equal("PAYMENT_CURRENCY_REQUIRED", exception.Code);
        Assert.Equal("Payment currency is required.", exception.Message);
        Assert.Equal(StatusCodes.Status500InternalServerError, exception.StatusCode);
    }

    [Fact]
    public async Task GetVendorDashboardAsync_WhenPersistedVendorCurrencyInvalid_ShouldReturnServerError()
    {
        var vendorId = await SeedVendorAsync("Invalid Currency Vendor", "bad-currency");

        var exception = await Assert.ThrowsAsync<CommerceHubVendorFinancialControlException>(() =>
            _service.GetVendorDashboardAsync(vendorId));

        Assert.Equal("PAYMENT_CURRENCY_INVALID", exception.Code);
        Assert.Equal("Payment currency is invalid.", exception.Message);
        Assert.Equal(StatusCodes.Status500InternalServerError, exception.StatusCode);
    }

    [Fact]
    public async Task CreateVendorPaymentRequestAsync_WhenRequestCurrencyMissing_ShouldReturnClientError()
    {
        var vendorId = await SeedVendorAsync("Request Currency Missing Vendor");
        var paymentMethod = await CreatePaymentMethodAsync();

        // Seed vendor debt so amount validation passes.
        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            500m,
            CommerceHubFinancialReferenceType.Order,
            Guid.NewGuid());
        await _dbContext.SaveChangesAsync();

        var request = CreatePaymentRequestForm(paymentMethod.Id, 100m, "payment-missing-currency", "TX-MISSING");
        request.Currency = string.Empty;

        var exception = await Assert.ThrowsAsync<CommerceHubVendorFinancialControlException>(() =>
            _service.CreateVendorPaymentRequestAsync(vendorId, request));

        Assert.Equal("PAYMENT_CURRENCY_REQUIRED", exception.Code);
        Assert.Equal("Payment currency is required.", exception.Message);
        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task CreateVendorPaymentRequestAsync_WhenRequestCurrencyInvalid_ShouldReturnClientError()
    {
        var vendorId = await SeedVendorAsync("Request Currency Invalid Vendor");
        var paymentMethod = await CreatePaymentMethodAsync();

        // Seed vendor debt so amount validation passes.
        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            500m,
            CommerceHubFinancialReferenceType.Order,
            Guid.NewGuid());
        await _dbContext.SaveChangesAsync();

        var request = CreatePaymentRequestForm(paymentMethod.Id, 100m, "payment-invalid-currency", "TX-INVALID");
        request.Currency = "bad-currency";

        var exception = await Assert.ThrowsAsync<CommerceHubVendorFinancialControlException>(() =>
            _service.CreateVendorPaymentRequestAsync(vendorId, request));

        Assert.Equal("PAYMENT_CURRENCY_INVALID", exception.Code);
        Assert.Equal("Payment currency is invalid.", exception.Message);
        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task CreateVendorPaymentRequestAsync_WhenRequestCurrencyIsNotEgp_ShouldReturnClientError()
    {
        var vendorId = await SeedVendorAsync("Request Currency Unsupported Vendor");
        var paymentMethod = await CreatePaymentMethodAsync();

        SeedTransaction(
            CommerceHubFinancialEntityType.Vendor,
            vendorId,
            CommerceHubFinancialTransactionType.Commission,
            CommerceHubFinancialTransactionDirection.Debit,
            500m,
            CommerceHubFinancialReferenceType.Order,
            Guid.NewGuid());
        await _dbContext.SaveChangesAsync();

        var request = CreatePaymentRequestForm(paymentMethod.Id, 100m, "payment-unsupported-currency", "TX-UNSUPPORTED");
        request.Currency = "USD";

        var exception = await Assert.ThrowsAsync<CommerceHubVendorFinancialControlException>(() =>
            _service.CreateVendorPaymentRequestAsync(vendorId, request));

        Assert.Equal("PAYMENT_CURRENCY_NOT_SUPPORTED", exception.Code);
        Assert.Equal("Only EGP is supported for CommerceHub vendor repayments.", exception.Message);
        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task FakeR2StorageService_GetObjectBytesAsync_ShouldThrowOnETagMismatch()
    {
        var fakeStorage = new FakeR2StorageService();
        
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await fakeStorage.GetObjectBytesAsync(
                "test-object-key",
                1024,
                "wrong-etag"));

        Assert.Contains("ETag mismatch", exception.Message);
        Assert.Contains("test-etag", exception.Message);
    }

    [Fact]
    public async Task FakeR2StorageService_GetObjectBytesAsync_ShouldSucceedWithMatchingETag()
    {
        var fakeStorage = new FakeR2StorageService();
        
        var metadata = await fakeStorage.GetObjectMetadataAsync("test-object-key");
        var bytes = await fakeStorage.GetObjectBytesAsync(
            "test-object-key",
            1024,
            metadata.ETag);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public async Task FakeR2StorageService_GetObjectBytesAsync_ShouldSucceedWithNullETag()
    {
        var fakeStorage = new FakeR2StorageService();
        
        var bytes = await fakeStorage.GetObjectBytesAsync(
            "test-object-key",
            1024,
            null);

        Assert.NotNull(bytes);
        Assert.NotEmpty(bytes);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private async Task<Guid> SeedVendorAsync(string name, string currency = "EGP")
    {
        var vendorId = Guid.NewGuid();
        _dbContext.Tenants.Add(new Tenant
        {
            Id = vendorId,
            Name = name,
            Currency = currency,
            IsActive = true,
            Status = TenantStatus.Active
        });

        await _dbContext.SaveChangesAsync();
        return vendorId;
    }

    private async Task<SuperAdminVendorPaymentMethodDto> CreatePaymentMethodAsync()
    {
        return await _service.CreatePaymentMethodAsync(
            new SuperAdminCreateVendorPaymentMethodRequestDto
            {
                Name = "InstaPay",
                DestinationValue = "finance@commercehub",
                Instructions = "Send the amount, then upload your transfer proof.",
                IsActive = true
            },
            _adminId);
    }

    private static CommerceHubCreateVendorPaymentRequestFormDto CreatePaymentRequestForm(
        Guid methodId,
        decimal amount,
        string idempotencyKey,
        string transferReference)
    {
        return new CommerceHubCreateVendorPaymentRequestFormDto
        {
            MethodId = methodId,
            Amount = amount,
            Currency = "EGP",
            TransferReference = transferReference,
            IdempotencyKey = idempotencyKey,
            ProofFile = CreateProofFile($"{idempotencyKey}.jpg", "image/jpeg", 1024)
        };
    }

    private static IFormFile CreateProofFile(string fileName, string contentType, int sizeBytes)
    {
        var bytes = new byte[Math.Max(sizeBytes, 4)];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        bytes[3] = 0xE0;
        var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, bytes.Length, "ProofFile", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

        return file;
    }

    private CommerceHubFinancialTransaction SeedTransaction(
        CommerceHubFinancialEntityType entityType,
        Guid? entityId,
        CommerceHubFinancialTransactionType transactionType,
        CommerceHubFinancialTransactionDirection direction,
        decimal amount,
        CommerceHubFinancialReferenceType referenceType,
        Guid referenceId)
    {
        var transaction = CommerceHubFinancialTransaction.Create(
            entityType,
            entityId,
            transactionType,
            direction,
            amount,
            "EGP",
            referenceType,
            referenceId,
            $"{transactionType} {direction}");

        _dbContext.CommerceHubFinancialTransactions.Add(transaction);
        return transaction;
    }

    private void SeedPostedCommerceHubJournal(Guid tenantId, CommerceHubFinancialTransaction transaction)
    {
        _dbContext.JournalEntries.Add(JournalEntry.Create(
            tenantId,
            $"JE-CH-{Guid.NewGuid():N}"[..18],
            DateTime.UtcNow,
            "CommerceHubFinancialTransaction",
            transaction.Id,
            transaction.ReferenceId.ToString("D"),
            $"GL coverage for CommerceHub transaction {transaction.Id:D}",
            $"COMMERCEHUB_LEDGER_POSTING:{tenantId:D}:{transaction.Id:D}",
            _adminId));
    }

    private void SeedHealthyReconciliationReport(Guid vendorId)
    {
        _dbContext.CommerceHubFinancialReconciliationReports.Add(new CommerceHubFinancialReconciliationReport
        {
            Id = Guid.NewGuid(),
            TimestampUtc = DateTime.UtcNow,
            Status = "Healthy",
            MismatchCount = 0,
            DetailsJson = $$"""{"checkedVendorIds":["{{vendorId:D}}"],"affectedVendorIds":[]}"""
        });
    }

    private sealed class FakeR2StorageService : IR2StorageService
    {
        private const string TestETag = "test-etag";

        public Task<R2UploadUrlResult> CreateUploadUrlAsync(
            string contentType,
            string path,
            TimeSpan expiresIn,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new R2UploadUrlResult(
                $"https://upload.example.com/{path}",
                "proof.jpg",
                $"{path}/proof.jpg",
                "PUT",
                contentType));
        }

        public Task<string> CreateDownloadUrlAsync(
            string objectKey,
            TimeSpan expiresIn,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult($"https://download.example.com/{objectKey}");
        }

        public Task<R2ObjectMetadata> GetObjectMetadataAsync(
            string objectKey,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new R2ObjectMetadata("image/jpeg", 1024, null, TestETag));
        }

        public Task<byte[]> GetObjectBytesAsync(
            string objectKey,
            int maxBytes,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
        }

        public Task<byte[]> GetObjectBytesAsync(
            string objectKey,
            int maxBytes,
            string? ifMatchETag,
            CancellationToken cancellationToken = default)
        {
            if (ifMatchETag != null && ifMatchETag != TestETag)
            {
                throw new InvalidOperationException($"ETag mismatch: expected '{TestETag}', got '{ifMatchETag}'");
            }

            return Task.FromResult(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
        }

        public Task DeleteObjectAsync(
            string objectKey,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<R2ObjectMetadata> CopyObjectAsync(
            string sourceObjectKey,
            string destinationObjectKey,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new R2ObjectMetadata(contentType, 1024, null, TestETag));
        }

        public Task<R2DownloadObjectResult> DownloadObjectAsync(
            string objectKey,
            string? ifMatchETag,
            CancellationToken cancellationToken = default)
        {
            if (ifMatchETag != null && ifMatchETag != TestETag)
            {
                throw new InvalidOperationException($"ETag mismatch: expected '{TestETag}', got '{ifMatchETag}'");
            }

            return Task.FromResult(new R2DownloadObjectResult(
                new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }),
                "image/jpeg",
                4,
                TestETag));
        }

        public Task<R2UploadResult> UploadFileAsync(
            Stream file,
            string contentType,
            string path,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new R2UploadResult(
                $"https://cdn.example.com/{path}/proof.jpg",
                "proof.jpg",
                $"{path}/proof.jpg"));
        }
    }
}
