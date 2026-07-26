using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using PointOfSale.Core.Entities.Accounting;
using PointOfSale.Core.Interfaces.Accounting;
using PointOfSale.Infrastructure.Accounting;
using Xunit;

namespace PointOfSale.Tests.Accounting;

/// <summary>
/// Unit tests for Phase 3.5 production hardening components.
/// </summary>
public class AccountingHardeningTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Mock<ILogger<AccountingMetrics>> _metricsLogger = new();
    
    #region AccountingOutboxMessage Tests
    
    [Fact]
    public void OutboxMessage_Create_SetsCorrectProperties()
    {
        // Arrange
        var invoiceId = Guid.NewGuid();
        var idempotencyKey = $"SALE:{_tenantId}:{invoiceId}";
        
        // Act
        var message = AccountingOutboxMessage.Create(
            _tenantId,
            "PosSale",
            invoiceId,
            "SalesInvoice",
            "{}",
            idempotencyKey);
        
        // Assert
        Assert.Equal(_tenantId, message.TenantId);
        Assert.Equal("PosSale", message.EventType);
        Assert.Equal(invoiceId, message.BusinessEntityId);
        Assert.Equal(idempotencyKey, message.IdempotencyKey);
        Assert.Null(message.ProcessedAtUtc);
        Assert.Equal(0, message.AttemptCount);
        Assert.False(message.IsFailed);
    }
    
    [Fact]
    public void OutboxMessage_MarkProcessed_SetsProcessedFields()
    {
        // Arrange
        var message = CreateTestOutboxMessage();
        var journalId = Guid.NewGuid();
        
        // Act
        message.MarkProcessed(journalId);
        
        // Assert
        Assert.NotNull(message.ProcessedAtUtc);
        Assert.Equal(journalId, message.JournalId);
        Assert.Null(message.LastError);
    }
    
    [Fact]
    public void OutboxMessage_RecordFailure_IncrementsAttemptCount()
    {
        // Arrange
        var message = CreateTestOutboxMessage();
        
        // Act
        message.RecordFailure("Connection timeout");
        
        // Assert
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal("Connection timeout", message.LastError);
        Assert.NotNull(message.NextRetryAtUtc);
        Assert.False(message.IsFailed);
    }
    
    [Fact]
    public void OutboxMessage_RecordFailure_ExponentialBackoff()
    {
        // Arrange
        var message = CreateTestOutboxMessage();
        var baseTime = DateTime.UtcNow;
        
        // Act - Record multiple failures
        message.RecordFailure("Error 1");
        var retry1 = message.NextRetryAtUtc;
        
        message.RecordFailure("Error 2");
        var retry2 = message.NextRetryAtUtc;
        
        message.RecordFailure("Error 3");
        var retry3 = message.NextRetryAtUtc;
        
        // Assert - Each retry should be further in the future
        Assert.True(retry2 > retry1, "Retry 2 should be after retry 1");
        Assert.True(retry3 > retry2, "Retry 3 should be after retry 2");
    }
    
    [Fact]
    public void OutboxMessage_RecordFailure_MarksFailedAfterMaxRetries()
    {
        // Arrange
        var message = CreateTestOutboxMessage();
        
        // Act - Exceed max retries (default 5)
        for (int i = 0; i < 5; i++)
        {
            message.RecordFailure($"Error {i + 1}");
        }
        
        // Assert
        Assert.True(message.IsFailed);
        Assert.Null(message.NextRetryAtUtc);
        Assert.Equal(5, message.AttemptCount);
    }
    
    #endregion
    
    #region CommerceAccountingAudit Tests
    
    [Fact]
    public void Audit_Create_SetsCorrectProperties()
    {
        // Arrange
        var businessId = Guid.NewGuid();
        var journalId = Guid.NewGuid();
        
        // Act
        var audit = CommerceAccountingAudit.Create(
            _tenantId,
            "SalesInvoice",
            businessId,
            "INV-2026-001",
            journalId,
            "JE-2026-000001",
            "Sale",
            1000m,
            $"SALE:{_tenantId}:{businessId}");
        
        // Assert
        Assert.Equal(_tenantId, audit.TenantId);
        Assert.Equal("SalesInvoice", audit.BusinessType);
        Assert.Equal(businessId, audit.BusinessId);
        Assert.Equal("INV-2026-001", audit.BusinessReference);
        Assert.Equal(journalId, audit.JournalId);
        Assert.Equal("JE-2026-000001", audit.JournalNumber);
        Assert.Equal("Sale", audit.OperationType);
        Assert.Equal(1000m, audit.Amount);
        Assert.False(audit.WasIdempotent);
    }
    
    [Fact]
    public void Audit_Create_WithIdempotentFlag()
    {
        // Arrange & Act
        var audit = CommerceAccountingAudit.Create(
            _tenantId,
            "SalesInvoice",
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            "JE-2026-000001",
            "Sale",
            1000m,
            "key",
            wasIdempotent: true);
        
        // Assert
        Assert.True(audit.WasIdempotent);
    }
    
    #endregion
    
    #region AccountingMetrics Tests
    
    [Fact]
    public void Metrics_IncrementPosSalesPosted_LogsMetric()
    {
        // Arrange
        var metrics = new AccountingMetrics(_metricsLogger.Object);
        
        // Act
        metrics.IncrementPosSalesPosted(_tenantId);
        metrics.IncrementPosSalesPosted(_tenantId);
        
        // Assert
        var counter = metrics.GetCounter($"accounting.posted_total:{ExpectedTenantBucket(_tenantId)}:pos_sale");
        Assert.Equal(2, counter);
        Assert.DoesNotContain(metrics.GetAllCounters().Keys, key => key.Contains(_tenantId.ToString(), StringComparison.OrdinalIgnoreCase));
    }
    
    [Fact]
    public void Metrics_SetOutboxPendingCount_TriggersAlertWhenHigh()
    {
        // Arrange
        var metrics = new AccountingMetrics(_metricsLogger.Object);
        
        // Act
        metrics.SetOutboxPendingCount(150);
        
        // Assert
        Assert.Equal(150, metrics.GetOutboxPendingCount());
        _metricsLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("exceeds threshold")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
    
    [Fact]
    public void Metrics_IncrementAccountingFailures_LogsWarning()
    {
        // Arrange
        var metrics = new AccountingMetrics(_metricsLogger.Object);
        
        // Act
        metrics.IncrementAccountingFailures(_tenantId, "PosSale", "SqlException");
        
        // Assert
        _metricsLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
    
    [Fact]
    public void Metrics_RecordPostingLatency_LogsSlowOperations()
    {
        // Arrange
        var metrics = new AccountingMetrics(_metricsLogger.Object);
        
        // Act
        metrics.RecordPostingLatency(1500, "PosSale");
        
        // Assert
        _metricsLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("SLOW")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Metrics_IncrementOutboxDeadLettered_TracksCounterAndLogsAlert()
    {
        var metrics = new AccountingMetrics(_metricsLogger.Object);

        metrics.IncrementOutboxDeadLettered(_tenantId, "PosSale");

        Assert.Equal(1, metrics.GetCounter($"accounting.outbox.dead_lettered_total:{ExpectedTenantBucket(_tenantId)}:possale"));
        _metricsLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("outbox_dead_lettered_total")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Metrics_RecordOutboxCleanup_TracksDeletedCounts()
    {
        var metrics = new AccountingMetrics(_metricsLogger.Object);

        metrics.RecordOutboxCleanup(3, 2);

        Assert.Equal(3, metrics.GetCounter("accounting.outbox.cleanup_total:processed"));
        Assert.Equal(2, metrics.GetCounter("accounting.outbox.cleanup_total:failed"));
    }
    
    #endregion
    
    #region Idempotency Key Tests
    
    [Theory]
    [InlineData("PosSale")]
    [InlineData("PosPurchase")]
    [InlineData("OnlinePayment")]
    [InlineData("CodDelivered")]
    public void IdempotencyKey_ContainsTenantId(string eventType)
    {
        // Arrange
        var entityId = Guid.NewGuid();
        
        // Act
        var key = $"{eventType.ToUpperInvariant()}:{_tenantId}:{entityId}";
        
        // Assert
        Assert.Contains(_tenantId.ToString(), key);
        Assert.Contains(entityId.ToString(), key);
    }
    
    [Fact]
    public void IdempotencyKey_OnlinePayment_IncludesPaymentIntentId()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var paymentIntentId = "pi_1234567890";
        
        // Act
        var key = $"ONLINE_PAYMENT:{_tenantId}:{orderId}:{paymentIntentId}";
        
        // Assert
        Assert.Contains(paymentIntentId, key);
    }
    
    #endregion
    
    #region Helper Methods
    
    private AccountingOutboxMessage CreateTestOutboxMessage()
    {
        return AccountingOutboxMessage.Create(
            _tenantId,
            "PosSale",
            Guid.NewGuid(),
            "SalesInvoice",
            "{}",
            $"SALE:{_tenantId}:{Guid.NewGuid()}");
    }

    private static string ExpectedTenantBucket(Guid tenantId)
    {
        var bytes = tenantId.ToByteArray();
        var hash = BitConverter.ToInt32(bytes, 0);
        return $"bucket_{Math.Abs(hash % 10)}";
    }
    
    #endregion
}
