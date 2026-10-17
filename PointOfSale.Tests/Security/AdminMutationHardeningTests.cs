using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using PointOfSale.API.Controllers;

namespace PointOfSale.Tests.Security;

public sealed class AdminMutationHardeningTests
{
    [Theory]
    [InlineData(nameof(PromotionsController.CreatePromotion))]
    [InlineData(nameof(PromotionsController.UpdatePromotion))]
    [InlineData(nameof(PromotionsController.DeletePromotion))]
    [InlineData(nameof(PromotionsController.TogglePromotionStatus))]
    [InlineData(nameof(PromotionsController.SyncPromotionsToMongo))]
    public void PromotionsController_Mutations_MustRequireOwnerAdminManager(string methodName)
    {
        var method = typeof(PromotionsController).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);

        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>(inherit: true);
        Assert.NotNull(authorize);
        Assert.Equal("Owner,Admin,Manager", authorize!.Roles);
    }

    [Fact]
    public void AdminSmartOffersController_SyncGeneratedCoupons_MustRequireOwnerAdminManager()
    {
        var method = typeof(AdminSmartOffersController).GetMethod(nameof(AdminSmartOffersController.SyncGeneratedCouponsToMongo), BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);

        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>(inherit: true);
        Assert.NotNull(authorize);
        Assert.Equal("Owner,Admin,Manager", authorize!.Roles);
    }

    [Fact]
    public void Program_MustDefine_PublicReadRateLimitPolicy()
    {
        var source = File.ReadAllText(ApiPath("Program.cs"));

        // Extract the rate limiting configuration method/section
        // Look for the AddRateLimiter call and its options configuration
        // Use non-greedy match (.*?) with Singleline to handle nested braces
        var rateLimiterPattern = @"\.AddRateLimiter\s*\(\s*options\s*=>\s*\{.*?options\.AddPolicy\s*\(\s*""PublicReadRateLimit"".*?\}\s*\)";
        var match = System.Text.RegularExpressions.Regex.Match(source, rateLimiterPattern, 
            System.Text.RegularExpressions.RegexOptions.Singleline);
        
        Assert.True(match.Success, "PublicReadRateLimit policy not found in AddRateLimiter configuration in Program.cs");
    }

    [Fact]
    public void StorefrontSmartOffers_MustUse_PublicReadRateLimit()
    {
        var method = typeof(StorefrontSmartOffersController).GetMethod(nameof(StorefrontSmartOffersController.GetSmartOffers))!;
        Assert.Equal("PublicReadRateLimit", method.GetCustomAttribute<Microsoft.AspNetCore.RateLimiting.EnableRateLimitingAttribute>()?.PolicyName);
        Assert.NotNull(method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpGetAttribute>());
        Assert.True(method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.ResponseCacheAttribute>()?.NoStore);
    }

    [Fact]
    public void StorefrontManualWalletConfig_MustUse_PublicReadRateLimit()
    {
        var source = File.ReadAllText(ApiPath("Controllers", "StorefrontManualWalletController.cs"));

        // Extract the GetConfig method to ensure rate limiting is applied to the correct method
        var methodPattern = @"\[HttpGet\(""config""\)\]\s*(?:\[[^\]]+\]\s*)*public\s+async\s+Task<IActionResult>\s+GetConfig\s*\([^\)]*\)\s*\{";
        var match = System.Text.RegularExpressions.Regex.Match(source, methodPattern, 
            System.Text.RegularExpressions.RegexOptions.Singleline);
        
        Assert.True(match.Success, "GetConfig method not found in StorefrontManualWalletController.cs");
        
        var methodSignature = match.Value;
        
        // Verify [EnableRateLimiting("PublicReadRateLimit")] is applied to the GetConfig method
        Assert.Contains("[EnableRateLimiting(\"PublicReadRateLimit\")]", methodSignature, StringComparison.Ordinal);
    }

    private static string ApiPath(params string[] parts) =>
        Path.GetFullPath(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "PointOfSale.API" }.Concat(parts).ToArray()));
}
