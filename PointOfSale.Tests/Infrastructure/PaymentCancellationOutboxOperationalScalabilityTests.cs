using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces.Payments;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Payment;

namespace PointOfSale.Tests.Infrastructure;

public sealed class PaymentCancellationOutboxOperationalScalabilityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ApplicationDbContext _dbContext;
    private readonly RecordingPaymentLifecycleService _paymentLifecycle = new();

    public PaymentCancellationOutboxOperationalScalabilityTests()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString("N");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(databaseName)
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddScoped<IPaymentLifecycleService>(_ => _paymentLifecycle);

        _serviceProvider = services.BuildServiceProvider();
        _dbContext = _serviceProvider.GetRequiredService<ApplicationDbContext>();
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_BoundsPaymentCancellationWorkToConfiguredBatchSize()
    {
        _paymentLifecycle.NextResult = PaymentTransitionResult.Succeeded(PaymentStatus.Failed);
        var tenantId = Guid.NewGuid();

        _dbContext.PaymentCancellationOutboxMessages.AddRange(
            Enumerable.Range(1, 3).Select(index => CreateMessage(tenantId, index)));
        await _dbContext.SaveChangesAsync();

        var processor = CreateProcessor(new PaymentCancellationOutboxOptions
        {
            BatchSize = 2,
            MaxRetryAttempts = 3
        });

        var processed = await processor.ProcessPendingMessagesAsync();

        Assert.Equal(2, processed);
        Assert.Equal(2, _paymentLifecycle.HandlePaymentResultCalls);
        Assert.Equal(2, await _dbContext.PaymentCancellationOutboxMessages.CountAsync(message => message.ProcessedAtUtc != null));
        Assert.Equal(1, await _dbContext.PaymentCancellationOutboxMessages.CountAsync(message => message.ProcessedAtUtc == null));
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_UsesConfiguredRetryThresholdAndBackoffCap()
    {
        _paymentLifecycle.NextResult = PaymentTransitionResult.Failed("transient lifecycle pressure");
        var message = CreateMessage(Guid.NewGuid(), index: 1);
        message.AttemptCount = 1;

        _dbContext.PaymentCancellationOutboxMessages.Add(message);
        await _dbContext.SaveChangesAsync();

        var processor = CreateProcessor(new PaymentCancellationOutboxOptions
        {
            BatchSize = 10,
            MaxRetryAttempts = 3,
            MaxRetryDelayMinutes = 1
        });

        var processed = await processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, processed);

        _dbContext.ChangeTracker.Clear();
        var reloaded = await _dbContext.PaymentCancellationOutboxMessages.SingleAsync();
        Assert.Equal(2, reloaded.AttemptCount);
        Assert.False(reloaded.IsFailed);
        Assert.NotNull(reloaded.NextRetryAtUtc);
        Assert.True(reloaded.NextRetryAtUtc <= DateTime.UtcNow.AddMinutes(2));

        reloaded.NextRetryAtUtc = null;
        await _dbContext.SaveChangesAsync();

        await processor.ProcessPendingMessagesAsync();

        _dbContext.ChangeTracker.Clear();
        reloaded = await _dbContext.PaymentCancellationOutboxMessages.SingleAsync();
        Assert.Equal(3, reloaded.AttemptCount);
        Assert.True(reloaded.IsFailed);
        Assert.Null(reloaded.NextRetryAtUtc);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_SkipsActivelyClaimedCancellationRows()
    {
        _paymentLifecycle.NextResult = PaymentTransitionResult.Succeeded(PaymentStatus.Failed);
        var message = CreateMessage(Guid.NewGuid(), index: 1);
        message.ClaimedUntilUtc = DateTime.UtcNow.AddMinutes(5);

        _dbContext.PaymentCancellationOutboxMessages.Add(message);
        await _dbContext.SaveChangesAsync();

        var processor = CreateProcessor(new PaymentCancellationOutboxOptions
        {
            BatchSize = 10,
            ProcessingLeaseSeconds = 30
        });

        var processed = await processor.ProcessPendingMessagesAsync();

        Assert.Equal(0, processed);
        Assert.Equal(0, _paymentLifecycle.HandlePaymentResultCalls);

        _dbContext.ChangeTracker.Clear();
        var reloaded = await _dbContext.PaymentCancellationOutboxMessages.SingleAsync();
        Assert.Null(reloaded.ProcessedAtUtc);
        Assert.NotNull(reloaded.ClaimedUntilUtc);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_ClaimsThenClearsLeaseAfterLifecycleConverges()
    {
        _paymentLifecycle.NextResult = PaymentTransitionResult.Succeeded(PaymentStatus.Failed);
        var message = CreateMessage(Guid.NewGuid(), index: 1);

        _dbContext.PaymentCancellationOutboxMessages.Add(message);
        await _dbContext.SaveChangesAsync();

        var processor = CreateProcessor(new PaymentCancellationOutboxOptions
        {
            BatchSize = 10,
            ProcessingLeaseSeconds = 30
        });

        var processed = await processor.ProcessPendingMessagesAsync();

        Assert.Equal(1, processed);
        Assert.Equal(1, _paymentLifecycle.HandlePaymentResultCalls);

        _dbContext.ChangeTracker.Clear();
        var reloaded = await _dbContext.PaymentCancellationOutboxMessages.SingleAsync();
        Assert.NotNull(reloaded.ProcessedAtUtc);
        Assert.Null(reloaded.ClaimedUntilUtc);
    }

    [Fact]
    public async Task PaymentCancellationOutbox_ConcurrentClaimsCollapseThroughRowVersion()
    {
        var message = CreateMessage(Guid.NewGuid(), index: 1);
        _dbContext.PaymentCancellationOutboxMessages.Add(message);
        await _dbContext.SaveChangesAsync();

        using var firstScope = _serviceProvider.CreateScope();
        using var secondScope = _serviceProvider.CreateScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var firstClaim = await firstDb.PaymentCancellationOutboxMessages.SingleAsync();
        var secondClaim = await secondDb.PaymentCancellationOutboxMessages.SingleAsync();

        Assert.True(firstClaim.TryClaim(30));
        Assert.True(secondClaim.TryClaim(30));

        await firstDb.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondDb.SaveChangesAsync());

        _dbContext.ChangeTracker.Clear();
        var reloaded = await _dbContext.PaymentCancellationOutboxMessages.SingleAsync();
        Assert.NotEqual(0u, reloaded.RowVersion);
        Assert.NotNull(reloaded.ClaimedUntilUtc);
        Assert.Null(reloaded.ProcessedAtUtc);
    }

    [Fact]
    public void PaymentCancellationOutbox_MustUseOperationalThroughputOptions()
    {
        var root = DiscoverSolutionRoot(AppContext.BaseDirectory);
        var worker = File.ReadAllText(Path.Combine(root, "PointOfSale.Infrastructure", "Payment", "PaymentCancellationOutboxWorker.cs"));
        var processor = File.ReadAllText(Path.Combine(root, "PointOfSale.Infrastructure", "Payment", "PaymentCancellationOutboxProcessor.cs"));
        var extension = File.ReadAllText(Path.Combine(root, "PointOfSale.Infrastructure", "Extensions", "PaymentCancellationOutboxExtensions.cs"));
        var program = File.ReadAllText(Path.Combine(root, "PointOfSale.API", "Program.cs"));

        Assert.Contains("_options.MaxBatchesPerPoll", worker, StringComparison.Ordinal);
        Assert.Contains("_options.InterBatchDelayMilliseconds", worker, StringComparison.Ordinal);
        Assert.Contains("_options.PollIntervalSeconds", worker, StringComparison.Ordinal);
        Assert.Contains("_options.IdleIntervalSeconds", worker, StringComparison.Ordinal);
        Assert.Contains("_options.ErrorDelaySeconds", worker, StringComparison.Ordinal);
        Assert.Contains("_options.BatchSize", processor, StringComparison.Ordinal);
        Assert.Contains("_options.ProcessingLeaseSeconds", processor, StringComparison.Ordinal);
        Assert.Contains("ClaimedUntilUtc == null || m.ClaimedUntilUtc <= nowUtc", processor, StringComparison.Ordinal);
        Assert.Contains("message.TryClaim(_options.ProcessingLeaseSeconds)", processor, StringComparison.Ordinal);
        Assert.Contains("ComputeStableAdvisoryLockKey(message.TenantId, message.OrderId)", processor, StringComparison.Ordinal);
        Assert.Contains("PaymentCancellationClaimOutcome.ConcurrencyConflict", processor, StringComparison.Ordinal);
        Assert.Contains("_options.MaxRetryAttempts", processor, StringComparison.Ordinal);
        Assert.Contains("_options.MaxRetryDelayMinutes", processor, StringComparison.Ordinal);
        Assert.DoesNotContain("private const int BatchSize", processor, StringComparison.Ordinal);
        Assert.DoesNotContain("private const int MaxRetryAttempts", processor, StringComparison.Ordinal);
        Assert.Contains("PaymentCancellationOutboxOptions.SectionName", extension, StringComparison.Ordinal);
        Assert.Contains("PaymentCancellationOutbox:ProcessingLeaseSeconds", extension, StringComparison.Ordinal);
        Assert.Contains("AddPaymentCancellationOutbox(builder.Configuration)", program, StringComparison.Ordinal);
        Assert.Contains("AddStockSyncOutbox(builder.Configuration)", program, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
    }

    private PaymentCancellationOutboxProcessor CreateProcessor(PaymentCancellationOutboxOptions options)
    {
        return new PaymentCancellationOutboxProcessor(
            _serviceProvider,
            NullLogger<PaymentCancellationOutboxProcessor>.Instance,
            Options.Create(options));
    }

    private static PaymentCancellationOutboxMessage CreateMessage(Guid tenantId, int index)
    {
        return new PaymentCancellationOutboxMessage
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            OrderId = Guid.NewGuid(),
            Reason = "admin cancellation",
            Actor = "scalability-test",
            CreatedAt = DateTime.UtcNow.AddMinutes(-index)
        };
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        var currentDir = new DirectoryInfo(startPath);

        while (currentDir != null)
        {
            if (currentDir.GetFiles("*.sln").Any() ||
                currentDir.GetDirectories("PointOfSale.API").Any())
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }

    private sealed class RecordingPaymentLifecycleService : IPaymentLifecycleService
    {
        public int HandlePaymentResultCalls { get; private set; }
        public PaymentTransitionResult NextResult { get; set; } = PaymentTransitionResult.Succeeded(PaymentStatus.Failed);

        public Task<PaymentTransitionResult> HandlePaymentResultAsync(
            HandlePaymentResultRequest request,
            CancellationToken ct = default)
        {
            HandlePaymentResultCalls++;
            return Task.FromResult(NextResult);
        }

        public Task<PaymentIntent> CreateReviewablePaymentIntentAsync(
            ReviewablePaymentIntentCreateRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PaymentTransitionResult> AdmitPaymentReviewAsync(
            PaymentReviewAdmissionRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PaymentTransitionResult> MarkOfflineCollectionDeliveredAsync(Guid tenantId, Guid orderId, string? deliveryReference = null, string? confirmedBy = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PaymentTransitionResult> RecordOfflineSettlementAsync(OfflineSettlementRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PaymentTransitionResult> CaptureOnlinePaymentAsync(Guid orderId, string gateway, string paymentIntentId, decimal providerAmount, string providerCurrency, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PaymentTransitionResult> RecordGatewaySettlementAsync(GatewaySettlementRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PaymentTransitionResult> ApproveReviewedPaymentAsync(Guid orderId, string approvedBy, string? transactionReference = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PaymentTransitionResult> RejectReviewedPaymentAsync(Guid orderId, string rejectedBy, string reason, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PaymentTransitionResult> RecordRefundAsync(Guid orderId, decimal refundAmount, string refundId, string reason, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PaymentTransitionResult> MarkPaymentFailedAsync(Guid orderId, string reason, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PaymentTransitionResult> MarkPaymentExpiredAsync(Guid tenantId, Guid orderId, string reason, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PaymentTransitionResult> ResetFailedPaymentForRetryAsync(Guid orderId, Guid failedPaymentIntentId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PaymentAllowedActions> GetAllowedActionsAsync(Guid orderId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
