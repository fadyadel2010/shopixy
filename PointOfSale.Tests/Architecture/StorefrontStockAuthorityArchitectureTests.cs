using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PointOfSale.Tests.Architecture;

public sealed class StorefrontStockAuthorityArchitectureTests
{
    [Fact]
    public void CartService_MustCrossCheckProjectedStockAgainstSqlAuthority()
    {
        var source = File.ReadAllText(ApiPath("Services", "Cart", "CartService.cs"));
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var methodNames = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Select(m => m.Identifier.Text)
            .ToHashSet();

        Assert.Contains("GetAuthoritativeVariantStockSnapshotAsync", methodNames);
        Assert.Contains("GetAuthoritativeVariantStockSnapshotsAsync", methodNames);
        Assert.Contains("GetStockDivergenceReason", methodNames);
        Assert.Contains("ResolveAuthoritativeAvailableStock", methodNames);
        Assert.Contains(".WhereCurrentInventoryStockOwner(tenantId)", source, StringComparison.Ordinal);

        var telemetryInvocation = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(inv => inv.Expression.ToString() == "_reliabilityTelemetry.RecordStockDivergenceDetected");

        Assert.True(
            telemetryInvocation,
            "CartService must record telemetry when projected Mongo stock diverges from the SQL stock authority.");
    }

    [Fact]
    public void CheckoutService_MustReserveAndReleaseInventoryThroughSqlStockRepository()
    {
        var source = File.ReadAllText(ApiPath("Services", "Checkout", "CheckoutService.cs"));
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        // Assert: CheckoutService has a field _variantStockRepository of type IProductVariantStockRepository
        var fields = root.DescendantNodes()
            .OfType<FieldDeclarationSyntax>()
            .ToList();

        var variantStockField = fields
            .FirstOrDefault(f =>
                f.Declaration.Variables.Any(v => v.Identifier.Text == "_variantStockRepository") &&
                f.Declaration.Type.ToString().Contains("IProductVariantStockRepository"));

        Assert.NotNull(variantStockField);

        // Assert: CheckoutService has a method ValidateAndReserveInventoryAsync
        var methods = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .ToList();

        var validateMethod = methods.FirstOrDefault(m => m.Identifier.Text == "ValidateAndReserveInventoryAsync");
        Assert.NotNull(validateMethod);

        // Assert: There are invocations to _variantStockRepository.ReserveAsync
        var reserveAsyncInvocations = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(inv =>
            {
                if (inv.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    return memberAccess.Expression.ToString() == "_variantStockRepository" &&
                           memberAccess.Name.Identifier.Text == "ReserveAsync";
                }
                return false;
            })
            .ToList();

        Assert.NotEmpty(reserveAsyncInvocations);

        // Assert: There are invocations to _variantStockRepository.ReleaseOrderReservationsAsync
        var releaseInvocations = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(inv =>
            {
                if (inv.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    return memberAccess.Expression.ToString() == "_variantStockRepository" &&
                           memberAccess.Name.Identifier.Text == "ReleaseOrderReservationsAsync";
                }
                return false;
            })
            .ToList();

        Assert.NotEmpty(releaseInvocations);

        // Assert: There are no ObjectCreationExpression for ProductVariantStockChangedEvent
        var stockChangedEventCreations = root.DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Where(obj => obj.Type.ToString().Contains("ProductVariantStockChangedEvent"))
            .ToList();

        Assert.Empty(stockChangedEventCreations);
    }

    [Fact]
    public void CheckoutService_MustReleasePartialReservations_WhenReservationLoopFails()
    {
        var source = File.ReadAllText(ApiPath("Services", "Checkout", "CheckoutService.cs"));
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var validateMethod = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.Text == "ValidateAndReserveInventoryAsync");

        Assert.NotNull(validateMethod);

        // Assert: There is an assignment setting reservationMutationAttempted = true
        var mutationMarkerAssignment = validateMethod.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .FirstOrDefault(a =>
                a.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                a.Left is IdentifierNameSyntax left &&
                left.Identifier.Text == "reservationMutationAttempted" &&
                a.Right is LiteralExpressionSyntax right &&
                right.IsKind(SyntaxKind.TrueLiteralExpression));

        Assert.NotNull(mutationMarkerAssignment);

        // Assert: There is an invocation to ReserveAsync
        var reserveAsyncInvocation = validateMethod.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(inv =>
                inv.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Expression.ToString() == "_variantStockRepository" &&
                memberAccess.Name.Identifier.Text == "ReserveAsync");

        Assert.NotNull(reserveAsyncInvocation);

        // Assert: The mutation marker assignment appears before the ReserveAsync invocation in the syntax tree
        var mutationMarkerPosition = mutationMarkerAssignment.SpanStart;
        var reserveAsyncPosition = reserveAsyncInvocation.SpanStart;

        Assert.True(
            mutationMarkerPosition < reserveAsyncPosition,
            "ValidateAndReserveInventoryAsync must set reservationMutationAttempted = true before calling ReserveAsync.");

