using MassTransit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.IO;
using System.Linq;
using PointOfSale.API.Controllers;
using PointOfSale.API.DTOs.Analytics;
using PointOfSale.API.Services.Analytics;
using PointOfSale.Core.Analytics;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Analytics.Dispatchers;
using PointOfSale.Infrastructure.Attribution;
using PointOfSale.Infrastructure.Consumers;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MongoDB.Services;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Redis.Services;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Enums;

public sealed class StorefrontAnalyticsHardeningTests
{
    [Fact]
    public async Task SubmitEvent_RejectsClientReportedPurchaseCompleted()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Authoritative Purchase Store",
            Slug = "authoritative-purchase-store",
            IsActive = true,
            Status = TenantStatus.Active,
            Currency = "USD"
        });
        dbContext.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-AUTH-1",
            Currency = "USD"
        });
        await dbContext.SaveChangesAsync();

        var publishEndpoint = new Mock<IEventPublisher>();
        var controller = CreateController(
            tenantId,
            dbContext,
            publishEndpoint.Object);

        var result = await controller.SubmitEvent(CreatePurchaseCompletedRequest(orderId), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<AnalyticsEventResponseDto>(badRequest.Value);
        Assert.False(payload.Accepted);
        Assert.Equal("PurchaseCompleted must be emitted by the server after payment succeeds.", payload.Error);
        publishEndpoint.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<AnalyticsEventReceivedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AuthoritativePurchaseAnalyticsConsumer_UsesServerOrderTotals()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Authoritative Consumer Store",
            Slug = "authoritative-consumer-store",
            IsActive = true,
            Status = TenantStatus.Active,
            Currency = "USD"
        });
        dbContext.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-AUTH-2",
            Currency = "USD",
            Total = 420m,
            SessionId = "checkout-session-authoritative",
            PaymentStatus = PaymentStatus.Collected
        });
        await dbContext.SaveChangesAsync();

        AnalyticsEventReceivedEvent? publishedEvent = null;
        var publishEndpoint = new Mock<IEventPublisher>();
        publishEndpoint
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<AnalyticsEventReceivedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AnalyticsEventReceivedEvent, CancellationToken>((message, _) => publishedEvent = message)
            .Returns(Task.CompletedTask);

        var consumer = new AuthoritativePurchaseAnalyticsConsumer(
            dbContext,
            publishEndpoint.Object,
            NullLogger<AuthoritativePurchaseAnalyticsConsumer>.Instance);

        var message = new OrderPaidEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            OrderNumber = "ORD-AUTH-2",
            Provider = "stripe",
            PaymentMethod = "card",
            AmountPaid = 1m,
            Currency = "USD",
            PaidAt = new DateTime(2026, 4, 23, 10, 0, 0, DateTimeKind.Utc)
        };
        var context = new Mock<ConsumeContext<OrderPaidEvent>>();
        context.SetupGet(item => item.Message).Returns(message);
        context.SetupGet(item => item.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        Assert.NotNull(publishedEvent);
        Assert.Equal(AnalyticsEventType.PurchaseCompleted, publishedEvent!.AnalyticsEvent.Type);
        Assert.Equal(orderId, publishedEvent.AnalyticsEvent.Payload.OrderId);
        Assert.Equal(420m, publishedEvent.AnalyticsEvent.Payload.Value);
        Assert.Equal("checkout-session-authoritative", publishedEvent.AnalyticsEvent.Actor.SessionId);
    }

    [Theory]
    [InlineData(PaymentStatus.PartiallyRefunded)]
    [InlineData(PaymentStatus.Refunded)]
    public async Task AuthoritativePurchaseAnalyticsConsumer_SkipsRefundedCurrentSqlAuthority(PaymentStatus paymentStatus)
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Refunded Analytics Store",
            Slug = $"refunded-analytics-store-{Guid.NewGuid():N}",
            IsActive = true,
            Status = TenantStatus.Active,
            Currency = "USD"
        });
        dbContext.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-AUTH-REFUND",
            Currency = "USD",
            Total = 420m,
            PaymentStatus = paymentStatus
        });
        await dbContext.SaveChangesAsync();

        var publishEndpoint = new Mock<IEventPublisher>();
        var consumer = new AuthoritativePurchaseAnalyticsConsumer(
            dbContext,
            publishEndpoint.Object,
            NullLogger<AuthoritativePurchaseAnalyticsConsumer>.Instance);

        var message = new OrderPaidEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            OrderNumber = "ORD-AUTH-REFUND",
            Provider = "stripe",
            PaymentMethod = "card",
            AmountPaid = 420m,
            Currency = "USD",
            PaidAt = new DateTime(2026, 7, 21, 10, 0, 0, DateTimeKind.Utc)
        };
        var context = new Mock<ConsumeContext<OrderPaidEvent>>();
        context.SetupGet(item => item.Message).Returns(message);
        context.SetupGet(item => item.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        publishEndpoint.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<AnalyticsEventReceivedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AuthoritativePurchaseAnalyticsConsumer_SkipsSuspendedTenantEvenWhenLegacyMirrorIsActive()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Suspended Analytics Store",
            Slug = $"suspended-analytics-store-{Guid.NewGuid():N}",
            IsActive = true,
            Status = TenantStatus.Suspended,
            Currency = "USD"
        });
        dbContext.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-AUTH-SUSPENDED",
            Currency = "USD",
            Total = 420m,
            PaymentStatus = PaymentStatus.Collected
        });
        await dbContext.SaveChangesAsync();

        var publishEndpoint = new Mock<IEventPublisher>();
        var consumer = new AuthoritativePurchaseAnalyticsConsumer(
            dbContext,
            publishEndpoint.Object,
            NullLogger<AuthoritativePurchaseAnalyticsConsumer>.Instance);

        var message = new OrderPaidEvent
        {
            TenantId = tenantId,
            OrderId = orderId,
            OrderNumber = "ORD-AUTH-SUSPENDED",
            Provider = "stripe",
            PaymentMethod = "card",
            AmountPaid = 420m,
            Currency = "USD",
            PaidAt = new DateTime(2026, 7, 21, 10, 0, 0, DateTimeKind.Utc)
        };
        var context = new Mock<ConsumeContext<OrderPaidEvent>>();
        context.SetupGet(item => item.Message).Returns(message);
        context.SetupGet(item => item.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        publishEndpoint.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<AnalyticsEventReceivedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void AnalyticsValidator_StripsQueryAndFragmentFromUrlContext()
    {
        var validator = new AnalyticsEventValidator(NullLogger<AnalyticsEventValidator>.Instance);
        var request = CreateBeginCheckoutRequest();
        request.Context!.PageUrl = "https://store.example.com/orders/7d3b9e32/track?token=secret-token&utm_medium=ad#tracking_token=secret-token";
        request.Context.Referrer = "https://mail.example.com/message?guestToken=secret-token#token=secret-token";

        var result = validator.Validate(
            request,
            Guid.NewGuid(),
            customerId: null,
            ipAddress: "127.0.0.1",
            userAgent: "unit-test",
            acceptLanguage: "en-US",
            currency: "EGP");

        Assert.True(result.IsValid, result.Error);
        Assert.Equal("https://store.example.com/orders/7d3b9e32/track", result.Event!.Context.PageUrl);
        Assert.Equal("https://mail.example.com/message", result.Event.Context.Referrer);
        Assert.DoesNotContain("secret-token", result.Event.Context.PageUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", result.Event.Context.Referrer!, StringComparison.Ordinal);
        Assert.Equal("ad", result.Event.Context.UtmMedium);
    }

    [Fact]
    public async Task SubmitEvent_UsesTenantCurrency_WhenTenantConfigExists()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Tenant Currency Store",
            Slug = "tenant-currency-store",
            IsActive = true,
            Currency = "SAR"
        });
        await dbContext.SaveChangesAsync();

        AnalyticsEventReceivedEvent? publishedEvent = null;
        var publishEndpoint = new Mock<IEventPublisher>();
        publishEndpoint
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<AnalyticsEventReceivedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AnalyticsEventReceivedEvent, CancellationToken>((message, _) => publishedEvent = message)
            .Returns(Task.CompletedTask);

        var controller = CreateController(
            tenantId,
            dbContext,
            publishEndpoint.Object);

        var result = await controller.SubmitEvent(CreateBeginCheckoutRequest(), CancellationToken.None);

        var accepted = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.NotNull(publishedEvent);
        Assert.Equal("SAR", publishedEvent!.AnalyticsEvent.Context.Currency);
        Assert.Equal(StatusCodes.Status202Accepted, accepted.StatusCode);
    }

    [Fact]
    public async Task SubmitEvent_StripsClientProvidedValue_FromBeginCheckoutPayload()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Sanitized Value Store",
            Slug = "sanitized-value-store",
            IsActive = true,
            Currency = "USD"
        });
        await dbContext.SaveChangesAsync();

        AnalyticsEventReceivedEvent? publishedEvent = null;
        var publishEndpoint = new Mock<IEventPublisher>();
        publishEndpoint
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<AnalyticsEventReceivedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AnalyticsEventReceivedEvent, CancellationToken>((message, _) => publishedEvent = message)
            .Returns(Task.CompletedTask);

        var controller = CreateController(
            tenantId,
            dbContext,
            publishEndpoint.Object);

        var result = await controller.SubmitEvent(CreateBeginCheckoutRequest(), CancellationToken.None);

        var accepted = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status202Accepted, accepted.StatusCode);
        Assert.NotNull(publishedEvent);
        Assert.Null(publishedEvent!.AnalyticsEvent.Payload.Value);
    }

    [Fact]
    public async Task SubmitEvent_WhenTenantCurrencyIsBlank_ReturnsBadRequestAndDoesNotPublish()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Order Currency Store",
            Slug = "order-currency-store",
            IsActive = true,
            Currency = " "
        });
        dbContext.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-AN-1",
            Currency = "EUR"
        });
        await dbContext.SaveChangesAsync();

        AnalyticsEventReceivedEvent? publishedEvent = null;
        var publishEndpoint = new Mock<IEventPublisher>();
        publishEndpoint
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<AnalyticsEventReceivedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AnalyticsEventReceivedEvent, CancellationToken>((message, _) => publishedEvent = message)
            .Returns(Task.CompletedTask);

        var controller = CreateController(
            tenantId,
            dbContext,
            publishEndpoint.Object);

        var result = await controller.SubmitEvent(CreateBeginCheckoutRequestWithOrder(orderId), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<AnalyticsEventResponseDto>(badRequest.Value);

        Assert.False(payload.Accepted);
        Assert.Equal("Analytics event rejected because currency is missing.", payload.Error);
        Assert.Null(publishedEvent);
        publishEndpoint.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<AnalyticsEventReceivedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitEvent_WhenOrderCurrencyDoesNotMatchTenant_ReturnsBadRequestAndDoesNotPublish()
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Mismatch Store",
            Slug = "mismatch-store",
            IsActive = true,
            Currency = "SAR"
        });
        dbContext.Orders.Add(new Order
        {
            Id = orderId,
            TenantId = tenantId,
            OrderNumber = "ORD-AN-2",
            Currency = "EUR"
        });
        await dbContext.SaveChangesAsync();

        AnalyticsEventReceivedEvent? publishedEvent = null;
        var publishEndpoint = new Mock<IEventPublisher>();
        publishEndpoint
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<AnalyticsEventReceivedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AnalyticsEventReceivedEvent, CancellationToken>((message, _) => publishedEvent = message)
            .Returns(Task.CompletedTask);

        var controller = CreateController(
            tenantId,
            dbContext,
            publishEndpoint.Object);

        var result = await controller.SubmitEvent(CreateBeginCheckoutRequestWithOrder(orderId), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<AnalyticsEventResponseDto>(badRequest.Value);

        Assert.False(payload.Accepted);
        Assert.Equal("Analytics event rejected because currency is inconsistent with tenant configuration.", payload.Error);
        Assert.Null(publishedEvent);
        publishEndpoint.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<AnalyticsEventReceivedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitEvent_WhenPublishFails_ReturnsServiceUnavailable()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Failure Store",
            Slug = "failure-store",
            IsActive = true,
            Currency = "USD"
        });
        await dbContext.SaveChangesAsync();

        var publishEndpoint = new Mock<IEventPublisher>();
        publishEndpoint
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<AnalyticsEventReceivedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("publish failed"));

        var controller = CreateController(
            tenantId,
            dbContext,
            publishEndpoint.Object);

        var result = await controller.SubmitEvent(CreateBeginCheckoutRequest(), CancellationToken.None);

        var serviceUnavailable = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, serviceUnavailable.StatusCode);
        var payload = Assert.IsType<AnalyticsEventResponseDto>(serviceUnavailable.Value);
        Assert.False(payload.Accepted);
        Assert.Equal("Analytics event could not be stored. Please retry.", payload.Error);
    }

    [Fact]
    public async Task SubmitEvent_WhenPayloadExceedsLimit_ReturnsBadRequestAndDoesNotPublish()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Payload Limit Store",
            Slug = "payload-limit-store",
            IsActive = true,
            Currency = "USD"
        });
        await dbContext.SaveChangesAsync();

        var publishEndpoint = new Mock<IEventPublisher>();
        var controller = CreateController(
            tenantId,
            dbContext,
            publishEndpoint.Object);

        var oversizedText = new string('x', 17 * 1024);
        var request = CreateBeginCheckoutRequest();
        request.Payload!.Extra = new Dictionary<string, object>
        {
            ["blob"] = oversizedText
        };

        var result = await controller.SubmitEvent(request, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var payload = Assert.IsType<AnalyticsEventResponseDto>(badRequest.Value);
        Assert.False(payload.Accepted);
        Assert.Equal("Analytics payload exceeds allowed limits.", payload.Error);
        publishEndpoint.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<AnalyticsEventReceivedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SubmitEvent_WhenPayloadIsWithinLimit_IsAccepted()
    {
        var tenantId = Guid.NewGuid();
        await using var dbContext = CreateDbContext();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Valid Payload Store",
            Slug = "valid-payload-store",
            IsActive = true,
            Currency = "USD"
        });
        await dbContext.SaveChangesAsync();

        AnalyticsEventReceivedEvent? publishedEvent = null;
        var publishEndpoint = new Mock<IEventPublisher>();
        publishEndpoint
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<AnalyticsEventReceivedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AnalyticsEventReceivedEvent, CancellationToken>((message, _) => publishedEvent = message)
            .Returns(Task.CompletedTask);

        var controller = CreateController(
            tenantId,
            dbContext,
            publishEndpoint.Object);

        var request = CreateBeginCheckoutRequest();
        request.Payload!.Extra = new Dictionary<string, object>
        {
            ["step"] = "payment",
            ["attempt"] = 1
        };

        var result = await controller.SubmitEvent(request, CancellationToken.None);

        var accepted = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.NotNull(publishedEvent);
        Assert.Equal(StatusCodes.Status202Accepted, accepted.StatusCode);
    }

    [Fact]
    public async Task SubmitEventBatch_WithMixedValidityBatch_AcceptsValidAndRejectsInvalidWithPerIndexResults()
    {
        var tenantId = Guid.NewGuid();
        var validOrderId = Guid.NewGuid();
        var invalidOrderId = Guid.NewGuid();
        
        await using var dbContext = CreateDbContext();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Mixed Batch Store",
            Slug = "mixed-batch-store",
            IsActive = true,
            Currency = "USD"
        });
        dbContext.Orders.Add(new Order
        {
            Id = validOrderId,
            TenantId = tenantId,
            OrderNumber = "ORD-VALID-1",
            Currency = "USD"
        });
        dbContext.Orders.Add(new Order
        {
            Id = invalidOrderId,
            TenantId = tenantId,
            OrderNumber = "ORD-INVALID-1",
            Currency = "EUR" // Mismatched currency
        });
        await dbContext.SaveChangesAsync();

        var publishedEvents = new List<AnalyticsEventReceivedEvent>();
        var publishEndpoint = new Mock<IEventPublisher>();
        publishEndpoint
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<AnalyticsEventReceivedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AnalyticsEventReceivedEvent, CancellationToken>((message, _) => publishedEvents.Add(message))
            .Returns(Task.CompletedTask);

        var controller = CreateController(
            tenantId,
            dbContext,
            publishEndpoint.Object);

        var batchRequest = new AnalyticsEventBatchRequestDto
        {
            Events = new List<AnalyticsEventRequestDto>
            {
                CreateBeginCheckoutRequestWithOrder(validOrderId),
                CreateBeginCheckoutRequestWithOrder(invalidOrderId)
            }
        };

        var result = await controller.SubmitEventBatch(batchRequest, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        var response = Assert.IsType<AnalyticsEventBatchResponseDto>(accepted.Value);

        Assert.Equal(1, response.Accepted);
        Assert.Equal(1, response.Rejected);
        Assert.Single(response.AcceptedEventIds);
        Assert.Single(response.Errors);
        Assert.Equal(
            "Analytics event rejected because currency is inconsistent with tenant configuration.",
            response.Errors[0].Error);

        publishEndpoint.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<AnalyticsEventReceivedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.Single(publishedEvents);
        Assert.Equal(validOrderId, publishedEvents[0].AnalyticsEvent.Payload.OrderId);
    }

    [Fact]
    public async Task SubmitEventBatch_WithMissingCurrency_AcceptsValidAndRejectsMissingCurrencyWithPerIndexResults()
    {
        var tenantId = Guid.NewGuid();
        var validOrderId = Guid.NewGuid();
        var missingCurrencyOrderId = Guid.NewGuid();
        
        await using var dbContext = CreateDbContext();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Missing Currency Store",
            Slug = "missing-currency-store",
            IsActive = true,
            Currency = "USD"
        });
        dbContext.Orders.Add(new Order
        {
            Id = validOrderId,
            TenantId = tenantId,
            OrderNumber = "ORD-VALID-2",
            Currency = "USD"
        });
        dbContext.Orders.Add(new Order
        {
            Id = missingCurrencyOrderId,
            TenantId = tenantId,
            OrderNumber = "ORD-MISSING-1",
            Currency = string.Empty // Missing order currency
        });
        await dbContext.SaveChangesAsync();

        var publishedEvents = new List<AnalyticsEventReceivedEvent>();
        var publishEndpoint = new Mock<IEventPublisher>();
        publishEndpoint
            .Setup(endpoint => endpoint.PublishAsync(It.IsAny<AnalyticsEventReceivedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<AnalyticsEventReceivedEvent, CancellationToken>((message, _) => publishedEvents.Add(message))
            .Returns(Task.CompletedTask);

        var controller = CreateController(
            tenantId,
            dbContext,
            publishEndpoint.Object);

        var batchRequest = new AnalyticsEventBatchRequestDto
        {
            Events = new List<AnalyticsEventRequestDto>
            {
                CreateBeginCheckoutRequestWithOrder(validOrderId), // Valid event with matching currency
                CreateBeginCheckoutRequestWithOrder(missingCurrencyOrderId) // Invalid - missing currency
            }
        };

        var result = await controller.SubmitEventBatch(batchRequest, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        var response = Assert.IsType<AnalyticsEventBatchResponseDto>(accepted.Value);

        Assert.Equal(1, response.Accepted);
        Assert.Equal(1, response.Rejected);
        Assert.Single(response.AcceptedEventIds);
        Assert.Single(response.Errors);
        Assert.Equal(
            "Analytics event rejected because currency is missing.",
            response.Errors[0].Error);

        publishEndpoint.Verify(
            endpoint => endpoint.PublishAsync(It.IsAny<AnalyticsEventReceivedEvent>(), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.Single(publishedEvents);
        Assert.Equal(validOrderId, publishedEvents[0].AnalyticsEvent.Payload.OrderId);
    }

    [Fact]
    public async Task AnalyticsEventConsumer_WhenBothStoresFail_Throws()
    {
        var hotStore = new Mock<IAnalyticsHotStoreService>();
        hotStore
            .Setup(store => store.IsEventProcessedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        hotStore
            .Setup(store => store.StoreEventAsync(It.IsAny<AnalyticsEvent>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var coldStore = new Mock<IAnalyticsColdStoreService>();
        coldStore
            .Setup(store => store.StoreEventWithOutcomeAsync(It.IsAny<AnalyticsEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AnalyticsEventStoreOutcome.Failed);

        var analyticsSettingsService = new Mock<IAnalyticsSettingsService>();
        analyticsSettingsService
            .Setup(service => service.GetSettingsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TenantAnalyticsSettings?)null);

        var dispatcherPipeline = new AnalyticsDispatcherPipeline(
            Array.Empty<IAnalyticsDispatcher>(),
            analyticsSettingsService.Object,
            NullLogger<AnalyticsDispatcherPipeline>.Instance);

        var consumer = new AnalyticsEventConsumer(
            hotStore.Object,
            coldStore.Object,
            dispatcherPipeline,
            Mock.Of<IAttributionContextResolver>(),
            Mock.Of<IOrderAttributionResolver>(),
            new AnalyticsReliabilityTelemetry(),
            NullLogger<AnalyticsEventConsumer>.Instance);

        var analyticsEvent = new AnalyticsEvent
        {
            EventId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Type = AnalyticsEventType.BeginCheckout,
            OccurredAtUtc = DateTime.UtcNow,
            ReceivedAtUtc = DateTime.UtcNow,
            Actor = new AnalyticsActor
            {
                SessionId = "guest-session",
                IsAuthenticated = false
            },
            Context = new AnalyticsContext
            {
                PageUrl = "https://store.example.com/checkout",
                Currency = "USD",
                Language = "en",
                DeviceType = "desktop",
                IpAddress = "127.0.0.1",
                UserAgent = "UnitTest"
            },
            Payload = new AnalyticsPayload
            {
                Value = 150m
            }
        };

        var context = Mock.Of<ConsumeContext<AnalyticsEventReceivedEvent>>(c =>
            c.Message == AnalyticsEventReceivedEvent.Create(analyticsEvent) &&
            c.CancellationToken == CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Consume(context));
    }

    [Fact]
    public void StorefrontAnalyticsController_MustPersistTheBusOutboxBeforeReturningSuccess()
    {
        var filePath = TryResolveApiPath("Controllers", "StorefrontAnalyticsController.cs");
        if (filePath == null)
        {
            Assert.Fail(
                "StorefrontAnalyticsController.cs not found via TryResolveApiPath. " +
                "This test enforces the outbox-ordering contract and cannot be skipped. " +
                "Verify the file exists in the API project and the build configuration is correct.");
        }

        var source = File.ReadAllText(filePath);

        var indexPublish = source.IndexOf("_eventPublisher.PublishAsync(integrationEvent, cancellationToken);", StringComparison.Ordinal);
        var indexSave = source.IndexOf("_dbContext.SaveChangesAsync(cancellationToken);", StringComparison.Ordinal);
        var indexStatus = source.IndexOf("StatusCodes.Status503ServiceUnavailable", StringComparison.Ordinal);

        Assert.True(indexPublish >= 0, "PublishAsync call not found in StorefrontAnalyticsController");
        Assert.True(indexSave >= 0, "SaveChangesAsync call not found in StorefrontAnalyticsController");
        Assert.True(indexStatus >= 0, "Status503ServiceUnavailable not found in StorefrontAnalyticsController");
        Assert.True(indexPublish < indexSave, 
            "PublishAsync must appear before SaveChangesAsync to ensure outbox-backed event publishing");
    }

    [Fact]
    public void StorefrontAnalyticsController_MustRecordCheckoutFinalizeFailureTelemetryOnlyAfterOutboxCommit()
    {
        var filePath = TryResolveApiPath("Controllers", "StorefrontAnalyticsController.cs");
        if (filePath == null)
        {
            Assert.Fail("StorefrontAnalyticsController.cs not found. This test enforces checkout finalize telemetry durability.");
        }

        var source = File.ReadAllText(filePath);
        var saveChangesIndex = source.IndexOf("_dbContext.SaveChangesAsync(cancellationToken);", StringComparison.Ordinal);
        var telemetryIndex = source.IndexOf("RecordReliabilityMetrics(acceptedRequests);", StringComparison.Ordinal);

        Assert.Contains("AnalyticsEventType.CheckoutFinalizeFailure", source, StringComparison.Ordinal);
        Assert.True(saveChangesIndex >= 0, "Expected analytics events to commit before telemetry is emitted.");
        Assert.True(telemetryIndex > saveChangesIndex, "Expected checkout finalize failure telemetry after outbox commit.");
    }

    private static StorefrontAnalyticsController CreateController(
        Guid tenantId,
        ApplicationDbContext dbContext,
        IEventPublisher publishEndpoint,
        IAnalyticsIngestionRateLimiter? rateLimiter = null)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);

        var limiter = rateLimiter ?? CreateAllowingRateLimiter();

        return new StorefrontAnalyticsController(
            new AnalyticsEventValidator(NullLogger<AnalyticsEventValidator>.Instance),
            new AnalyticsPayloadGuard(),
            limiter,
            new AnalyticsTenantCurrencyResolver(dbContext, NullLogger<AnalyticsTenantCurrencyResolver>.Instance),
            publishEndpoint,
            dbContext,
            new AnalyticsReliabilityTelemetry(),
            new CommerceReliabilityTelemetry(),
            tenantContext,
            NullLogger<StorefrontAnalyticsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static IAnalyticsIngestionRateLimiter CreateAllowingRateLimiter()
    {
        var rateLimiter = new Mock<IAnalyticsIngestionRateLimiter>();
        rateLimiter
            .Setup(limiter => limiter.CheckAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(AnalyticsIngestionRateLimitResult.Allow());
        return rateLimiter.Object;
    }

    private static AnalyticsEventRequestDto CreateBeginCheckoutRequest() =>
        new()
        {
            Type = AnalyticsEventType.BeginCheckout,
            Actor = new AnalyticsActorDto
            {
                SessionId = "guest-session"
            },
            Context = new AnalyticsContextDto
            {
                PageUrl = "https://store.example.com/checkout"
            },
            Payload = new AnalyticsPayloadDto
            {
                Value = 150m
            }
        };

    private static AnalyticsEventRequestDto CreatePurchaseCompletedRequest(Guid orderId) =>
        new()
        {
            Type = AnalyticsEventType.PurchaseCompleted,
            Actor = new AnalyticsActorDto
            {
                SessionId = "guest-session"
            },
            Context = new AnalyticsContextDto
            {
                PageUrl = "https://store.example.com/thank-you"
            },
            Payload = new AnalyticsPayloadDto
            {
                OrderId = orderId,
                Value = 275m
            }
        };

    private static AnalyticsEventRequestDto CreateBeginCheckoutRequestWithOrder(Guid orderId) =>
        new()
        {
            Type = AnalyticsEventType.BeginCheckout,
            Actor = new AnalyticsActorDto
            {
                SessionId = "guest-session"
            },
            Context = new AnalyticsContextDto
            {
                PageUrl = "https://store.example.com/checkout"
            },
            Payload = new AnalyticsPayloadDto
            {
                OrderId = orderId,
                Value = 150m
            }
        };

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static string? TryResolveApiPath(params string[] parts)
    {
        // Strategy 1: Try standard bin/Debug/net*/ layout (4 levels up)
        var standardPath = Path.GetFullPath(Path.Combine(
            new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.API" }
                .Concat(parts)
                .ToArray()));

        if (File.Exists(standardPath))
        {
            return standardPath;
        }

        // Strategy 2: Search for solution root via marker files (.git, .sln)
        var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory);
        if (solutionRoot != null)
        {
            var fromSolutionRoot = Path.Combine(
                new[] { solutionRoot, "PointOfSale.API" }
                    .Concat(parts)
                    .ToArray());

            if (File.Exists(fromSolutionRoot))
            {
                return fromSolutionRoot;
            }
        }

        // Strategy 3: Return null to signal test should be skipped
        return null;
    }

    private static string? FindSolutionRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);

        while (directory != null)
        {
            // Look for solution file or .git directory as markers
            if (directory.GetFiles("*.sln").Any() || 
                directory.GetDirectories(".git").Any())
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
