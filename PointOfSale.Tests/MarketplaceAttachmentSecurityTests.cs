using System.Security.Claims;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Nest;
using PointOfSale.API.Controllers;
using PointOfSale.API.Services;
using PointOfSale.Core.Abstractions;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Data;
using PointOfSale.Infrastructure.Instrumentation;
using PointOfSale.Infrastructure.MultiTenancy;
using PointOfSale.Infrastructure.Services;
using System.Data.Common;

namespace PointOfSale.Tests;

public sealed class MarketplaceAttachmentSecurityTests
{
    [Fact]
    public async Task GetAttachment_WithoutAuthentication_ReturnsUnauthorized()
    {
        await using var context = CreateInMemoryDbContext(Guid.NewGuid());
        var controller = CreateController(
            context,
            Mock.Of<IChatService>(),
            Mock.Of<IR2StorageService>(),
            new ClaimsPrincipal(new ClaimsIdentity()));

        var result = await controller.GetAttachment(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task GetAttachment_CrossTenantRequest_ReturnsForbid()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var message = ServiceRequestMessage.CreateText(
            tenantId,
            requestId,
            userId,
            PointOfSale.Core.Enums.SenderRole.Buyer,
            "Attachment",
            MarketplaceChatAttachmentKey.BuildStoragePath(tenantId, requestId, userId) + "/file.png");

        await using var context = CreateInMemoryDbContext(tenantId);
        context.Set<ServiceRequestMessage>().Add(message);
        await context.SaveChangesAsync();

        var controller = CreateController(
            context,
            new Mock<IChatService>(MockBehavior.Strict).Object,
            Mock.Of<IR2StorageService>(MockBehavior.Strict),
            CreateUser(userId, otherTenantId));

        var result = await controller.GetAttachment(message.Id, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetAttachment_WhenCallerIsAuthorized_ReturnsSignedUrlRedirect()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var objectKey = MarketplaceChatAttachmentKey.BuildStoragePath(tenantId, requestId, userId) + "/file.png";
        var message = ServiceRequestMessage.CreateText(
            tenantId,
            requestId,
            userId,
            PointOfSale.Core.Enums.SenderRole.Buyer,
            "Attachment",
            objectKey);

        await using var context = CreateInMemoryDbContext(tenantId);
        context.Set<ServiceRequestMessage>().Add(message);
        await context.SaveChangesAsync();

        var chatService = new Mock<IChatService>();
        chatService
            .Setup(service => service.CanUserViewConversationAsync(
                requestId,
                userId,
                tenantId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var storageService = new Mock<IR2StorageService>();
        storageService
            .Setup(service => service.CreateDownloadUrlAsync(
                objectKey,
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://signed.example.test/file");

        var controller = CreateController(
            context,
            chatService.Object,
            storageService.Object,
            CreateUser(userId, tenantId));

        var result = await controller.GetAttachment(message.Id, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("https://signed.example.test/file", redirect.Url);
        Assert.Equal("no-store, private, max-age=0", controller.Response.Headers.CacheControl);
        Assert.Equal("no-cache", controller.Response.Headers.Pragma);
        Assert.Equal("0", controller.Response.Headers.Expires);
    }

    [Fact]
    public async Task ChatService_RejectsAbsoluteAttachmentUrlInjection()
    {
        var tenantId = Guid.NewGuid();
        var buyerUserId = Guid.NewGuid();
        var providerUserId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        RegisterSqliteFunctions(connection);

        await using (var bootstrapContext = CreateSqliteDbContext(connection, tenantId))
        {
            await bootstrapContext.Database.EnsureCreatedAsync();

            var profile = MarketplaceProfile.Create(
                tenantId,
                providerUserId,
                "Provider Profile",
                "Provider bio");
            var service = Service.Create(
                tenantId,
                profile.Id,
                "Provider Service",
                "Secure chat service");

            var request = ServiceRequest.Create(
                tenantId,
                service.Id,
                buyerUserId,
                providerUserId,
                profile.Id,
                service.Title,
                service.Category,
                service.StartingPrice,
                service.PricingType,
                service.Currency,
                service.Packages,
                "Need help");
            request.Accept();

            bootstrapContext.Set<MarketplaceProfile>().Add(profile);
            bootstrapContext.Set<Service>().Add(service);
            bootstrapContext.Set<ServiceRequest>().Add(request);
            await bootstrapContext.SaveChangesAsync();
        }

        await using var context = CreateSqliteDbContext(connection, tenantId);
        var chatService = new ChatService(context, CreateTenantContext(tenantId));
        var requestId = await context.Set<ServiceRequest>()
            .IgnoreQueryFilters()
            .Select(item => item.Id)
            .SingleAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            chatService.SendMessageAsync(
                requestId,
                buyerUserId,
                tenantId,
                "hello",
                "https://evil.example.test/steal.png",
                CancellationToken.None));

        Assert.Contains("server-issued tenant-scoped object key", ex.Message, StringComparison.Ordinal);
        Assert.Empty(await context.Set<ServiceRequestMessage>().ToListAsync());
    }

    [Fact]
    public async Task ChatService_RejectsAttachmentReferenceThatWasNotServerIssuedForSender()
    {
        var tenantId = Guid.NewGuid();
        var buyerUserId = Guid.NewGuid();
        var providerUserId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        RegisterSqliteFunctions(connection);

        await using (var bootstrapContext = CreateSqliteDbContext(connection, tenantId))
        {
            await bootstrapContext.Database.EnsureCreatedAsync();

            var profile = MarketplaceProfile.Create(
                tenantId,
                providerUserId,
                "Provider Profile",
                "Provider bio");
            var service = Service.Create(
                tenantId,
                profile.Id,
                "Provider Service",
                "Secure chat service");

            var request = ServiceRequest.Create(
                tenantId,
                service.Id,
                buyerUserId,
                providerUserId,
                profile.Id,
                service.Title,
                service.Category,
                service.StartingPrice,
                service.PricingType,
                service.Currency,
                service.Packages,
                "Need help");
            request.Accept();

            bootstrapContext.Set<MarketplaceProfile>().Add(profile);
            bootstrapContext.Set<Service>().Add(service);
            bootstrapContext.Set<ServiceRequest>().Add(request);
            await bootstrapContext.SaveChangesAsync();
        }

        await using var context = CreateSqliteDbContext(connection, tenantId);
        var chatService = new ChatService(context, CreateTenantContext(tenantId));
        var requestId = await context.Set<ServiceRequest>()
            .IgnoreQueryFilters()
            .Select(item => item.Id)
            .SingleAsync();

        var forgedObjectKey = MarketplaceChatAttachmentKey.BuildStoragePath(
            tenantId,
            requestId,
            providerUserId) + "/forged.png";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            chatService.SendMessageAsync(
                requestId,
                buyerUserId,
                tenantId,
                "hello",
                forgedObjectKey,
                CancellationToken.None));

        Assert.Contains("server-issued tenant-scoped object key", ex.Message, StringComparison.Ordinal);
        Assert.Empty(await context.Set<ServiceRequestMessage>().ToListAsync());
    }

    [Fact]
    public async Task ChatService_RejectsTenantScopedAttachmentWhenDurableObjectWasNotIssued()
    {
        var tenantId = Guid.NewGuid();
        var buyerUserId = Guid.NewGuid();
        var providerUserId = Guid.NewGuid();

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        RegisterSqliteFunctions(connection);

        await using (var bootstrapContext = CreateSqliteDbContext(connection, tenantId))
        {
            await bootstrapContext.Database.EnsureCreatedAsync();

            var profile = MarketplaceProfile.Create(
                tenantId,
                providerUserId,
                "Provider Profile",
                "Provider bio");
            var service = Service.Create(
                tenantId,
                profile.Id,
                "Provider Service",
                "Secure chat service");

            var request = ServiceRequest.Create(
                tenantId,
                service.Id,
                buyerUserId,
                providerUserId,
                profile.Id,
                service.Title,
                service.Category,
                service.StartingPrice,
                service.PricingType,
                service.Currency,
                service.Packages,
                "Need help");
            request.Accept();

            bootstrapContext.Set<MarketplaceProfile>().Add(profile);
            bootstrapContext.Set<Service>().Add(service);
            bootstrapContext.Set<ServiceRequest>().Add(request);
            await bootstrapContext.SaveChangesAsync();
        }

        await using var context = CreateSqliteDbContext(connection, tenantId);
        var requestId = await context.Set<ServiceRequest>()
            .IgnoreQueryFilters()
            .Select(item => item.Id)
            .SingleAsync();
        var forgedObjectKey = MarketplaceChatAttachmentKey.BuildStoragePath(
            tenantId,
            requestId,
            buyerUserId) + "/forged.png";
        var verifier = new StubAttachmentReferenceVerifier(false);
        var chatService = new ChatService(
            context,
            CreateTenantContext(tenantId),
            new MarketplaceTelemetry(),
            verifier);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            chatService.SendMessageAsync(
                requestId,
                buyerUserId,
                tenantId,
                "hello",
                forgedObjectKey,
                CancellationToken.None));

        Assert.Contains("server-issued tenant-scoped object key", ex.Message, StringComparison.Ordinal);
        Assert.Equal([forgedObjectKey], verifier.CheckedObjectKeys);
        Assert.Empty(await context.Set<ServiceRequestMessage>().ToListAsync());
    }

    [Fact]
    public async Task GetAttachment_WithDurableObjectKey_DoesNotDependOnLocalDiskAfterRestart()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var objectKey = MarketplaceChatAttachmentKey.BuildStoragePath(tenantId, requestId, userId) + "/file.png";
        var message = ServiceRequestMessage.CreateText(
            tenantId,
            requestId,
            userId,
            PointOfSale.Core.Enums.SenderRole.Buyer,
            "Attachment",
            objectKey);

        await using var context = CreateInMemoryDbContext(tenantId);
        context.Set<ServiceRequestMessage>().Add(message);
        await context.SaveChangesAsync();

        var chatService = new Mock<IChatService>();
        chatService
            .Setup(service => service.CanUserViewConversationAsync(
                requestId,
                userId,
                tenantId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var storageService = new Mock<IR2StorageService>();
        storageService
            .Setup(service => service.CreateDownloadUrlAsync(
                objectKey,
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://signed.example.test/file");

        var nonexistentWebRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing");

        var controller = CreateController(
            context,
            chatService.Object,
            storageService.Object,
            CreateUser(userId, tenantId),
            nonexistentWebRoot);

        var result = await controller.GetAttachment(message.Id, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("https://signed.example.test/file", redirect.Url);
        storageService.Verify(service => service.CreateDownloadUrlAsync(
            objectKey,
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAttachment_LegacyLocalReference_FailsClosed()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var message = ServiceRequestMessage.CreateText(
            tenantId,
            requestId,
            userId,
            PointOfSale.Core.Enums.SenderRole.Buyer,
            "Attachment",
            MarketplaceChatAttachmentKey.BuildStoragePath(tenantId, requestId, userId) + "/file.png");

        var propertyInfo = typeof(ServiceRequestMessage)
            .GetProperty(
                nameof(ServiceRequestMessage.AttachmentUrl),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(propertyInfo);
        propertyInfo.SetValue(message, "/uploads/chat/legacy-proof.png");

        await using var context = CreateInMemoryDbContext(tenantId);
        context.Set<ServiceRequestMessage>().Add(message);
        await context.SaveChangesAsync();

        var chatService = new Mock<IChatService>();
        chatService
            .Setup(service => service.CanUserViewConversationAsync(
                requestId,
                userId,
                tenantId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var storageService = new Mock<IR2StorageService>(MockBehavior.Strict);

        var controller = CreateController(
            context,
            chatService.Object,
            storageService.Object,
            CreateUser(userId, tenantId),
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing"));

        var result = await controller.GetAttachment(message.Id, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        storageService.VerifyNoOtherCalls();
    }

    private static MarketplaceController CreateController(
        ApplicationDbContext context,
        IChatService chatService,
        IR2StorageService storageService,
        ClaimsPrincipal user,
        string? webRootPath = null)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.SetupGet(item => item.WebRootPath).Returns(webRootPath ?? Path.GetTempPath());

        var httpContext = new DefaultHttpContext
        {
            User = user
        };

        return new MarketplaceController(
            Mock.Of<IMarketplaceSearchService>(),
            Mock.Of<IMarketplaceProfileService>(),
            Mock.Of<IServiceCatalogService>(),
            Mock.Of<IServiceRequestService>(),
            Mock.Of<IServiceReviewService>(),
            chatService,
            Mock.Of<IProviderTrustService>(),
            Mock.Of<IFileValidationService>(),
            storageService,
            context,
            Mock.Of<IElasticClient>(),
            environment.Object,
            new MarketplaceTelemetry(),
            NullLogger<MarketplaceController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static ClaimsPrincipal CreateUser(Guid userId, Guid tenantId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString("D")),
                new Claim("tenant_id", tenantId.ToString("D"))
            },
            "Cookies"));
    }

    private static ApplicationDbContext CreateInMemoryDbContext(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options, CreateTenantContext(tenantId));
    }

    private static ApplicationDbContext CreateSqliteDbContext(SqliteConnection connection, Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new SqliteForUpdateStripInterceptor())
            .Options;

        return new ApplicationDbContext(options, CreateTenantContext(tenantId));
    }

    private static void RegisterSqliteFunctions(SqliteConnection connection)
    {
        connection.CreateFunction<string?, string?>("btrim", input => input?.Trim());
        connection.CreateFunction<string?, string?, string?>(
            "btrim",
            (input, characters) => input?.Trim((characters ?? " \t\n\r").ToCharArray()));
        connection.CreateFunction<string?, int>("char_length", input => input?.Length ?? 0);
        connection.CreateFunction<string?, string?, string?, string?, string?>(
            "REGEXP_REPLACE",
            (input, pattern, replacement, options) =>
            {
                if (input == null)
                {
                    return null;
                }

                var regexOptions = RegexOptions.None;
                if (!string.IsNullOrWhiteSpace(options) &&
                    options.Contains('i', StringComparison.OrdinalIgnoreCase))
                {
                    regexOptions |= RegexOptions.IgnoreCase;
                }

                return Regex.Replace(
                    input,
                    pattern ?? string.Empty,
                    replacement ?? string.Empty,
                    regexOptions);
            });
    }

    private static TenantContext CreateTenantContext(Guid tenantId)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetTenantId(tenantId);
        return tenantContext;
    }

    private sealed class SqliteForUpdateStripInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            command.CommandText = command.CommandText.Replace(
                "\"Last4\" ~ '^[0-9]{4}$'",
                "\"Last4\" GLOB '[0-9][0-9][0-9][0-9]'",
                StringComparison.Ordinal);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            command.CommandText = command.CommandText.Replace("FOR UPDATE", string.Empty, StringComparison.OrdinalIgnoreCase);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class StubAttachmentReferenceVerifier : IChatAttachmentReferenceVerifier
    {
        private readonly bool _isServerIssued;

        public StubAttachmentReferenceVerifier(bool isServerIssued)
        {
            _isServerIssued = isServerIssued;
        }

        public List<string> CheckedObjectKeys { get; } = [];

        public Task<bool> IsServerIssuedAttachmentAsync(
            string objectKey,
            CancellationToken cancellationToken = default)
        {
            CheckedObjectKeys.Add(objectKey);
            return Task.FromResult(_isServerIssued);
        }
    }
}
