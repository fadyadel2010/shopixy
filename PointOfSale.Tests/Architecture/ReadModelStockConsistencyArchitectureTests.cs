using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace PointOfSale.Tests.Architecture;

public sealed class ReadModelStockConsistencyArchitectureTests
{
    [Fact]
    public void ProductVariantProjectionConsumer_MustRejectDuplicateAndStaleStockEvents()
    {
        var source = File.ReadAllText(InfrastructurePath("Consumers", "ProductVariantProjectionConsumer.cs"));
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        // Assert: There is an equality comparison for duplicate detection: LastEventId == EventId
        var duplicateCheck = root.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .FirstOrDefault(bin =>
                bin.IsKind(SyntaxKind.EqualsExpression) &&
                (
                    (bin.Left.ToString().Contains("LastEventId") && bin.Right.ToString().Contains("EventId") && !bin.Right.ToString().Contains("LastEventId")) ||
                    (bin.Right.ToString().Contains("LastEventId") && bin.Left.ToString().Contains("EventId") && !bin.Left.ToString().Contains("LastEventId"))
                ));

        Assert.NotNull(duplicateCheck);

        Assert.Contains("IsStaleStockProjection", source, StringComparison.Ordinal);
        Assert.Contains("ProjectionAuthorityValidator.IsIncomingStockStale", source, StringComparison.Ordinal);
        Assert.Contains("LastStockUpdatedAtUtc", source, StringComparison.Ordinal);
        Assert.Contains("LastStockEventVersion", source, StringComparison.Ordinal);
        Assert.Contains("BuildFreshVariantStockEventFilter", source, StringComparison.Ordinal);
        Assert.Contains("variant.LastStockUpdatedAtUtc < stockUpdatedAtUtc", source, StringComparison.Ordinal);
        Assert.Contains("variant.LastStockEventVersion <= eventVersion", source, StringComparison.Ordinal);

        // Assert: The stale rejection log is inside a conditional block that checks the staleness condition
        var ifStatements = root.DescendantNodes().OfType<IfStatementSyntax>().ToList();
        var staleLogFoundInRejectionPath = false;

        foreach (var ifStmt in ifStatements)
        {
            if (ifStmt.Condition.ToString().Contains("IsStaleStockProjection", StringComparison.Ordinal))
            {
                var logsInBlock = ifStmt.Statement.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Where(inv =>
                    {
                        var invText = inv.ToString();
                        return invText.Contains("Log") && invText.Contains("stale") && invText.Contains("projection");
                    })
                    .ToList();

                if (logsInBlock.Any())
                {
                    staleLogFoundInRejectionPath = true;
                    break;
                }
            }
        }

        Assert.True(staleLogFoundInRejectionPath, "Stale rejection log must be inside the staleness check conditional block");
    }

    private static bool ContainsIdentifier(ExpressionSyntax expression, string identifierName)
    {
        // Check if the expression itself is an IdentifierNameSyntax with the target identifier
        if (expression is IdentifierNameSyntax identifierName1 && 
            identifierName1.Identifier.ValueText == identifierName)
        {
            return true;
        }

        // Check if the expression is a MemberAccessExpressionSyntax with the target identifier
        if (expression is MemberAccessExpressionSyntax memberAccess && 
            memberAccess.Name.Identifier.ValueText == identifierName)
        {
            return true;
        }

        // Recursively check descendant nodes for the identifier
        return expression.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(id => id.Identifier.ValueText == identifierName);
    }

    private static bool HasStalenessCheck(SyntaxNode condition)
    {
        return condition.DescendantNodesAndSelf()
            .OfType<BinaryExpressionSyntax>()
            .Any(bin => bin.IsKind(SyntaxKind.GreaterThanOrEqualExpression) &&
                ContainsIdentifier(bin.Left, "StockUpdatedAtUtc") && 
                ContainsIdentifier(bin.Right, "StockUpdatedAtUtc"));
    }