        // Assert: There is a catch clause with a filter checking reservationMutationAttempted
        var compensationCatch = validateMethod.DescendantNodes()
            .OfType<CatchClauseSyntax>()
            .FirstOrDefault(c =>
                c.Declaration?.Type.ToString() == "Exception" &&
                c.Filter is CatchFilterClauseSyntax filter &&
                filter.FilterExpression is IdentifierNameSyntax filterExpr &&
                filterExpr.Identifier.Text == "reservationMutationAttempted");

        Assert.NotNull(compensationCatch);

        // Assert: The catch block contains an invocation to ReleaseInventoryReservationAsync
        var releaseInvocation = compensationCatch.Block.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(inv =>
                (inv.Expression is MemberAccessExpressionSyntax memberAccess &&
                 memberAccess.Name.Identifier.Text == "ReleaseInventoryReservationAsync") ||
                (inv.Expression is IdentifierNameSyntax identifierName &&
                 identifierName.Identifier.Text == "ReleaseInventoryReservationAsync"));

        Assert.NotNull(releaseInvocation);

        // Assert: The catch block contains a throw statement (rethrow)
        var throwStatement = compensationCatch.Block.DescendantNodes()
            .OfType<ThrowStatementSyntax>()
            .FirstOrDefault(t => t.Expression == null); // Rethrow has no expression

        Assert.NotNull(throwStatement);

        // Assert: The release invocation uses CancellationToken.None
        var releaseArguments = releaseInvocation.ArgumentList.Arguments;
        var hasCancellationTokenNone = releaseArguments.Any(arg =>
            arg.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression.ToString() == "CancellationToken" &&
            memberAccess.Name.Identifier.Text == "None");

        Assert.True(hasCancellationTokenNone);

        // Assert: The release invocation includes "InventoryReservationFailed" reason
        var hasFailureReason = releaseArguments.Any(arg =>
            arg.Expression is LiteralExpressionSyntax literal &&
            literal.Token.ValueText == "InventoryReservationFailed");

        Assert.True(hasFailureReason);
    }

    [Fact]
    public void CommerceHubCheckoutService_MustReserveInventoryInsideSerializableTransactionAndRollbackBeforeCleanup()
    {
        var source = File.ReadAllText(ApiPath("Services", "CommerceHub", "CommerceHubCheckoutService.cs"));
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        var checkoutMethod = root.DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(method => method.Identifier.Text == "CheckoutAsync" && method.Body != null);

        Assert.NotNull(checkoutMethod);

        var beginTransactionInvocation = checkoutMethod.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(invocation =>
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "BeginTransactionAsync");

        Assert.NotNull(beginTransactionInvocation);

        Assert.Contains(
            beginTransactionInvocation.ArgumentList.Arguments,
            argument => argument.Expression.ToString() == "IsolationLevel.Serializable");

        var reserveInvocation = checkoutMethod.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(invocation =>
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Expression.ToString() == "_inventoryConsistencyService" &&
                memberAccess.Name.Identifier.Text == "ReserveOrderItemAsync");

        Assert.NotNull(reserveInvocation);

        var commitInvocation = checkoutMethod.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(invocation =>
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "CommitAsync");

        Assert.NotNull(commitInvocation);
        Assert.True(
            reserveInvocation.SpanStart < commitInvocation.SpanStart,
            "CommerceHub checkout must reserve inventory before committing the marketplace order transaction.");

        var catchClause = checkoutMethod.DescendantNodes()
            .OfType<CatchClauseSyntax>()
            .FirstOrDefault(catchClause =>
                catchClause.Block.DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Any(invocation =>
                        invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                        memberAccess.Name.Identifier.Text == "RollbackAsync"));

        Assert.NotNull(catchClause);

        var rollbackInvocation = catchClause.Block.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .First(invocation =>
                invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "RollbackAsync");

        var cleanupInvocation = catchClause.Block.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(invocation =>
                invocation.Expression is IdentifierNameSyntax identifier &&
                identifier.Identifier.Text == "CleanupAfterCheckoutFailureAsync");

        Assert.NotNull(cleanupInvocation);
        Assert.True(
            rollbackInvocation.SpanStart < cleanupInvocation.SpanStart,
            "CommerceHub checkout failure cleanup must run only after the transaction rollback completes.");

        Assert.Contains(
            rollbackInvocation.ArgumentList.Arguments,
            argument => argument.Expression.ToString() == "CancellationToken.None");
        Assert.Contains(
            cleanupInvocation.ArgumentList.Arguments,
            argument => argument.Expression.ToString() == "CancellationToken.None");
    }

    private static string ApiPath(params string[] parts)
    {
        var solutionRoot = DiscoverSolutionRoot(AppContext.BaseDirectory);
        return Path.GetFullPath(Path.Combine(
            new[] { solutionRoot, "PointOfSale.API" }
                .Concat(parts)
                .ToArray()));
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
}
