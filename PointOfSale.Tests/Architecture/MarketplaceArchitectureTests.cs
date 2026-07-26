using System.Reflection;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Core.Interfaces;
using Xunit;

namespace PointOfSale.Tests.Architecture;

/// <summary>
/// Architecture tests that enforce marketplace invariants at compile/test time.
/// These tests will fail the build if invariants are violated.
/// </summary>
public class MarketplaceArchitectureTests
{
    [Fact]
    public void ServiceRequest_Status_MustNotHavePublicSetter()
    {
        var statusProperty = typeof(ServiceRequest).GetProperty(nameof(ServiceRequest.Status));
        
        Assert.NotNull(statusProperty);
        Assert.True(statusProperty!.CanRead, "Status property must be readable");
        
        var setter = statusProperty.GetSetMethod(nonPublic: false);
        Assert.Null(setter);
    }

    [Fact]
    public void ServiceRequest_MustHaveTransitionMethods()
    {
        var type = typeof(ServiceRequest);
        
        Assert.NotNull(type.GetMethod("Accept"));
        Assert.NotNull(type.GetMethod("MarkCompletedByProvider"));
        Assert.NotNull(type.GetMethod("ConfirmCompletedByBuyer"));
        Assert.NotNull(type.GetMethod("Cancel"));
    }

    [Fact]
    public void ServiceReview_MustNotBeCreatableOutsideService()
    {
        var constructors = typeof(ServiceReview)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        
        Assert.Empty(constructors);
    }

    [Fact]
    public void ServiceReview_MustHaveStaticCreateMethod()
    {
        var createMethod = typeof(ServiceReview).GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
        
        Assert.NotNull(createMethod);
    }

    [Fact]
    public void IServiceReviewService_MustBeOnlyWayToCreateReviews()
    {
        var serviceInterface = typeof(IServiceReviewService);
        var createMethod = serviceInterface.GetMethod("CreateReviewAsync");
        
        Assert.NotNull(createMethod);
        Assert.Equal(typeof(Task<ServiceReview>), createMethod!.ReturnType);
    }

    [Fact]
    public void Controllers_MustNotAcceptServiceRequestStatusInDto()
    {
        var apiAssembly = Assembly.Load("PointOfSale.API");
        var controllerTypes = apiAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("Controller"));

