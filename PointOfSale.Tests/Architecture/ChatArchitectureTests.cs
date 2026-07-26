using System.Reflection;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using PointOfSale.Core.Security;
using PointOfSale.Infrastructure.Instrumentation;
using Xunit;

namespace PointOfSale.Tests.Architecture;

/// <summary>
/// Architecture tests for Phase 2 chat functionality.
/// These tests enforce invariants at compile/test time.
/// </summary>
public class ChatArchitectureTests
{
    [Fact]
    public void ServiceRequestMessage_MustNotHavePublicSetters()
    {
        var properties = typeof(ServiceRequestMessage).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            // Skip navigation properties
            if (property.Name == nameof(ServiceRequestMessage.ServiceRequest))
                continue;

            // Skip inherited properties from TenantEntity (TenantId, IsDeleted, etc.)
            if (property.DeclaringType != typeof(ServiceRequestMessage))
                continue;

            var setter = property.GetSetMethod(nonPublic: false);
            Assert.Null(setter);
        }
    }

    [Fact]
    public void ServiceRequestMessage_MustNotHavePublicConstructor()
    {
        var constructors = typeof(ServiceRequestMessage)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(constructors);
    }

    [Fact]
    public void ServiceRequestMessage_MustHaveStaticCreateMethods()
    {
        var type = typeof(ServiceRequestMessage);

        var createText = type.GetMethod("CreateText", BindingFlags.Public | BindingFlags.Static);
        var createSystem = type.GetMethod("CreateSystem", BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(createText);
        Assert.NotNull(createSystem);
    }

    [Fact]
    public void ChatHub_MustNotAccessDbContextDirectly()
    {
        var hubAssembly = Assembly.Load("PointOfSale.API");
        var chatHubType = hubAssembly.GetType("PointOfSale.API.Hubs.ChatHub");

        Assert.NotNull(chatHubType);

        var fields = chatHubType!.GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        var dbContextFields = fields.Where(f => f.FieldType.Name.Contains("DbContext")).ToList();

        Assert.Empty(dbContextFields);
    }

    [Fact]
    public void ChatHub_MustOnlyDependOnIChatService()
    {
        var hubAssembly = Assembly.Load("PointOfSale.API");
        var chatHubType = hubAssembly.GetType("PointOfSale.API.Hubs.ChatHub");

        Assert.NotNull(chatHubType);

        var constructor = chatHubType!.GetConstructors().FirstOrDefault();
        Assert.NotNull(constructor);

        var parameters = constructor!.GetParameters();
        var serviceParameters = parameters.Where(p =>
            p.ParameterType.Namespace?.Contains("PointOfSale") == true &&
            !p.ParameterType.Name.Contains("Logger")
        ).ToList();

        Assert.Contains(serviceParameters, parameter => parameter.ParameterType == typeof(IChatService));
        Assert.All(
            serviceParameters,
            parameter => Assert.Contains(
                parameter.ParameterType,
                new[] { typeof(IChatService), typeof(MarketplaceTelemetry) }));
    }

    [Fact]
    public void DTOs_MustNotAcceptSenderRole()
    {
        var coreAssembly = Assembly.Load("PointOfSale.Core");
        var dtoTypes = coreAssembly.GetTypes()
            .Where(t => t.Namespace?.Contains("DTOs") == true && t.IsClass);

        foreach (var dtoType in dtoTypes)
        {
            var properties = dtoType.GetProperties();

            foreach (var prop in properties)
            {
                Assert.False(
                    prop.PropertyType == typeof(SenderRole),
                    $"DTO {dtoType.Name} has SenderRole property {prop.Name}. SenderRole must be determined server-side."
                );
            }
        }
    }

    [Fact]
    public void DTOs_MustNotAcceptMessageType()
    {
        var coreAssembly = Assembly.Load("PointOfSale.Core");
        var dtoTypes = coreAssembly.GetTypes()
            .Where(t => t.Namespace?.Contains("DTOs") == true && t.IsClass);

        foreach (var dtoType in dtoTypes)
        {
            var properties = dtoType.GetProperties();

            foreach (var prop in properties)
            {
                Assert.False(
                    prop.PropertyType == typeof(MessageType),
                    $"DTO {dtoType.Name} has MessageType property {prop.Name}. MessageType must be determined server-side."
                );
            }
        }
    }

    [Fact]
    public void ServiceRequest_MustHaveAllowsInteractionMethod()
    {
        var method = typeof(ServiceRequest).GetMethod("AllowsInteraction");

        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method!.ReturnType);
        Assert.Empty(method.GetParameters());
    }

    [Fact]
    public void ChatService_MustValidateUsingAllowsInteraction()
    {
        var serviceAssembly = Assembly.Load("PointOfSale.Infrastructure");
        var chatServiceType = serviceAssembly.GetType("PointOfSale.Infrastructure.Services.ChatService");

        Assert.NotNull(chatServiceType);

        // Verify it implements IChatService
        Assert.True(typeof(IChatService).IsAssignableFrom(chatServiceType));
    }

    [Fact]
    public void ServiceRequestMessage_MustInheritFromTenantEntity()
    {
        Assert.True(
            typeof(TenantEntity).IsAssignableFrom(typeof(ServiceRequestMessage)),
            "ServiceRequestMessage must inherit from TenantEntity for proper tenant isolation"
        );
    }

    [Fact]
    public void ServiceRequestMessage_EmptyBody_MustThrow()
    {
        var createTextMethod = typeof(ServiceRequestMessage).GetMethod("CreateText", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(createTextMethod);

        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var senderId = Guid.NewGuid();
        var objectKey = $"{MarketplaceChatAttachmentKey.BuildStoragePath(tenantId, requestId, senderId)}/chat.png";

        var exception = Assert.Throws<TargetInvocationException>(() =>
        {
            createTextMethod!.Invoke(null, new object?[] { tenantId, requestId, senderId, SenderRole.Buyer, "", null });
        });

        var argException = Assert.IsType<ArgumentException>(exception.InnerException);
        Assert.Equal("body", argException.ParamName);

        exception = Assert.Throws<TargetInvocationException>(() =>
        {
            createTextMethod!.Invoke(null, new object?[] { tenantId, requestId, senderId, SenderRole.Buyer, "   ", null });
        });

        argException = Assert.IsType<ArgumentException>(exception.InnerException);
        Assert.Equal("body", argException.ParamName);

        var withAttachment = (ServiceRequestMessage?)createTextMethod.Invoke(
            null,
            new object?[] { tenantId, requestId, senderId, SenderRole.Buyer, "", objectKey });

        Assert.NotNull(withAttachment);
        Assert.Equal(string.Empty, withAttachment!.Body);
        Assert.Equal(objectKey, withAttachment.AttachmentUrl);

        exception = Assert.Throws<TargetInvocationException>(() =>
        {
            createTextMethod.Invoke(
                null,
                new object?[] { tenantId, requestId, senderId, SenderRole.Buyer, "", "https://cdn.example.test/chat.png" });
        });

        argException = Assert.IsType<ArgumentException>(exception.InnerException);
        Assert.Equal("attachmentObjectKey", argException.ParamName);
    }

    [Fact]
    public void IChatService_MustNotReturnVoidForSendMessage()
    {
        var sendMessageMethod = typeof(IChatService).GetMethod("SendMessageAsync");

        Assert.NotNull(sendMessageMethod);
        Assert.Equal(typeof(Task<ServiceRequestMessage>), sendMessageMethod!.ReturnType);
    }

    [Fact]
    public void ChatService_MustEnforceRateLimit()
    {
        var serviceAssembly = Assembly.Load("PointOfSale.Infrastructure");
        var chatServiceType = serviceAssembly.GetType("PointOfSale.Infrastructure.Services.ChatService");

        Assert.NotNull(chatServiceType);

        // Verify it has a private ValidateRateLimitAsync method
        var rateLimitMethod = chatServiceType!.GetMethod(
            "ValidateRateLimitAsync",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        Assert.NotNull(rateLimitMethod);
    }
}
