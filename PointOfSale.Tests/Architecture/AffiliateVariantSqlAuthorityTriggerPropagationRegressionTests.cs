using MassTransit;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PointOfSale.Core.Events;
using PointOfSale.Infrastructure.Consumers.Affiliate;

namespace PointOfSale.Tests.Architecture;

public sealed class AffiliateVariantSqlAuthorityTriggerPropagationRegressionTests
{
    private static readonly Type[] VariantEventContracts =
    {
        typeof(ProductVariantCreatedEvent),
        typeof(ProductVariantUpdatedEvent),
        typeof(ProductVariantDeletedEvent),
        typeof(VariantPriceUpdatedEvent)
    };

    [Theory]
    [InlineData(typeof(AffiliateProductPolicyProjectionConsumer))]
    [InlineData(typeof(AffiliateProductIntelligenceProjectionConsumer))]
    public void Consumer_DeclaresAndHandlesEveryVariantPropagationContract(Type consumerType)
    {
        foreach (var contract in VariantEventContracts)
        {
            var consumerContract = typeof(IConsumer<>).MakeGenericType(contract);
            var contextType = typeof(ConsumeContext<>).MakeGenericType(contract);

            Assert.Contains(consumerContract, consumerType.GetInterfaces());
            Assert.NotNull(consumerType.GetMethod("Consume", new[] { contextType }));
        }
    }

    [Theory]
    [InlineData("AffiliateProductPolicyProjectionConsumer.cs", "RecomposePublicAffiliateProductFromSqlAuthorityAsync", "SyncPublicAffiliateProductAsync")]
    [InlineData("AffiliateProductIntelligenceProjectionConsumer.cs", "RecomposeProductIntelligenceFromSqlAuthorityAsync", "LoadCurrentStorefrontAuthorityProductAsync")]
    public void VariantHandlers_ArePayloadBlindTriggersForSqlAuthorityRecomposition(
        string fileName,
        string recompositionMethod,
        string sqlAuthorityMethod)
    {
        var source = File.ReadAllText(InfrastructurePath("Consumers", "Affiliate", fileName));
        var methods = CSharpSyntaxTree.ParseText(source)
            .GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .ToList();

        foreach (var contract in VariantEventContracts)
        {
            var handler = Assert.Single(methods, method =>
                method.Identifier.ValueText == "Consume" &&
                method.ParameterList.ToString().Contains($"ConsumeContext<{contract.Name}>", StringComparison.Ordinal));
            var handlerSource = handler.ToFullString();

            Assert.Contains("ConsumeProductVariantTriggerFromSqlAuthorityAsync", handlerSource, StringComparison.Ordinal);
            Assert.Contains("context.Message.TenantId", handlerSource, StringComparison.Ordinal);
            Assert.Contains("context.Message.ProductId", handlerSource, StringComparison.Ordinal);
            Assert.Contains("context.Message.VariantId", handlerSource, StringComparison.Ordinal);
            Assert.DoesNotContain("context.Message.Price", handlerSource, StringComparison.Ordinal);
            Assert.DoesNotContain("context.Message.OnlinePrice", handlerSource, StringComparison.Ordinal);
            Assert.DoesNotContain("context.Message.NewPrice", handlerSource, StringComparison.Ordinal);
            Assert.DoesNotContain("context.Message.IsActive", handlerSource, StringComparison.Ordinal);
        }

        var trigger = Assert.Single(methods, method =>
            method.Identifier.ValueText == "ConsumeProductVariantTriggerFromSqlAuthorityAsync");
        var triggerSource = trigger.ToFullString();
        var recomposition = Assert.Single(methods, method => method.Identifier.ValueText == recompositionMethod);

        Assert.Contains("if (!tenantId.HasValue)", triggerSource, StringComparison.Ordinal);
        Assert.Contains("if (productId == Guid.Empty)", triggerSource, StringComparison.Ordinal);
        Assert.Contains($"await {recompositionMethod}(tenantId.Value, productId, ct)", triggerSource, StringComparison.Ordinal);
        Assert.Contains(sqlAuthorityMethod, recomposition.ToFullString(), StringComparison.Ordinal);
        Assert.DoesNotContain("EventVersion", recomposition.ToFullString(), StringComparison.Ordinal);
    }

    private static string InfrastructurePath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(
            new[] { solutionRoot, "PointOfSale.Infrastructure" }
                .Concat(parts)
                .ToArray()));
    }

    private static string DiscoverSolutionRoot(string startPath)
    {
        for (var directory = new DirectoryInfo(startPath); directory != null; directory = directory.Parent)
        {
            if (directory.GetFiles("*.sln").Any() || directory.GetDirectories("PointOfSale.Infrastructure").Any())
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