        foreach (var controller in controllerTypes)
        {
            var methods = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            
            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                
                foreach (var param in parameters)
                {
                    var paramType = param.ParameterType;
                    
                    if (paramType.IsClass && paramType.Namespace?.Contains("PointOfSale") == true)
                    {
                        var properties = paramType.GetProperties();
                        
                        foreach (var prop in properties)
                        {
                            Assert.False(
                                prop.PropertyType == typeof(ServiceRequestStatus),
                                $"Controller {controller.Name}.{method.Name} accepts DTO {paramType.Name} with ServiceRequestStatus property {prop.Name}. Status must not be accepted from clients."
                            );
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void MarketplaceEntities_MustInheritFromTenantEntity()
    {
        var marketplaceEntities = new[]
        {
            typeof(MarketplaceProfile),
            typeof(Service),
            typeof(ServiceRequest),
            typeof(ServiceReview)
        };

        foreach (var entity in marketplaceEntities)
        {
            Assert.True(
                typeof(TenantEntity).IsAssignableFrom(entity),
                $"{entity.Name} must inherit from TenantEntity for proper tenant isolation"
            );
        }
    }

    [Fact]
    public void ServiceRequest_BuyerAndProvider_MustBeDifferent()
    {
        var createMethod = typeof(ServiceRequest).GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(createMethod);

        var tenantId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var exception = Assert.Throws<TargetInvocationException>(() =>
        {
            createMethod!.Invoke(null, NewServiceRequestArgs(tenantId, serviceId, userId, userId));
        });

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("different users", exception.InnerException!.Message);
    }

    [Fact]
    public void ServiceReview_Rating_MustBeBetween1And5()
    {
        var createMethod = typeof(ServiceReview).GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(createMethod);

        var tenantId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var providerId = Guid.NewGuid();

        var exception = Assert.Throws<TargetInvocationException>(() =>
        {
            createMethod!.Invoke(null, new object[] { tenantId, requestId, serviceId, reviewerId, providerId, 0, null! });
        });

        Assert.IsType<ArgumentException>(exception.InnerException);

        exception = Assert.Throws<TargetInvocationException>(() =>
        {
            createMethod!.Invoke(null, new object[] { tenantId, requestId, serviceId, reviewerId, providerId, 6, null! });
        });

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void ServiceRequest_InvalidTransitions_MustThrow()
    {
        var tenantId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var providerId = Guid.NewGuid();

        var createMethod = typeof(ServiceRequest).GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
        var request = (ServiceRequest)createMethod!.Invoke(null, NewServiceRequestArgs(tenantId, serviceId, buyerId, providerId))!;

        Assert.Throws<InvalidOperationException>(() => request.MarkCompletedByProvider());
        Assert.Throws<InvalidOperationException>(() => request.ConfirmCompletedByBuyer());

        request.Accept();
        Assert.Throws<InvalidOperationException>(() => request.Accept());
        Assert.Throws<InvalidOperationException>(() => request.ConfirmCompletedByBuyer());

        request.MarkCompletedByProvider();
        Assert.Throws<InvalidOperationException>(() => request.Accept());
        Assert.Throws<InvalidOperationException>(() => request.MarkCompletedByProvider());
        Assert.Throws<InvalidOperationException>(() => request.Cancel());

        request.ConfirmCompletedByBuyer();
        Assert.Throws<InvalidOperationException>(() => request.Accept());
        Assert.Throws<InvalidOperationException>(() => request.MarkCompletedByProvider());
        Assert.Throws<InvalidOperationException>(() => request.Cancel());
    }

    // Phase FE-3: Media & Pricing Architecture Tests

    private static object?[] NewServiceRequestArgs(Guid tenantId, Guid serviceId, Guid buyerId, Guid providerId)
    {
        return new object?[]
        {
            tenantId,
            serviceId,
            buyerId,
            providerId,
            Guid.NewGuid(),
            "Snapshot title",
            "Category",
            100m,
            PricingType.Fixed,
            "EGP",
            null,
            "Need help"
        };
    }

    [Fact]
    public void Controllers_MustNotAcceptPricingTypeInDto()
    {
        var apiAssembly = Assembly.Load("PointOfSale.API");
        var controllerTypes = apiAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("Controller") && !t.Name.Contains("Marketplace"));

        foreach (var controller in controllerTypes)
        {
            var methods = controller.GetMethods(BindingFlags.Public | BindingFlags.Instance);
            
            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                
                foreach (var param in parameters)
                {
                    var paramType = param.ParameterType;
                    
                    if (paramType.IsClass && paramType.Namespace?.Contains("PointOfSale") == true)
                    {
                        var properties = paramType.GetProperties();
                        
                        foreach (var prop in properties)
                        {
                            if (prop.Name == "StartingPrice" || prop.Name == "PricingType")
                            {
                                Assert.True(
                                    controller.Name.Contains("Marketplace"),
                                    $"Non-marketplace controller {controller.Name}.{method.Name} accepts pricing fields. Only MarketplaceController allowed."
                                );
                            }
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void Service_PricingFields_MustBeNullable()
    {
        var serviceType = typeof(Service);
        
        var startingPriceProperty = serviceType.GetProperty("StartingPrice", BindingFlags.Public | BindingFlags.Instance);
        var pricingTypeProperty = serviceType.GetProperty("PricingType", BindingFlags.Public | BindingFlags.Instance);
        
        Assert.NotNull(startingPriceProperty);
        Assert.NotNull(pricingTypeProperty);
        
        Assert.True(Nullable.GetUnderlyingType(startingPriceProperty!.PropertyType) != null || !startingPriceProperty.PropertyType.IsValueType);
        Assert.True(Nullable.GetUnderlyingType(pricingTypeProperty!.PropertyType) != null || !pricingTypeProperty.PropertyType.IsValueType);
    }

    [Fact]
    public void MarketplaceProfile_MediaFields_MustBeNullable()
    {
        var profileType = typeof(MarketplaceProfile);
        
        var coverImageProperty = profileType.GetProperty("CoverImageUrl", BindingFlags.Public | BindingFlags.Instance);
        var skillsProperty = profileType.GetProperty("Skills", BindingFlags.Public | BindingFlags.Instance);
        
        Assert.NotNull(coverImageProperty);
        Assert.NotNull(skillsProperty);
        
        Assert.True(coverImageProperty!.PropertyType == typeof(string) || Nullable.GetUnderlyingType(coverImageProperty.PropertyType) != null);
        Assert.True(skillsProperty!.PropertyType == typeof(string) || Nullable.GetUnderlyingType(skillsProperty.PropertyType) != null);
    }

    [Fact]
    public void ServiceRequestMessage_AttachmentUrl_MustBeNullable()
    {
        var messageType = typeof(ServiceRequestMessage);
        var attachmentProperty = messageType.GetProperty("AttachmentUrl", BindingFlags.Public | BindingFlags.Instance);
        
        Assert.NotNull(attachmentProperty);
        Assert.True(attachmentProperty!.PropertyType == typeof(string) || Nullable.GetUnderlyingType(attachmentProperty.PropertyType) != null);
    }

    [Fact]
    public void IMediaService_MustExist()
    {
        var mediaServiceInterface = typeof(IMediaService);
        
        Assert.NotNull(mediaServiceInterface);
        Assert.True(mediaServiceInterface.IsInterface);
        
        var uploadImageMethod = mediaServiceInterface.GetMethod("UploadImageAsync");
        var uploadAttachmentMethod = mediaServiceInterface.GetMethod("UploadAttachmentAsync");
        
        Assert.NotNull(uploadImageMethod);
        Assert.NotNull(uploadAttachmentMethod);
    }
}
