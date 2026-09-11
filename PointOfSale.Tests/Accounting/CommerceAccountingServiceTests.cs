using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Core.Interfaces.Treasury;
using PointOfSale.Core.Models.Accounting;
using PointOfSale.Core.Models.Refunds;
using PointOfSale.Infrastructure.Accounting;
using PointOfSale.Infrastructure.Data;
using Xunit;
using PaymentMethodEnum = PointOfSale.Core.Enums.PaymentMethod;

namespace PointOfSale.Tests.Accounting;

/// <summary>
/// Unit tests for Phase 3: Commerce-to-Ledger Integration.
/// </summary>
public class CommerceAccountingServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly Mock<IFinancialPostingService> _mockPostingService;
    private readonly Mock<IAccountResolver> _mockAccountResolver;
    private readonly Mock<ICashboxLedgerAccountService> _mockCashboxLedgerAccountService;
    private readonly CommerceAccountingService _service;
    private readonly Guid _tenantId = Guid.NewGuid();
    
    // Standard account IDs for testing
    private readonly Guid _cashboxId = Guid.NewGuid();
    private readonly Guid _visaClearingId = Guid.NewGuid();
    private readonly Guid _stripeClearingId = Guid.NewGuid();
    private readonly Guid _codClearingId = Guid.NewGuid();
    private readonly Guid _walletClearingId = Guid.NewGuid();
    private readonly Guid _posSalesId = Guid.NewGuid();
    private readonly Guid _ecomSalesId = Guid.NewGuid();
    private readonly Guid _inventoryId = Guid.NewGuid();
    private readonly Guid _inputTaxReceivableId = Guid.NewGuid();
    private readonly Guid _salesTaxPayableId = Guid.NewGuid();
    private readonly Guid _treasurySuspenseId = Guid.NewGuid();
    private readonly Guid _marketplaceVendorPayableId = Guid.NewGuid();
    private readonly Guid _marketplaceVendorReceivableId = Guid.NewGuid();
    private readonly Guid _cogsId = Guid.NewGuid();
    private readonly Guid _bankId = Guid.NewGuid();
    private readonly Guid _gatewayFeesId = Guid.NewGuid();
    private readonly Guid _accountsReceivableId = Guid.NewGuid();
    private readonly Guid _supplierPayablesId = Guid.NewGuid();
    private readonly Guid _openingBalanceEquityId = Guid.NewGuid();
    
    public CommerceAccountingServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        
        _db = new ApplicationDbContext(options);
        
        _mockPostingService = new Mock<IFinancialPostingService>();
        _mockAccountResolver = new Mock<IAccountResolver>();
        _mockCashboxLedgerAccountService = new Mock<ICashboxLedgerAccountService>();
        
        // Setup default account resolution
        SetupAccountResolver();
        SetupCashboxAccountResolver();
        
        // Setup default posting behavior
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PostingRequest req, CancellationToken ct) => JournalEntry.Create(
                req.TenantId,
                $"JE-2026-{Guid.NewGuid().ToString()[..6]}",
                req.JournalDate,
                req.SourceType,
                req.SourceId,
                req.SourceReference,
                req.Description,
                req.IdempotencyKey,
                req.UserId));
        
        var logger = Mock.Of<ILogger<CommerceAccountingService>>();
        _service = new CommerceAccountingService(
            _mockPostingService.Object,
            _mockAccountResolver.Object,
            _mockCashboxLedgerAccountService.Object,
            logger);
    }
    
    private void SetupAccountResolver()
    {
        var accounts = new Dictionary<string, Account>
        {
            [AccountCodes.CashboxMain] = CreateAccount(_cashboxId, AccountCodes.CashboxMain),
            [AccountCodes.VisaClearing] = CreateAccount(_visaClearingId, AccountCodes.VisaClearing),
            [AccountCodes.StripeClearing] = CreateAccount(_stripeClearingId, AccountCodes.StripeClearing),
            [AccountCodes.CodClearing] = CreateAccount(_codClearingId, AccountCodes.CodClearing),
            [AccountCodes.WalletClearing] = CreateAccount(_walletClearingId, AccountCodes.WalletClearing),
            [AccountCodes.PosSales] = CreateAccount(_posSalesId, AccountCodes.PosSales),
            [AccountCodes.EcommerceSales] = CreateAccount(_ecomSalesId, AccountCodes.EcommerceSales),
            [AccountCodes.Inventory] = CreateAccount(_inventoryId, AccountCodes.Inventory),
            [AccountCodes.InputTaxReceivable] = CreateAccount(_inputTaxReceivableId, AccountCodes.InputTaxReceivable),
            [AccountCodes.SalesTaxPayable] = CreateAccount(_salesTaxPayableId, AccountCodes.SalesTaxPayable),
            [AccountCodes.TreasurySuspense] = CreateAccount(_treasurySuspenseId, AccountCodes.TreasurySuspense),
            [AccountCodes.MarketplaceVendorPayable] = CreateAccount(_marketplaceVendorPayableId, AccountCodes.MarketplaceVendorPayable),
            [AccountCodes.MarketplaceVendorReceivable] = CreateAccount(_marketplaceVendorReceivableId, AccountCodes.MarketplaceVendorReceivable),
            [AccountCodes.CostOfGoodsSold] = CreateAccount(_cogsId, AccountCodes.CostOfGoodsSold),
            [AccountCodes.BankAccount] = CreateAccount(_bankId, AccountCodes.BankAccount),
            [AccountCodes.GatewayFees] = CreateAccount(_gatewayFeesId, AccountCodes.GatewayFees),
            [AccountCodes.AccountsReceivable] = CreateAccount(_accountsReceivableId, AccountCodes.AccountsReceivable),
            [AccountCodes.SupplierPayables] = CreateAccount(_supplierPayablesId, AccountCodes.SupplierPayables),
            [AccountCodes.OpeningBalanceEquity] = CreateAccount(_openingBalanceEquityId, AccountCodes.OpeningBalanceEquity)
        };
        
        _mockAccountResolver
            .Setup(x => x.GetByCodesAsync(
                It.Is<Guid>(t => t == _tenantId),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantId, IEnumerable<string> codes, CancellationToken ct) =>
                codes.ToDictionary(c => c, c => accounts[c]));
        
        _mockAccountResolver
            .Setup(x => x.GetByCodeAsync(
                It.Is<Guid>(t => t == _tenantId),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantId, string code, CancellationToken ct) => accounts[code]);

        _mockAccountResolver
            .Setup(x => x.GetPaymentAccountIdAsync(
                It.Is<Guid>(t => t == _tenantId),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid tenantId, string paymentMethodCode, CancellationToken ct) => paymentMethodCode switch
            {
                PaymentMethodCodes.Stripe => _stripeClearingId,
                PaymentMethodCodes.Card => _visaClearingId,
                PaymentMethodCodes.Cod => _codClearingId,
                PaymentMethodCodes.Wallet => _walletClearingId,
                _ => _stripeClearingId
            });
    }

    private void SetupCashboxAccountResolver()
    {
        var defaultCashbox = new CashboxLedgerAccountInfo(
            CashboxId: null,
            AccountId: _cashboxId,
            AccountCode: AccountCodes.CashboxMain,
            Name: "Main Cashbox",
            IsDefault: true,
            WarehouseId: null,
            Location: null);

        _mockCashboxLedgerAccountService
            .Setup(x => x.ResolveWarehouseCashboxAccountAsync(
                It.Is<Guid>(t => t == _tenantId),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaultCashbox);

        _mockCashboxLedgerAccountService
            .Setup(x => x.ResolveDefaultCashboxAccountAsync(
                It.Is<Guid>(t => t == _tenantId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaultCashbox);
    }
    
    private Account CreateAccount(Guid id, string code)
    {
        var account = Account.Create(
            _tenantId,
            code,
            $"Test Account {code}",
            AccountType.Asset);
        account.Id = id;
        return account;
    }
    
    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task PostCustomerPaymentAsync_MixedPayment_DebitsExactFundingAndCreditsReceivable()
    {
        PostingRequest? capturedRequest = null;
        _mockPostingService
            .Setup(service => service.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync((PostingRequest request, CancellationToken _) => JournalEntry.Create(
                request.TenantId,
                "JE-CUSTOMER-PAYMENT",
                request.JournalDate,
                request.SourceType,
                request.SourceId,
                request.SourceReference,
                request.Description,
                request.IdempotencyKey,
                request.UserId));
        var payment = new CustomerPayment
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            CustomerId = Guid.NewGuid(),
            PaymentDate = DateTime.UtcNow,
            Amount = 100m,
            PaymentMethod = PaymentMethodEnum.Mixed,
            CashAmount = 40m,
            CardAmount = 60m,
            BankTransferAmount = 0m,
            IdempotencyKey = "customer-payment-test",
            ReferenceNumber = "CPAY-TEST"
        };

        var result = await _service.PostCustomerPaymentAsync(payment);

        Assert.True(result.Success);
        Assert.NotNull(capturedRequest);
        Assert.Equal("CustomerPayment", capturedRequest.SourceType);
        Assert.Equal($"CUSTOMER_PAYMENT:{_tenantId}:{payment.Id}", capturedRequest.IdempotencyKey);
        Assert.Contains(capturedRequest.Lines, line =>
            line.AccountId == _cashboxId && line.DebitAmount == 40m && line.CreditAmount == 0m);
        Assert.Contains(capturedRequest.Lines, line =>
            line.AccountId == _visaClearingId && line.DebitAmount == 60m && line.CreditAmount == 0m);
        Assert.Contains(capturedRequest.Lines, line =>
            line.AccountId == _accountsReceivableId && line.DebitAmount == 0m && line.CreditAmount == 100m);
        Assert.Equal(capturedRequest.Lines.Sum(line => line.DebitAmount), capturedRequest.Lines.Sum(line => line.CreditAmount));
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // POS CASH SALE TESTS
    // ═══════════════════════════════════════════════════════════════════════════
    
    [Fact]
    public async Task PostPosSaleAsync_CashSale_DebitsCashboxCreditsSales()
    {
        // Arrange
        var invoice = CreateSalesInvoice(PaymentMethodEnum.Cash, 100m);
        
        PostingRequest? capturedRequest = null;
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(CreateJournal());
        
        // Act
        var result = await _service.PostPosSaleAsync(invoice);
        
        // Assert
        Assert.True(result.Success);
        Assert.NotNull(capturedRequest);
        Assert.Equal($"SALE:{_tenantId}:{invoice.Id}", capturedRequest!.IdempotencyKey);
        
        // Verify debit to Cashbox
        var debitLine = capturedRequest.Lines.FirstOrDefault(l => l.DebitAmount > 0);
        Assert.NotNull(debitLine);
        Assert.Equal(_cashboxId, debitLine!.AccountId);
        Assert.Equal(100m, debitLine.DebitAmount);
        
        // Verify credit to POS Sales
        var creditLine = capturedRequest.Lines.FirstOrDefault(l => l.CreditAmount > 0);
        Assert.NotNull(creditLine);
        Assert.Equal(_posSalesId, creditLine!.AccountId);
        Assert.Equal(100m, creditLine.CreditAmount);
    }
    
    [Fact]
    public async Task PostPosSaleAsync_CardSale_DebitsVisaClearingCreditsSales()
    {
        // Arrange
        var invoice = CreateSalesInvoice(PaymentMethodEnum.Card, 250m);
        
        PostingRequest? capturedRequest = null;
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(CreateJournal());
        
        // Act
        var result = await _service.PostPosSaleAsync(invoice);
        
        // Assert
        Assert.True(result.Success);
        
        var debitLine = capturedRequest!.Lines.FirstOrDefault(l => l.DebitAmount > 0);
        Assert.Equal(_visaClearingId, debitLine!.AccountId);
        Assert.Equal(250m, debitLine.DebitAmount);
    }
    
    [Fact]
    public async Task PostPosSaleAsync_MixedSale_DebitsBothCashboxAndClearing()
    {
        // Arrange
        var invoice = CreateSalesInvoice(PaymentMethodEnum.Mixed, 300m);
        invoice.CashAmount = 100m;
        invoice.CardAmount = 200m;
        
        PostingRequest? capturedRequest = null;
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(CreateJournal());
        
        // Act
        var result = await _service.PostPosSaleAsync(invoice);
        
        // Assert
        Assert.True(result.Success);
        
        // Should have 3 lines: 2 debits, 1 credit
        Assert.Equal(3, capturedRequest!.Lines.Count);
        
        var cashDebit = capturedRequest.Lines.First(l => l.AccountId == _cashboxId);
        Assert.Equal(100m, cashDebit.DebitAmount);
        
        var cardDebit = capturedRequest.Lines.First(l => l.AccountId == _visaClearingId);
        Assert.Equal(200m, cardDebit.DebitAmount);
        
        var credit = capturedRequest.Lines.First(l => l.CreditAmount > 0);
        Assert.Equal(300m, credit.CreditAmount);
    }

    [Fact]
    public async Task PostPosSaleAsync_WithTax_SeparatesRevenueAndSalesTaxPayable()
    {
        var invoice = CreateSalesInvoice(PaymentMethodEnum.Credit, 105m);
        invoice.TaxAmount = 5m;
        PostingRequest? capturedRequest = null;
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(CreateJournal());

        var result = await _service.PostPosSaleAsync(invoice);

        Assert.True(result.Success);
        Assert.Contains(capturedRequest!.Lines, line => line.AccountId == _posSalesId && line.CreditAmount == 100m);
        Assert.Contains(capturedRequest.Lines, line => line.AccountId == _salesTaxPayableId && line.CreditAmount == 5m);
        Assert.Contains(capturedRequest.Lines, line => line.AccountId == _accountsReceivableId && line.DebitAmount == 105m);
    }

    [Fact]
    public async Task PostPosPurchaseAsync_WithTax_SeparatesInventoryAndInputTax()
    {
        var invoice = new PurchaseInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            InvoiceNumber = "PUR-TAX",
            InvoiceDate = DateTime.UtcNow,
            PaymentMethod = PaymentMethodEnum.Credit,
            Subtotal = 100m,
            TaxAmount = 5m,
            TotalAmount = 105m,
            PaidAmount = 0m
        };
        PostingRequest? capturedRequest = null;
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(CreateJournal());

        var result = await _service.PostPosPurchaseAsync(invoice);

        Assert.True(result.Success);
        Assert.Contains(capturedRequest!.Lines, line => line.AccountId == _inventoryId && line.DebitAmount == 100m);
        Assert.Contains(capturedRequest.Lines, line => line.AccountId == _inputTaxReceivableId && line.DebitAmount == 5m);
        Assert.Contains(capturedRequest.Lines, line => line.AccountId == _supplierPayablesId && line.CreditAmount == 105m);
    }

    [Fact]
    public async Task PostPosRefundAsync_WithTax_ReversesRevenueAndSalesTaxPayable()
    {
        var returnObj = new Return
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ReturnNumber = "SR-TAX",
            ReturnDate = DateTime.UtcNow,
            ReturnType = ReturnType.SalesReturn,
            Subtotal = 100m,
            TaxAmount = 5m,
            TotalAmount = 105m
        };
        PostingRequest? capturedRequest = null;
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(CreateJournal());

        var result = await _service.PostPosRefundAsync(returnObj, RefundAllocation.AccountCredit);

        Assert.True(result.Success);
        Assert.Contains(capturedRequest!.Lines, line => line.AccountId == _posSalesId && line.DebitAmount == 100m);
        Assert.Contains(capturedRequest.Lines, line => line.AccountId == _salesTaxPayableId && line.DebitAmount == 5m);
        Assert.Contains(capturedRequest.Lines, line => line.AccountId == _accountsReceivableId && line.CreditAmount == 105m);
    }

    [Fact]
    public async Task PostPurchaseReturnAsync_WithTax_ReversesInventoryAndInputTax()
    {
        var returnObj = new Return
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            ReturnNumber = "PR-TAX",
            ReturnDate = DateTime.UtcNow,
            ReturnType = ReturnType.PurchaseReturn,
            Subtotal = 100m,
            TaxAmount = 5m,
            TotalAmount = 105m
        };
        PostingRequest? capturedRequest = null;
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(CreateJournal());

        var result = await _service.PostPurchaseReturnAsync(returnObj);

        Assert.True(result.Success);
        Assert.Contains(capturedRequest!.Lines, line => line.AccountId == _supplierPayablesId && line.DebitAmount == 105m);
        Assert.Contains(capturedRequest.Lines, line => line.AccountId == _inventoryId && line.CreditAmount == 100m);
        Assert.Contains(capturedRequest.Lines, line => line.AccountId == _inputTaxReceivableId && line.CreditAmount == 5m);
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // E-COMMERCE PAYMENT TESTS
    // ═══════════════════════════════════════════════════════════════════════════
    
    [Fact]
    public async Task PostOnlinePaymentCapturedAsync_Stripe_DebitsStripeClearingCreditsEcomSales()
    {
        // Arrange
        var order = CreateOrder(500m);
        
        PostingRequest? capturedRequest = null;
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(CreateJournal());
        
        // Act
        var result = await _service.PostOnlinePaymentCapturedAsync(order, "pi_test123", "STRIPE");
        
        // Assert
        Assert.True(result.Success);
        Assert.Contains("pi_test123", capturedRequest!.IdempotencyKey);
        
        var debitLine = capturedRequest.Lines.First(l => l.DebitAmount > 0);
        Assert.Equal(_stripeClearingId, debitLine.AccountId);
        Assert.Equal(500m, debitLine.DebitAmount);
        
        var creditLine = capturedRequest.Lines.First(l => l.CreditAmount > 0);
        Assert.Equal(_ecomSalesId, creditLine.AccountId);
    }
    
    [Fact]
    public async Task PostCodDeliveredAsync_DebitsCodClearingCreditsEcomSales()
    {
        // Arrange
        var order = CreateOrder(350m);
        order.DeliveredAt = DateTime.UtcNow;
        
        PostingRequest? capturedRequest = null;
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(CreateJournal());
        
        // Act
        var result = await _service.PostCodDeliveredAsync(order);
        
        // Assert
        Assert.True(result.Success);
        Assert.Equal($"COD_DELIVERED:{_tenantId}:{order.Id}", capturedRequest!.IdempotencyKey);
        
        var debitLine = capturedRequest.Lines.First(l => l.DebitAmount > 0);
        Assert.Equal(_codClearingId, debitLine.AccountId);
    }
    
    [Fact]
    public async Task PostWalletApprovedAsync_DebitsWalletClearingCreditsEcomSales()
    {
        // Arrange
        var order = CreateOrder(200m);
        order.PaidAt = DateTime.UtcNow;
        
        PostingRequest? capturedRequest = null;
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(CreateJournal());
        
        // Act
        var result = await _service.PostWalletApprovedAsync(order);
        
        // Assert
        Assert.True(result.Success);
        
        var debitLine = capturedRequest!.Lines.First(l => l.DebitAmount > 0);
        Assert.Equal(_walletClearingId, debitLine.AccountId);
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // SETTLEMENT TESTS
    // ═══════════════════════════════════════════════════════════════════════════
    
    [Fact]
    public async Task PostGatewaySettlementAsync_DebitsBankAndFeesCreditsClearing()
    {
        // Arrange
        var grossAmount = 1000m;
        var fees = 29m; // 2.9% Stripe fee
        var netAmount = grossAmount - fees;
        
        PostingRequest? capturedRequest = null;
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(CreateJournal());
        
        // Act
        var result = await _service.PostGatewaySettlementAsync(
            _tenantId, "STRIPE", grossAmount, fees, "SETTLE-001");
        
        // Assert
        Assert.True(result.Success);
        
        // Should have 3 lines: Bank debit, Fees debit, Clearing credit
        Assert.Equal(3, capturedRequest!.Lines.Count);
        
        var bankDebit = capturedRequest.Lines.First(l => l.AccountId == _bankId);
        Assert.Equal(netAmount, bankDebit.DebitAmount);
        
        var feesDebit = capturedRequest.Lines.First(l => l.AccountId == _gatewayFeesId);
        Assert.Equal(fees, feesDebit.DebitAmount);
        
        var clearingCredit = capturedRequest.Lines.First(l => l.CreditAmount > 0);
        Assert.Equal(_stripeClearingId, clearingCredit.AccountId);
        Assert.Equal(grossAmount, clearingCredit.CreditAmount);
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // IDEMPOTENCY TESTS
    // ═══════════════════════════════════════════════════════════════════════════
    
    [Fact]
    public async Task PostPosSaleAsync_GeneratesCorrectIdempotencyKey()
    {
        // Arrange
        var invoice = CreateSalesInvoice(PaymentMethodEnum.Cash, 100m);
        
        PostingRequest? capturedRequest = null;
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(CreateJournal());
        
        // Act
        await _service.PostPosSaleAsync(invoice);
        
        // Assert
        Assert.Equal($"SALE:{_tenantId}:{invoice.Id}", capturedRequest!.IdempotencyKey);
    }

    [Fact]
    public async Task PostPosSaleAsync_WithOperationKey_AppendsOperationKeyToIdempotencyKey()
    {
        // Arrange
        var invoice = CreateSalesInvoice(PaymentMethodEnum.Cash, 100m);
        invoice.Items =
        [
            new SalesInvoiceItem
            {
                ProductId = Guid.NewGuid(),
                ProductVariantId = Guid.NewGuid(),
                Quantity = 1m,
                ProductVariant = new ProductVariant
                {
                    TenantId = _tenantId,
                    CostPrice = 40m
                },
                Product = new Product
                {
                    TenantId = _tenantId,
                    Name = "Test product"
                }
            }
        ];
        const string operationKey = "update-req-1";

        var capturedRequests = new List<PostingRequest>();
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((req, ct) => capturedRequests.Add(req))
            .ReturnsAsync(CreateJournal());

        // Act
        await _service.PostPosSaleAsync(invoice, operationKey: operationKey);

        // Assert
        Assert.Collection(
            capturedRequests,
            saleRequest => Assert.Equal($"SALE:{_tenantId}:{invoice.Id}:{operationKey}", saleRequest.IdempotencyKey),
            cogsRequest => Assert.Equal($"COGS:{_tenantId}:{invoice.Id}:{operationKey}", cogsRequest.IdempotencyKey));
    }
    
    [Fact]
    public async Task PostOnlinePaymentCapturedAsync_IncludesPaymentIntentInIdempotencyKey()
    {
        // Arrange
        var order = CreateOrder(500m);
        var paymentIntentId = "pi_abc123xyz";
        
        PostingRequest? capturedRequest = null;
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((req, ct) => capturedRequest = req)
            .ReturnsAsync(CreateJournal());
        
        // Act
        await _service.PostOnlinePaymentCapturedAsync(order, paymentIntentId, "STRIPE");
        
        // Assert
        Assert.Equal($"ONLINE_PAYMENT:{_tenantId}:{order.Id}:{paymentIntentId}", capturedRequest!.IdempotencyKey);
    }

    [Fact]
    public async Task PostSupplierOpeningBalanceAsync_UsesOpeningBalanceEntryIdForIdempotency()
    {
        var openingBalance = CreateSupplierOpeningBalance(Guid.NewGuid(), creditAmount: 125m);

        PostingRequest? capturedRequest = null;
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(CreateJournal());

        var result = await _service.PostSupplierOpeningBalanceAsync(openingBalance);

        Assert.True(result.Success);
        Assert.NotNull(capturedRequest);
        Assert.Equal($"SUPPLIER_OPENING_BALANCE:{_tenantId}:{openingBalance.Id}", capturedRequest!.IdempotencyKey);
    }

    [Fact]
    public async Task PostSupplierOpeningBalanceAsync_AllowsDistinctEntriesForSameSupplier()
    {
        var supplierId = Guid.NewGuid();
        var openingBalances = new[]
        {
            CreateSupplierOpeningBalance(supplierId, creditAmount: 125m),
            CreateSupplierOpeningBalance(supplierId, creditAmount: 75m)
        };

        var capturedRequests = new List<PostingRequest>();
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((req, _) => capturedRequests.Add(req))
            .ReturnsAsync(CreateJournal());

        foreach (var openingBalance in openingBalances)
        {
            var result = await _service.PostSupplierOpeningBalanceAsync(openingBalance);
            Assert.True(result.Success);
        }

        Assert.Collection(
            capturedRequests,
            first => Assert.Equal(
                $"SUPPLIER_OPENING_BALANCE:{_tenantId}:{openingBalances[0].Id}",
                first.IdempotencyKey),
            second => Assert.Equal(
                $"SUPPLIER_OPENING_BALANCE:{_tenantId}:{openingBalances[1].Id}",
                second.IdempotencyKey));
        Assert.NotEqual(capturedRequests[0].IdempotencyKey, capturedRequests[1].IdempotencyKey);
    }

    [Fact]
    public async Task PostCustomerOpeningBalanceAsync_PostsReceivableAgainstOpeningEquity()
    {
        var openingBalance = CreateCustomerOpeningBalance(Guid.NewGuid(), debitAmount: 210m);

        PostingRequest? capturedRequest = null;
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(CreateJournal());

        var result = await _service.PostCustomerOpeningBalanceAsync(openingBalance);

        Assert.True(result.Success);
        Assert.NotNull(capturedRequest);
        Assert.Equal("CustomerOpeningBalance", capturedRequest!.SourceType);
        Assert.Equal(openingBalance.Id, capturedRequest.SourceId);
        Assert.Equal($"CUSTOMER_OPENING_BALANCE:{_tenantId}:{openingBalance.Id}", capturedRequest.IdempotencyKey);
        Assert.Collection(
            capturedRequest.Lines,
            debit =>
            {
                Assert.Equal(_accountsReceivableId, debit.AccountId);
                Assert.Equal(210m, debit.DebitAmount);
                Assert.Equal(0m, debit.CreditAmount);
            },
            credit =>
            {
                Assert.Equal(_openingBalanceEquityId, credit.AccountId);
                Assert.Equal(0m, credit.DebitAmount);
                Assert.Equal(210m, credit.CreditAmount);
            });
    }

    [Theory]
    [InlineData("Credit", true)]
    [InlineData("Debit", false)]
    public async Task PostCommerceHubFinancialTransactionAsync_CashDirection_UsesAssetNormalBalance(
        string direction,
        bool cashIsDebit)
    {
        PostingRequest? capturedRequest = null;
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .Callback<PostingRequest, CancellationToken>((request, _) => capturedRequest = request)
            .ReturnsAsync(CreateJournal());

        var result = await _service.PostCommerceHubFinancialTransactionAsync(
            _tenantId,
            Guid.NewGuid(),
            cashIsDebit ? "OrderPayment" : "Refund",
            direction,
            "Vendor",
            "Cash",
            400m,
            "EGP",
            "CommerceHub cash posting",
            DateTime.UtcNow);

        Assert.True(result.Success);
        Assert.NotNull(capturedRequest);
        var cashLine = Assert.Single(capturedRequest!.Lines, line => line.AccountId == _bankId);
        var offsetLine = Assert.Single(capturedRequest.Lines, line => line.AccountId == _treasurySuspenseId);
        Assert.Equal(cashIsDebit ? 400m : 0m, cashLine.DebitAmount);
        Assert.Equal(cashIsDebit ? 0m : 400m, cashLine.CreditAmount);
        Assert.Equal(cashIsDebit ? 0m : 400m, offsetLine.DebitAmount);
        Assert.Equal(cashIsDebit ? 400m : 0m, offsetLine.CreditAmount);
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // ERROR HANDLING TESTS
    // ═══════════════════════════════════════════════════════════════════════════
    
    [Fact]
    public async Task PostPosSaleAsync_WhenPostingFails_ReturnsFailure()
    {
        // Arrange
        var invoice = CreateSalesInvoice(PaymentMethodEnum.Cash, 100m);
        
        _mockPostingService
            .Setup(x => x.PostJournalAsync(It.IsAny<PostingRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Insufficient funds"));
        
        // Act
        var result = await _service.PostPosSaleAsync(invoice);
        
        // Assert
        Assert.False(result.Success);
        Assert.Contains("Insufficient funds", result.ErrorMessage);
    }
    
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public async Task PostPosSaleAsync_WithEmptyOrWhitespaceOperationKey_ThrowsArgumentException(string invalidKey)
    {
        // Arrange
        var invoice = CreateSalesInvoice(PaymentMethodEnum.Cash, 100m);
        
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _service.PostPosSaleAsync(invoice, operationKey: invalidKey));
        
        Assert.Equal("operationKey", exception.ParamName);
        Assert.Contains("cannot be empty or whitespace", exception.Message);
    }
    
    [Theory]
    [InlineData("update:v2", "':'")]
    [InlineData("update\nv2", "newline")]
    [InlineData("update\tv2", "tab")]
    [InlineData("update\0v2", "null")]
    [InlineData("update v2", "space")]
    [InlineData("update@v2", "'@'")]
    [InlineData("update#v2", "'#'")]
    public async Task PostPosSaleAsync_WithInvalidCharactersInOperationKey_ThrowsArgumentException(string invalidKey, string description)
    {
        // Arrange
        var invoice = CreateSalesInvoice(PaymentMethodEnum.Cash, 100m);
        
        // Act & Assert
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => _service.PostPosSaleAsync(invoice, operationKey: invalidKey));
        
        Assert.Equal("operationKey", exception.ParamName);
        Assert.Contains("must contain only alphanumeric characters, dots, underscores, or hyphens", exception.Message);
    }
    
    [Theory]
    [InlineData("update-v2")]
    [InlineData("update_v2")]
    [InlineData("update.v2")]
    [InlineData("UpdateV2")]
    [InlineData("update123")]
    [InlineData("123")]
    [InlineData("a")]
    [InlineData("update-v2.final_3")]
    public async Task PostPosSaleAsync_WithValidOperationKey_Succeeds(string validKey)
    {
        // Arrange
        var invoice = CreateSalesInvoice(PaymentMethodEnum.Cash, 100m);
        
        // Act
        var result = await _service.PostPosSaleAsync(invoice, operationKey: validKey);
        
        // Assert
        Assert.True(result.Success);
    }
    
    // ═══════════════════════════════════════════════════════════════════════════
    // HELPER METHODS
    // ═══════════════════════════════════════════════════════════════════════════
    
    private SalesInvoice CreateSalesInvoice(PaymentMethodEnum paymentMethod, decimal totalAmount)
    {
        return new SalesInvoice
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            InvoiceNumber = $"INV-{Guid.NewGuid().ToString()[..6]}",
            InvoiceDate = DateTime.UtcNow,
            PaymentMethod = paymentMethod,
            TotalAmount = totalAmount,
            PaidAmount = totalAmount,
            Items = new List<SalesInvoiceItem>()
        };
    }
    
    private Order CreateOrder(decimal total)
    {
        return new Order
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            OrderNumber = $"ORD-{Guid.NewGuid().ToString()[..6]}",
            Total = total,
            Items = new List<OrderItem>()
        };
    }

    private AccountLedger CreateSupplierOpeningBalance(Guid supplierId, decimal creditAmount)
    {
        return new AccountLedger
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            PartyType = PartyType.Supplier,
            PartyId = supplierId,
            TransactionDate = DateTime.UtcNow,
            TransactionType = TransactionType.OpeningBalance,
            DocumentNumber = $"OB-SUP-{Guid.NewGuid():N}"[..15],
            DebitAmount = 0m,
            CreditAmount = creditAmount,
            RunningBalance = -creditAmount,
            CreatedAt = DateTime.UtcNow
        };
    }

    private AccountLedger CreateCustomerOpeningBalance(Guid customerId, decimal debitAmount)
    {
        return new AccountLedger
        {
            Id = Guid.NewGuid(),
            TenantId = _tenantId,
            PartyType = PartyType.Customer,
            PartyId = customerId,
            TransactionDate = DateTime.UtcNow,
            TransactionType = TransactionType.OpeningBalance,
            DocumentNumber = $"OB-CUS-{Guid.NewGuid():N}"[..15],
            DebitAmount = debitAmount,
            CreditAmount = 0m,
            RunningBalance = debitAmount,
            CreatedAt = DateTime.UtcNow
        };
    }

    private JournalEntry CreateJournal()
    {
        return JournalEntry.Create(
            _tenantId,
            $"JE-2026-{Guid.NewGuid().ToString()[..6]}",
            DateTime.UtcNow,
            "Test",
            Guid.NewGuid(),
            null,
            "Test journal",
            Guid.NewGuid().ToString(),
            null);
    }
}
