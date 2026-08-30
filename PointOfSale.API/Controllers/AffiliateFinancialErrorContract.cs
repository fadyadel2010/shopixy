using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Exceptions.Affiliate;

namespace PointOfSale.API.Controllers;

internal sealed record AffiliateFinancialError(string Code, string Message, int StatusCode);

internal static class AffiliateFinancialErrorContract
{
    public static AffiliateFinancialError From(Exception exception)
    {
        if (exception is DbUpdateConcurrencyException)
        {
            return new(
                "AFFILIATE_CONCURRENCY_CONFLICT",
                "The affiliate financial state changed concurrently. Refresh and retry.",
                StatusCodes.Status409Conflict);
        }

        var domainCode = ExtractDomainCode(exception.Message);
        if (domainCode is not null)
        {
            if (domainCode.Contains("NOT_FOUND", StringComparison.Ordinal))
            {
                return new(domainCode, "The requested affiliate financial record was not found.", StatusCodes.Status404NotFound);
            }

            if (domainCode.Contains("ALREADY", StringComparison.Ordinal) ||
                domainCode.Contains("DUPLICATE", StringComparison.Ordinal) ||
                domainCode.Contains("IDEMPOT", StringComparison.Ordinal))
            {
                return new(domainCode, "This affiliate financial operation was already applied or conflicts with an earlier request.", StatusCodes.Status409Conflict);
            }

            if (domainCode.Contains("REFUND", StringComparison.Ordinal) ||
                domainCode.Contains("REVERS", StringComparison.Ordinal))
            {
                return new(domainCode, "The commission operation conflicts with authoritative refund or reversal state.", StatusCodes.Status409Conflict);
            }

            if (domainCode.Contains("PAYOUT", StringComparison.Ordinal) ||
                domainCode.Contains("PROVIDER", StringComparison.Ordinal) ||
                domainCode.Contains("EXTERNAL_EVIDENCE", StringComparison.Ordinal) ||
                domainCode.Contains("WALLET", StringComparison.Ordinal))
            {
                return new(domainCode, "The payout operation could not be completed from the current authoritative evidence.", StatusCodes.Status409Conflict);
            }

            return new(domainCode, "The commission operation conflicts with the current authoritative financial state.", StatusCodes.Status409Conflict);
        }

        if (exception is ArgumentException)
        {
            return new(
                "AFFILIATE_VALIDATION_FAILED",
                "The affiliate financial request is invalid.",
                StatusCodes.Status400BadRequest);
        }

        if (exception is AffiliatePayoutAuthorityException)
        {
            return new(
                "AFFILIATE_PAYOUT_AUTHORITY_CONFLICT",
                "The payout is blocked because its store or currency authority cannot be proven.",
                StatusCodes.Status409Conflict);
        }

        if (exception is DbUpdateException)
        {
            return new(
                "AFFILIATE_FINANCIAL_CONFLICT",
                "The affiliate financial operation conflicts with persisted ledger state.",
                StatusCodes.Status409Conflict);
        }

        return new(
            "AFFILIATE_OPERATION_FAILED",
            "The affiliate financial operation could not be completed.",
            StatusCodes.Status500InternalServerError);
    }

    private static string? ExtractDomainCode(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var separator = message.IndexOf(':');
        var candidate = (separator >= 0 ? message[..separator] : message).Trim();
        if (!candidate.StartsWith("AFFILIATE_", StringComparison.Ordinal) || candidate.Length > 96)
        {
            return null;
        }

        return candidate.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')
            ? candidate
            : null;
    }
}