    [Fact]
    public void CommerceHubProjectionConsumer_MustRejectStaleStockEvents()
    {
        var source = File.ReadAllText(InfrastructurePath("Consumers", "CommerceHubProjectionConsumer.cs"));
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        // Assert: There is an equality comparison for duplicate detection: LastEventId == EventId
        var duplicateCheck = root.DescendantNodes()
            .OfType<BinaryExpressionSyntax>()
            .FirstOrDefault(bin =>
                bin.IsKind(SyntaxKind.EqualsExpression) &&
                (
                    (bin.Left.ToString().Contains("LastEventId") && bin.Right.ToString().Contains("EventId") && !bin.Right.ToString().Contains("LastEventId")) ||
                    (bin.Right.ToString().Contains("LastEventId") && bin.Left.ToString().Contains("EventId") && !bin.Left.ToString().Contains("LastEventId"))
                ));

        Assert.NotNull(duplicateCheck);

        Assert.Contains("IsStaleStockProjection", source, StringComparison.Ordinal);
        Assert.Contains("ProjectionAuthorityValidator.IsIncomingStockStale", source, StringComparison.Ordinal);
        Assert.Contains("filter.Lt(x => x.StockUpdatedAtUtc, stockUpdatedAtUtc)", source, StringComparison.Ordinal);
        Assert.Contains("filter.Lte(x => x.LastStockEventVersion, eventVersion)", source, StringComparison.Ordinal);

        // Assert: The stale rejection log is inside a conditional block that checks the staleness condition
        var ifStatements = root.DescendantNodes().OfType<IfStatementSyntax>().ToList();
        var staleLogFoundInRejectionPath = false;

        foreach (var ifStmt in ifStatements)
        {
            if (ifStmt.Condition.ToString().Contains("IsStaleStockProjection", StringComparison.Ordinal))
            {
                var logsInBlock = ifStmt.Statement.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Where(inv =>
                    {
                        var invText = inv.ToString();
                        return invText.Contains("Log") && invText.Contains("stale") && invText.Contains("CommerceHub");
                    })
                    .ToList();

                if (logsInBlock.Any())
                {
                    staleLogFoundInRejectionPath = true;
                    break;
                }
            }
        }

        Assert.True(staleLogFoundInRejectionPath, "Stale rejection log must be inside the staleness check conditional block");
    }

