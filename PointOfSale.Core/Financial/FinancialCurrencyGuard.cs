namespace PointOfSale.Core.Financial;

/// <summary>
/// Centralized strict currency validation for financial and analytics flows.
/// Silent fallbacks are forbidden.
/// Uses authoritative ISO 4217 currency codes via Iso4217CurrencyProvider.
/// </summary>
public static class FinancialCurrencyGuard
{
    public const string PlatformBaseCurrencyCode = "EGP";

    public static string RequireCurrency(
        string? currency,
        string component,
        string context,
        bool validateIso = true)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new FinancialCurrencyMissingException(component, context);
        }

        var normalized = currency.Trim().ToUpperInvariant();
        if (validateIso && !Iso4217CurrencyProvider.IsValidCurrency(normalized))
        {
            throw new FinancialCurrencyInvalidException(component, normalized, context);
        }

        return normalized;
    }

    public static string RequireExpectedCurrency(
        string? currency,
        string expectedCurrency,
        string component,
        string context,
        bool validateIso = true)
    {
        var normalizedExpected = RequireCurrency(
            expectedCurrency,
            $"{component}.expected",
            context,
            validateIso);
        var normalizedActual = RequireCurrency(currency, component, context, validateIso);

        if (!string.Equals(normalizedActual, normalizedExpected, StringComparison.Ordinal))
        {
            throw new FinancialCurrencyMismatchException(
                $"{component}.expected",
                normalizedExpected,
                component,
                normalizedActual,
                context);
        }

        return normalizedActual;
    }

    public static string RequireEgpCurrency(
        string? currency,
        string component,
        string context,
        bool validateIso = true) =>
        RequireExpectedCurrency(
            currency,
            PlatformBaseCurrencyCode,
            component,
            context,
            validateIso);

    public static string? NormalizeIfPresent(
        string? currency,
        string component,
        string context,
        bool validateIso = true)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return null;
        }

        return RequireCurrency(currency, component, context, validateIso);
    }

    public static string RequireMatch(
        string leftCurrency,
        string leftComponent,
        string rightCurrency,
        string rightComponent,
        string context)
    {
        var normalizedLeft = RequireCurrency(leftCurrency, leftComponent, context);
        var normalizedRight = RequireCurrency(rightCurrency, rightComponent, context);

        if (!string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal))
        {
            throw new FinancialCurrencyMismatchException(
                leftComponent,
                normalizedLeft,
                rightComponent,
                normalizedRight,
                context);
        }

        return normalizedLeft;
    }

    public static string RequireConsistentCurrency(
        string? tenantCurrency,
        string tenantComponent,
        string? orderCurrency,
        string orderComponent,
        string? paymentCurrency,
        string paymentComponent,
        string context)
    {
        var normalizedTenant = RequireCurrency(tenantCurrency, tenantComponent, context);
        var normalizedOrder = RequireMatch(
            orderCurrency ?? string.Empty,
            orderComponent,
            normalizedTenant,
            tenantComponent,
            context);

        return RequireMatch(
            paymentCurrency ?? string.Empty,
            paymentComponent,
            normalizedOrder,
            orderComponent,
            context);
    }
}

public sealed class FinancialCurrencyMissingException : InvalidOperationException
{
    public FinancialCurrencyMissingException(string component, string context)
        : base($"Currency is required for '{component}' but was missing. Context: {context}")
    {
        Component = component;
        Context = context;
    }

    public string Component { get; }

    public string Context { get; }
}

public sealed class FinancialCurrencyInvalidException : InvalidOperationException
{
    public FinancialCurrencyInvalidException(string component, string currency, string context)
        : base($"Currency '{currency}' is invalid for '{component}'. Context: {context}")
    {
        Component = component;
        Currency = currency;
        Context = context;
    }

    public string Component { get; }

    public string Currency { get; }

    public string Context { get; }
}

public sealed class FinancialCurrencyMismatchException : InvalidOperationException
{
    public FinancialCurrencyMismatchException(
        string expectedComponent,
        string expectedCurrency,
        string actualComponent,
        string actualCurrency,
        string context)
        : base(
            $"Currency mismatch: '{expectedComponent}' uses '{expectedCurrency}' but '{actualComponent}' uses '{actualCurrency}'. Context: {context}")
    {
        ExpectedComponent = expectedComponent;
        ExpectedCurrency = expectedCurrency;
        ActualComponent = actualComponent;
        ActualCurrency = actualCurrency;
        Context = context;
    }

    public string ExpectedComponent { get; }

    public string ExpectedCurrency { get; }

    public string ActualComponent { get; }

    public string ActualCurrency { get; }

    public string Context { get; }
}
