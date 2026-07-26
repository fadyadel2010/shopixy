using System.Reflection;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services;
using PointOfSale.API.Services.Audit;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Tests;

public sealed class SalesControllerIdempotencySafetyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateSale_RejectsMissingIdempotencyHeaders(string? key)
    {
        var controller = CreateController();

        var result = await controller.CreateSale(
            new CreateSaleRequest
            {
                WarehouseId = Guid.NewGuid(),
            },
            key);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Either X-Payment-Attempt-Id or X-Pos-Cart-Id header is required", JsonSerializer.Serialize(badRequest.Value));
    }

    [Theory]
    [InlineData("bad_key")]
    [InlineData("bad/key")]
    public async Task CreateSale_RejectsInvalidIdempotencyKeys(string key)
    {
        var controller = CreateController();

        var result = await controller.CreateSale(
            new CreateSaleRequest
            {
                WarehouseId = Guid.NewGuid(),
            },
            key);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Invalid idempotency key", JsonSerializer.Serialize(badRequest.Value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad_key")]
    [InlineData("bad/key")]
    public async Task UpdateSale_RejectsInvalidIdempotencyKeys(string? key)
    {
        var controller = CreateController();

        var result = await controller.UpdateSale(
            Guid.NewGuid(),
            new CreateSaleRequest
            {
                WarehouseId = Guid.NewGuid(),
            },
            key);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Invalid idempotency key", JsonSerializer.Serialize(badRequest.Value));
    }

    [Fact]
    public async Task CreateSale_RejectsIdempotencyKeysLongerThan100Characters()
    {
        var controller = CreateController();
        var tooLongKey = new string('A', 101);

        var result = await controller.CreateSale(
            new CreateSaleRequest
            {
                WarehouseId = Guid.NewGuid(),
            },
            tooLongKey);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Invalid idempotency key", JsonSerializer.Serialize(badRequest.Value));
    }

    [Fact]
    public async Task UpdateSale_RejectsIdempotencyKeysLongerThan100Characters()
    {
        var controller = CreateController();
        var tooLongKey = new string('A', 101);

        var result = await controller.UpdateSale(
            Guid.NewGuid(),
            new CreateSaleRequest
            {
                WarehouseId = Guid.NewGuid(),
            },
            tooLongKey);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Invalid idempotency key", JsonSerializer.Serialize(badRequest.Value));
    }

    [Fact]
    public void BuildStoredSaleResponse_ReplaysExactStoredStatusAndBody()
    {
        var method = typeof(SalesController).GetMethod(
            "BuildStoredSaleResponse",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var responseJson = "{\"invoiceId\":\"2bc9fdf7-4678-4ec4-8c9a-44d174798660\",\"invoiceNumber\":\"INV-0001\",\"totalAmount\":42.5,\"message\":\"ok\"}";
        var record = new SalesRequestIdempotency
        {
            ResponseStatusCode = StatusCodes.Status202Accepted,
            ResponseJson = responseJson,
        };

        var replayed = (ActionResult<SaleResponse>)method!.Invoke(null, [record])!;
        var objectResult = Assert.IsType<ObjectResult>(replayed.Result);
        Assert.Equal(StatusCodes.Status202Accepted, objectResult.StatusCode);

        var payload = Assert.IsType<JsonElement>(objectResult.Value);
        Assert.Equal(responseJson, payload.GetRawText());
    }

    [Fact]
    public void BuildSalesRequestHash_ProducesDeterministicHash()
    {
        var method = typeof(SalesController).GetMethod(
            "BuildSalesRequestHash",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var request = new CreateSaleRequest
        {
            WarehouseId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            CustomerId = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            PaymentMethod = PaymentMethod.Cash,
        };

        // Same input should produce same hash
        var hash1 = (string)method!.Invoke(null, ["CREATE", request])!;
        var hash2 = (string)method.Invoke(null, ["CREATE", request])!;

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length); // SHA256 produces 64 hex characters
    }

    [Fact]
    public void BuildSalesRequestHash_DifferentPayloadsProduceDifferentHashes()
    {
        var method = typeof(SalesController).GetMethod(
            "BuildSalesRequestHash",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var request1 = new CreateSaleRequest
        {
            WarehouseId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            PaymentMethod = PaymentMethod.Cash,
        };

        var request2 = new CreateSaleRequest
        {
            WarehouseId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            PaymentMethod = PaymentMethod.Card, // Different payment method
        };

        var hash1 = (string)method!.Invoke(null, ["CREATE", request1])!;
        var hash2 = (string)method.Invoke(null, ["CREATE", request2])!;

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void BuildSaleRequestIdempotencyPayload_IgnoresClientPriceAndTax_ButKeepsSaleIntent()
    {
        var payloadMethod = typeof(SalesController).GetMethod(
            "BuildCreateSaleIdempotencyPayload",
            BindingFlags.NonPublic | BindingFlags.Static);
        var hashMethod = typeof(SalesController).GetMethod(
            "BuildSalesRequestHash",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(payloadMethod);
        Assert.NotNull(hashMethod);

        var productId = Guid.Parse("00000000-0000-0000-0000-000000000011");
        var warehouseId = Guid.Parse("00000000-0000-0000-0000-000000000012");
        var baseRequest = new CreateSaleRequest
        {
            WarehouseId = warehouseId,
            PaymentMethod = PaymentMethod.Cash,
            PaidAmount = 100m,
            Items =
            [
                new SaleItemRequest
                {
                    ProductId = productId,
                    Quantity = 1m,
                    UnitPrice = 10m,
                    TaxRate = 5m,
                    DiscountAmount = 0m,
                }
            ]
        };
        var staleClientFinancialsRequest = new CreateSaleRequest
        {
            WarehouseId = warehouseId,
            PaymentMethod = PaymentMethod.Cash,
            PaidAmount = 100m,
            Items =
            [
                new SaleItemRequest
                {
                    ProductId = productId,
                    Quantity = 1m,
                    UnitPrice = 999m,
                    TaxRate = 99m,
                    DiscountAmount = 0m,
                }
            ]
        };
        var changedQuantityRequest = new CreateSaleRequest
        {
            WarehouseId = warehouseId,
            PaymentMethod = PaymentMethod.Cash,
            PaidAmount = 100m,
            Items =
            [
                new SaleItemRequest
                {
                    ProductId = productId,
                    Quantity = 2m,
                    UnitPrice = 999m,
                    TaxRate = 99m,
                    DiscountAmount = 0m,
                }
            ]
        };

        var basePayload = payloadMethod!.Invoke(null, [baseRequest])!;
        var stalePayload = payloadMethod.Invoke(null, [staleClientFinancialsRequest])!;
        var changedPayload = payloadMethod.Invoke(null, [changedQuantityRequest])!;

        var baseHash = (string)hashMethod!.Invoke(null, ["CREATE", basePayload])!;
        var staleHash = (string)hashMethod.Invoke(null, ["CREATE", stalePayload])!;
        var changedHash = (string)hashMethod.Invoke(null, ["CREATE", changedPayload])!;

        Assert.Equal(baseHash, staleHash);
        Assert.NotEqual(baseHash, changedHash);
    }

    [Fact]
    public void BuildSaleRequestIdempotencyPayload_IncludesExpectedMutationVersion()
    {
        var payloadMethod = typeof(SalesController).GetMethod(
            "BuildUpdateSaleIdempotencyPayload",
            BindingFlags.NonPublic | BindingFlags.Static);
        var hashMethod = typeof(SalesController).GetMethod(
            "BuildSalesRequestHash",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(payloadMethod);
        Assert.NotNull(hashMethod);

        var requestAtVersion1 = BuildValidSaleRequest();
        requestAtVersion1.ExpectedMutationVersion = 1;

        var requestAtVersion2 = BuildValidSaleRequest();
        requestAtVersion2.ExpectedMutationVersion = 2;

        var hashAtVersion1 = (string)hashMethod!.Invoke(null, ["UPDATE", payloadMethod!.Invoke(null, [requestAtVersion1])!])!;
        var hashAtVersion2 = (string)hashMethod.Invoke(null, ["UPDATE", payloadMethod.Invoke(null, [requestAtVersion2])!])!;

        Assert.NotEqual(hashAtVersion1, hashAtVersion2);
    }

    [Fact]
    public void ApplySaleTotals_SubtractsLineDiscountsBeforeInvoiceDiscount()
    {
        var method = typeof(SalesController).GetMethod(
            "ApplySaleTotals",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var invoice = new SalesInvoice();
        var request = BuildValidSaleRequest();
        request.DiscountPercentage = 10m;
        request.DiscountAmount = 0m;

        method!.Invoke(null, [invoice, 100m, 20m, 4m, request]);

        Assert.Equal(100m, invoice.Subtotal);
        Assert.Equal(8m, invoice.DiscountAmount);
        Assert.Equal(4m, invoice.TaxAmount);
        Assert.Equal(76m, invoice.TotalAmount);
    }

    [Fact]
    public void ApplySaleTotals_RejectsInvoiceDiscountGreaterThanRemainingSubtotal()
    {
        var method = typeof(SalesController).GetMethod(
            "ApplySaleTotals",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var invoice = new SalesInvoice();
        var request = BuildValidSaleRequest();
        request.DiscountAmount = 81m;

        var exception = Assert.Throws<TargetInvocationException>(() =>
            method!.Invoke(null, [invoice, 100m, 20m, 0m, request]));

        Assert.IsType<ValidationException>(exception.InnerException);
    }

    [Fact]
    public void BuildStoredSaleResponse_FailsClosed_WhenNoBody()
    {
        var method = typeof(SalesController).GetMethod(
            "BuildStoredSaleResponse",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var record = new SalesRequestIdempotency
        {
            ResponseStatusCode = StatusCodes.Status204NoContent,
            ResponseJson = null,
        };

        var replayed = (ActionResult<SaleResponse>)method!.Invoke(null, [record])!;
        var objectResult = Assert.IsType<ObjectResult>(replayed.Result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        Assert.Contains("SALE_IN_PROGRESS", objectResult.Value!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotSaleActionResult_CapturesStatusCodeAndBody()
    {
        var method = typeof(SalesController).GetMethod(
            "SnapshotSaleActionResult",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var response = new SaleResponse
        {
            InvoiceId = Guid.Parse("00000000-0000-0000-0000-000000000001"),
            InvoiceNumber = "INV-001",
            TotalAmount = 100.50m,
            Message = "Success",
        };

        var actionResult = new ActionResult<SaleResponse>(response);
        var snapshot = method!.Invoke(null, [actionResult]);

        // Verify the method returns a record with the expected shape
        var snapshotType = snapshot!.GetType();
        var statusCodeProperty = snapshotType.GetProperty("StatusCode");
        var responseJsonProperty = snapshotType.GetProperty("ResponseJson");

        Assert.NotNull(statusCodeProperty);
        Assert.NotNull(responseJsonProperty);
        Assert.Equal(StatusCodes.Status200OK, statusCodeProperty!.GetValue(snapshot));
    }

    [Fact]
    public async Task TryClaimSalesIdempotencyAsync_ReplaysCompletedUpdateByRequestHash_WhenAttemptKeyWasLost()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateDbContext();
        var controller = CreateController(context, tenantId);
        var requestHash = BuildUpdateRequestHash(Guid.Parse("00000000-0000-0000-0000-000000000021"));
        var responseJson = "{\"invoiceId\":\"00000000-0000-0000-0000-000000000021\",\"invoiceNumber\":\"INV-0021\",\"totalAmount\":10,\"message\":\"Sale updated successfully\"}";

        context.SalesRequestIdempotencyRecords.Add(new SalesRequestIdempotency
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Key = "old-update-attempt",
            Operation = "UPDATE",
            RequestHash = requestHash,
            ResponseStatusCode = StatusCodes.Status200OK,
            ResponseJson = responseJson,
            CompletedAtUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        var (_, shortCircuitResult) = await InvokeTryClaimSalesIdempotencyAsync(
            controller,
            "UPDATE",
            "fresh-update-attempt",
            requestHash);

        Assert.NotNull(shortCircuitResult);
        var objectResult = Assert.IsType<ObjectResult>(shortCircuitResult!.Result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
        var payload = Assert.IsType<JsonElement>(objectResult.Value);
        Assert.Equal(responseJson, payload.GetRawText());
    }

    [Fact]
    public async Task TryClaimSalesIdempotencyAsync_BlocksActiveUpdateWithSameRequestHash_WhenAttemptKeyWasLost()
    {
        var tenantId = Guid.NewGuid();
        using var context = CreateDbContext();
        var controller = CreateController(context, tenantId);
        var requestHash = BuildUpdateRequestHash(Guid.Parse("00000000-0000-0000-0000-000000000022"));

        context.SalesRequestIdempotencyRecords.Add(new SalesRequestIdempotency
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Key = "old-update-attempt",
            Operation = "UPDATE",
            RequestHash = requestHash,
            ClaimTimestampUtc = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        var (_, shortCircuitResult) = await InvokeTryClaimSalesIdempotencyAsync(
            controller,
            "UPDATE",
            "fresh-update-attempt",
            requestHash);

        Assert.NotNull(shortCircuitResult);
        var conflict = Assert.IsType<ConflictObjectResult>(shortCircuitResult!.Result);
        Assert.Contains("same payload", JsonSerializer.Serialize(conflict.Value));
    }

    [Theory]
    [InlineData("valid-key-123")]
    [InlineData("ABC123")]
    [InlineData("a-b-c")]
    [InlineData("12345")]
    public void ValidateSalesIdempotencyKey_AcceptsValidKeys(string key)
    {
        var method = typeof(SalesController).GetMethod(
            "ValidateSalesIdempotencyKey",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(method);

        using var context = CreateDbContext();
        var controller = CreateController(context, Guid.NewGuid());

        var result = method!.Invoke(controller, [key]) as string;

        Assert.NotNull(result);
        Assert.Equal(key.Trim(), result);
    }

    [Theory]
    [InlineData("key_with_underscore")]
    [InlineData("key.with.dots")]
    [InlineData("key@symbol")]
    [InlineData("key with spaces")]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateSalesIdempotencyKey_RejectsInvalidKeys(string key)
    {
        var method = typeof(SalesController).GetMethod(
            "ValidateSalesIdempotencyKey",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(method);

        using var context = CreateDbContext();
        var controller = CreateController(context, Guid.NewGuid());

        var result = method!.Invoke(controller, [key]) as string;

        Assert.Null(result);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static SalesController CreateController()
    {
        var context = CreateDbContext();
        var tenantId = Guid.NewGuid();

        return CreateController(context, tenantId);
    }

    private static SalesController CreateController(ApplicationDbContext context, Guid tenantId)
    {

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(value => value.TenantId).Returns(tenantId);
        tenantContext.SetupGet(value => value.RequiredTenantId).Returns(tenantId);

        return new SalesController(
            context,
            tenantContext.Object,
            NullLogger<SalesController>.Instance,
            Mock.Of<ILedgerService>(),
            Mock.Of<IPartyBalanceMaintenanceService>(),
            Mock.Of<ISaleFinancialIntegrityService>(),
            Mock.Of<IValidationService>(),
            Mock.Of<IAuditService>(),
            Mock.Of<IEventPublisher>(),
            Mock.Of<IAccountingOutboxService>(),
            Mock.Of<ICommerceAccountingService>(),
            Mock.Of<IFinancialPostingService>());
    }

    private static string BuildUpdateRequestHash(Guid saleId)
    {
        var payloadMethod = typeof(SalesController).GetMethod(
            "BuildUpdateSaleIdempotencyPayload",
            BindingFlags.NonPublic | BindingFlags.Static);
        var hashMethod = typeof(SalesController).GetMethod(
            "BuildSalesRequestHash",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(payloadMethod);
        Assert.NotNull(hashMethod);

        var request = new CreateSaleRequest
        {
            WarehouseId = Guid.Parse("00000000-0000-0000-0000-000000000012"),
            PaymentMethod = PaymentMethod.Cash,
            PaidAmount = 10m,
            Items =
            [
                new SaleItemRequest
                {
                    ProductId = Guid.Parse("00000000-0000-0000-0000-000000000011"),
                    Quantity = 1m,
                    UnitPrice = 10m,
                    DiscountAmount = 0m,
                    TaxRate = 0m,
                }
            ],
        };

        var requestPayload = payloadMethod!.Invoke(null, [request])!;
        return (string)hashMethod!.Invoke(null, ["UPDATE", new
        {
            SaleId = saleId,
            Request = requestPayload,
        }])!;
    }

    private static CreateSaleRequest BuildValidSaleRequest() => new()
    {
        WarehouseId = Guid.Parse("00000000-0000-0000-0000-000000000012"),
        PaymentMethod = PaymentMethod.Cash,
        PaidAmount = 10m,
        Items =
        [
            new SaleItemRequest
            {
                ProductId = Guid.Parse("00000000-0000-0000-0000-000000000011"),
                Quantity = 1m,
                UnitPrice = 10m,
                DiscountAmount = 0m,
                TaxRate = 0m,
            }
        ],
    };

    private static async Task<(SalesRequestIdempotency? Record, ActionResult<SaleResponse>? ShortCircuitResult)> InvokeTryClaimSalesIdempotencyAsync(
        SalesController controller,
        string operation,
        string key,
        string requestHash)
    {
        var method = typeof(SalesController).GetMethod(
            "TryClaimSalesIdempotencyAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(method);

        var task = (Task)method!.Invoke(controller, [operation, key, requestHash])!;
        await task;

        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var record = (SalesRequestIdempotency?)result.GetType().GetField("Item1")!.GetValue(result);
        var shortCircuitResult = (ActionResult<SaleResponse>?)result.GetType().GetField("Item2")!.GetValue(result);
        return (record, shortCircuitResult);
    }
}