    [Fact]
    public void ProductVariantReadRepository_MustFallBackToSqlWhenMongoIsMissingOrUnavailable()
    {
        var source = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "ProductVariantReadRepository.cs"));
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        // Find the GetByVariantIdAsync method
        var getByVariantIdMethod = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == "GetByVariantIdAsync" && m.Body is not null);

        Assert.NotNull(getByVariantIdMethod);

        // Assert: LoadVariantFromSqlAsync is called after the try-catch block
        var tryStatements = getByVariantIdMethod.DescendantNodes().OfType<TryStatementSyntax>().ToList();
        Assert.NotEmpty(tryStatements);

        var tryStatement = tryStatements.First();
        var statementsAfterTry = getByVariantIdMethod.Body!.Statements
            .SkipWhile(s => s != tryStatement)
            .Skip(1)
            .ToList();

        var loadVariantFromSqlAfterTry = statementsAfterTry
            .SelectMany(s => s.DescendantNodesAndSelf())
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => inv.ToString().Contains("LoadVariantFromSqlAsync"));

        Assert.True(loadVariantFromSqlAfterTry, "LoadVariantFromSqlAsync must be called after the try-catch block in GetByVariantIdAsync");

        // Assert: Fallback log exists in the catch block
        var catchClauses = tryStatement.Catches;
        var fallbackLogInCatch = catchClauses
            .SelectMany(c => c.Block.DescendantNodes())
            .OfType<InvocationExpressionSyntax>()
            .Any(inv =>
            {
                var invText = inv.ToString();
                return invText.Contains("Log") && invText.Contains("falling back") && invText.Contains("SQL");
            });

        Assert.True(fallbackLogInCatch, "Fallback log must be in the catch block of GetByVariantIdAsync");

        // Find the GetByVariantIdsAsync method
        var getByVariantIdsMethod = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == "GetByVariantIdsAsync" && m.Body is not null);

        Assert.NotNull(getByVariantIdsMethod);

        // Assert: LoadVariantsFromSqlAsync is called in the missing-variants conditional
        var ifStatements = getByVariantIdsMethod.DescendantNodes().OfType<IfStatementSyntax>().ToList();
        var missingVariantsIf = ifStatements.FirstOrDefault(ifStmt =>
            ifStmt.Condition.ToString().Contains("missingVariantIds"));

        Assert.NotNull(missingVariantsIf);

        var loadVariantsInMissingCheck = missingVariantsIf.Statement
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => inv.ToString().Contains("LoadVariantsFromSqlAsync"));

        Assert.True(loadVariantsInMissingCheck, "LoadVariantsFromSqlAsync must be called inside the missing-variants conditional in GetByVariantIdsAsync");

        // Assert: LoadVariantsFromSqlAsync is also called after the try-catch block
        var tryStatementsInBulk = getByVariantIdsMethod.DescendantNodes().OfType<TryStatementSyntax>().ToList();
        Assert.NotEmpty(tryStatementsInBulk);

        var tryStatementInBulk = tryStatementsInBulk.First();
        var statementsAfterTryInBulk = getByVariantIdsMethod.Body!.Statements
            .SkipWhile(s => s != tryStatementInBulk)
            .Skip(1)
            .ToList();

        var loadVariantsFromSqlAfterTry = statementsAfterTryInBulk
            .SelectMany(s => s.DescendantNodesAndSelf())
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => inv.ToString().Contains("LoadVariantsFromSqlAsync"));

        Assert.True(loadVariantsFromSqlAfterTry, "LoadVariantsFromSqlAsync must be called after the try-catch block in GetByVariantIdsAsync");

        // Assert: Fallback logs exist in the catch block and missing-variants conditional
        var catchClausesInBulk = tryStatementInBulk.Catches;
        var fallbackLogInCatchBulk = catchClausesInBulk
            .SelectMany(c => c.Block.DescendantNodes())
            .OfType<InvocationExpressionSyntax>()
            .Any(inv =>
            {
                var invText = inv.ToString();
                return invText.Contains("Log") && invText.Contains("falling back") && invText.Contains("SQL");
            });

        Assert.True(fallbackLogInCatchBulk, "Fallback log must be in the catch block of GetByVariantIdsAsync");

        var fallbackLogInMissingCheck = missingVariantsIf.Statement
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv =>
            {
                var invText = inv.ToString();
                return invText.Contains("Log") && (invText.Contains("missing") || invText.Contains("partial"));
            });

        Assert.True(fallbackLogInMissingCheck, "Missing variants log must be in the missing-variants conditional in GetByVariantIdsAsync");
        Assert.Contains(".WherePubliclyVisibleLineage(tenantId)", source, StringComparison.Ordinal);
        Assert.Contains(".WhereCurrentInventoryStockOwner(tenantId)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MarketplaceReadModels_MustUseMonotonicEntityMutationVersion_NotTimestampOnly()
    {
        var productRepository = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "Marketplace", "MarketplaceProductReadRepository.cs"));
        var orderRepository = File.ReadAllText(InfrastructurePath("MongoDB", "Services", "Marketplace", "MarketplaceOrderReadRepository.cs"));
        var projectionService = File.ReadAllText(InfrastructurePath("ReadModels", "Marketplace", "MarketplaceReadModelProjectionServices.cs"));
        var dbContext = File.ReadAllText(InfrastructurePath("Data", "ApplicationDbContext.cs"));
        var migration = File.ReadAllText(InfrastructurePath("Migrations", "20260424160000_S30_MarketplaceProjectionMutationVersions.sql"));

        Assert.Contains("LastEntityMutationVersion", productRepository, StringComparison.Ordinal);
        Assert.Contains("incomingEntityMutationVersion", productRepository, StringComparison.Ordinal);
        Assert.Contains("filter.Lt(x => x.LastEntityMutationVersion, incomingEntityMutationVersion)", productRepository, StringComparison.Ordinal);
        Assert.Contains("filter.Eq(x => x.LastEntityMutationVersion, incomingEntityMutationVersion)", productRepository, StringComparison.Ordinal);

        Assert.Contains("LastEntityMutationVersion", orderRepository, StringComparison.Ordinal);
        Assert.Contains("incomingEntityMutationVersion", orderRepository, StringComparison.Ordinal);
        Assert.Contains("Builders<MarketplaceOrderReadModelDocument>.Filter.Lt(x => x.LastEntityMutationVersion, incomingEntityMutationVersion)", orderRepository, StringComparison.Ordinal);
        Assert.Contains("Builders<MarketplaceOrderReadModelDocument>.Filter.Eq(x => x.LastEntityMutationVersion, incomingEntityMutationVersion)", orderRepository, StringComparison.Ordinal);

        Assert.Contains("ResolveProductProjectionMutationVersion", projectionService, StringComparison.Ordinal);
        Assert.Contains("product.ProjectionMutationVersion", projectionService, StringComparison.Ordinal);
        Assert.Contains("variant.ProjectionMutationVersion", projectionService, StringComparison.Ordinal);
        Assert.Contains("stock.ProjectionMutationVersion", projectionService, StringComparison.Ordinal);
        Assert.Contains("order.ProjectionMutationVersion", projectionService, StringComparison.Ordinal);

        Assert.Contains("IncrementMarketplaceProjectionMutationVersions", dbContext, StringComparison.Ordinal);
        Assert.Contains("ChangeTracker.Entries<Product>()", dbContext, StringComparison.Ordinal);
        Assert.Contains("ChangeTracker.Entries<ProductVariant>()", dbContext, StringComparison.Ordinal);
        Assert.Contains("ChangeTracker.Entries<ProductVariantStock>()", dbContext, StringComparison.Ordinal);
        Assert.Contains("ChangeTracker.Entries<Order>()", dbContext, StringComparison.Ordinal);

        Assert.Contains("ProjectionMutationVersion", migration, StringComparison.Ordinal);
        Assert.Contains("Products", migration, StringComparison.Ordinal);
        Assert.Contains("ProductVariants", migration, StringComparison.Ordinal);
        Assert.Contains("ProductVariantStocks", migration, StringComparison.Ordinal);
        Assert.Contains("Orders", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyWarehouseStock_MustNotBeUsedAsOperationalInventoryAuthority()
    {
        var dashboardController = File.ReadAllText(ApiPath("Controllers", "DashboardController.cs"));
        var productsController = File.ReadAllText(ApiPath("Controllers", "ProductsController.cs"));
        var safeResponseMapper = File.ReadAllText(ApiPath("Mappers", "SafeResponseMapper.cs"));
        var marketplaceProjectionService = File.ReadAllText(InfrastructurePath("ReadModels", "Marketplace", "MarketplaceReadModelProjectionServices.cs"));
        var stockProjectionConsumer = File.ReadAllText(InfrastructurePath("Consumers", "ProductStockChangedConsumer.cs"));

        Assert.DoesNotContain("_context.WarehouseStocks", dashboardController, StringComparison.Ordinal);
        Assert.DoesNotContain("_context.WarehouseStocks", productsController, StringComparison.Ordinal);
        Assert.DoesNotContain(".Include(p => p.WarehouseStocks)", productsController, StringComparison.Ordinal);
        Assert.DoesNotContain("p.WarehouseStocks", productsController, StringComparison.Ordinal);

        Assert.DoesNotContain("product.WarehouseStocks", safeResponseMapper, StringComparison.Ordinal);
        Assert.DoesNotContain("ToProductWarehouseStockResponseDto(WarehouseStock", safeResponseMapper, StringComparison.Ordinal);

        Assert.DoesNotContain("WarehouseStocks", marketplaceProjectionService, StringComparison.Ordinal);
        Assert.Contains("ProductVariantStocks", stockProjectionConsumer, StringComparison.Ordinal);
        Assert.DoesNotContain("PostgreSQL WarehouseStocks", stockProjectionConsumer, StringComparison.Ordinal);
    }

    private static string ApiPath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(
            new[] { solutionRoot, "PointOfSale.API" }
                .Concat(parts)
                .ToArray()));
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
        var currentDir = new DirectoryInfo(startPath);

        while (currentDir != null)
        {
            if (currentDir.GetFiles("*.sln").Any() ||
                currentDir.GetDirectories("PointOfSale.Infrastructure").Any())
            {
                return currentDir.FullName;
            }

            currentDir = currentDir.Parent;
        }

        throw new InvalidOperationException($"Could not discover solution root from '{startPath}'.");
    }
}
