using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace PointOfSale.API.Services.CommerceHub.Finance;

internal static class CommerceHubVendorSettlementStoreCompatibility
{
    private const string TableName = "CommerceHubVendorSettlements";
    private const string RequiredMigrationFile = "20260324_ZZN_AddCommerceHubVendorFinancialControl.sql";

    public static bool IsMissingSettlementTable(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is PostgresException postgresException &&
                string.Equals(postgresException.SqlState, PostgresErrorCodes.UndefinedTable, StringComparison.Ordinal) &&
                postgresException.MessageText.Contains(TableName, StringComparison.Ordinal))
            {
                return true;
            }

            if (current is DbException dbException &&
                dbException.Message.Contains(TableName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static CommerceHubVendorFinancialControlException CreateSchemaMissingException()
    {
        return new CommerceHubVendorFinancialControlException(
            "VENDOR_SETTLEMENTS_SCHEMA_MISSING",
            $"Vendor financial control storage is unavailable because '{TableName}' is missing. Apply migration '{RequiredMigrationFile}' and retry.",
            StatusCodes.Status503ServiceUnavailable);
    }
}
