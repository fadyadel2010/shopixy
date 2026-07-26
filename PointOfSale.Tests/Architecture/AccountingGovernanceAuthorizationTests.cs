using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using PointOfSale.API.Controllers;

namespace PointOfSale.Tests.Architecture;

public sealed class AccountingGovernanceAuthorizationTests
{
    [Fact]
    public void ManualFinancialCorrectionEndpoints_MustRequireGovernanceRoles()
    {
        AssertMethodRoles(
            typeof(StatementOfAccountController),
            nameof(StatementOfAccountController.CreateAdjustment),
            "Owner,Admin,Manager");

        AssertMethodRoles(
            typeof(ExpensesController),
            nameof(ExpensesController.ApproveExpense),
            "Owner,Admin,Manager");

        AssertMethodRoles(
            typeof(CashboxController),
            nameof(CashboxController.CloseCashbox),
            "Owner,Admin,Manager");
    }

    [Fact]
    public void CashboxClose_MustRejectNegativeActualCash()
    {
        var source = File.ReadAllText(RepoFile("PointOfSale.API", "Controllers", "CashboxController.cs"));

        Assert.Contains("request.ActualCash < 0m", source, StringComparison.Ordinal);
        Assert.Contains("Actual cash cannot be negative.", source, StringComparison.Ordinal);
    }

    private static void AssertMethodRoles(Type controllerType, string methodName, string expectedRoles)
    {
        var method = controllerType.GetMethod(methodName);
        Assert.NotNull(method);

        var attribute = Assert.Single(method!.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(expectedRoles, attribute.Roles);
    }

    private static string RepoFile(params string[] relativeSegments)
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            Path.Combine(relativeSegments)));
    }
}
