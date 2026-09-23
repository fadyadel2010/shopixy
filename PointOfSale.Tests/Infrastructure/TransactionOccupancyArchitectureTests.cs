namespace PointOfSale.Tests.Infrastructure;

public sealed class TransactionOccupancyArchitectureTests
{
    [Fact]
    public void StripeRecovery_CommitsClaimBeforeProviderAndRevalidatesDuringFinalization()
    {
        var source = File.ReadAllText(Repo("PointOfSale.API", "Services", "Payments", "Stripe", "StripePaymentService.cs"));
        var pending = source.IndexOf("if (existingIntent != null && existingIntent.Status == PaymentIntentStatus.Pending)", StringComparison.Ordinal);
        var commit = source.IndexOf("await transaction.CommitAsync(cancellationToken);", pending, StringComparison.Ordinal);
        var recovery = source.IndexOf("var recovered = await RehydrateStripeProviderIntentAsync", pending, StringComparison.Ordinal);
        Assert.True(pending >= 0 && commit > pending && recovery > commit);

        var finalize = source.IndexOf("private async Task<StripePaymentIntentResponseDto> PersistStripeProviderIntentAsync", StringComparison.Ordinal);
        var finalLock = source.IndexOf("OrderLockingHelper.LoadOrderWithLockAsync", finalize, StringComparison.Ordinal);
        var finalIntentRead = source.IndexOf("_dbContext.PaymentIntents", finalLock, StringComparison.Ordinal);
        var mismatch = source.IndexOf("PAYMENT_INTENT_RECOVERY_FAILED", finalIntentRead, StringComparison.Ordinal);
        var finalCommit = source.IndexOf("await transaction.CommitAsync(cancellationToken);", mismatch, StringComparison.Ordinal);
        Assert.True(finalize >= 0 && finalLock > finalize && finalIntentRead > finalLock && mismatch > finalIntentRead && finalCommit > mismatch);
        Assert.Contains("stripe:intent:{tenantId:N}:{order.Id:N}", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderCreationBoundaries_DoNotCallProviderBeforeClaimCommit()
    {
        AssertDisposedClaimBeforeProvider(
            Repo("PointOfSale.API", "Services", "Payments", "Stripe", "StripePaymentService.cs"),
            "await transaction.CommitAsync(cancellationToken);",
            "stripePaymentIntent = await CreateStripeProviderIntentAsync");
        AssertDisposedClaimBeforeProvider(
            Repo("PointOfSale.API", "Services", "Payments", "Paymob", "PaymobPaymentService.cs"),
            "await transaction.CommitAsync(cancellationToken);",
            "await CreatePaymobProviderIntentAsync");
        AssertCommitBeforeProvider(
            Repo("PointOfSale.API", "Services", "Payments", "PaymentService.cs"),
            "await checkpointTransaction.CommitAsync(cancellationToken);",
            "await InitiateFawryPersistedPaymentIntentAsync");

        AssertDisposedClaimBeforeProvider(
            Repo("PointOfSale.API", "Services", "Payments", "PaymentService.cs"),
            "await transaction.CommitAsync(cancellationToken);",
            "var finalizedResponse = await EnsurePersistedPaymentIntentReadyAsync");
    }

    [Fact]
    public void ProviderFinalizationRetries_DisposeRolledBackTransactionBeforeBackoff()
    {
        AssertDisposeBeforeRetryDelay(
            Repo("PointOfSale.API", "Services", "Payments", "Stripe", "StripePaymentService.cs"));
        AssertDisposeBeforeRetryDelay(
            Repo("PointOfSale.API", "Services", "Payments", "Paymob", "PaymobPaymentService.cs"));
    }

    [Fact]
    public void PostCommitRemoteWork_ReleasesRegistrationTransactionsFirst()
    {
        AssertOrdered(
            Repo("PointOfSale.API", "Controllers", "AffiliateAuthController.cs"),
            "await registrationTransaction.CommitAsync(ct);",
            "await registrationTransaction.DisposeAsync();",
            "await SendVerificationEmailAsync(affiliateUser, rawVerificationToken);");
        AssertOrdered(
            Repo("PointOfSale.API", "Controllers", "AffiliateAuthController.cs"),
            "public async Task<IActionResult> ResendVerification",
            "await transaction.CommitAsync(ct);",
            "await transaction.DisposeAsync();",
            "await SendVerificationEmailAsync(affiliateUser, rawToken);");
        AssertOrdered(
            Repo("PointOfSale.API", "Services", "Platform", "TenantRegistrationService.cs"),
            "await transaction.CommitAsync(cancellationToken);",
            "await transaction.DisposeAsync();",
            "await _emailVerificationService.CreateVerificationTokenAsync(user, cancellationToken);");
    }

    [Fact]
    public void PaymobConcurrentClaim_EndsAbortedTransactionBeforeRecoveryRead()
    {
        AssertOrdered(
            Repo("PointOfSale.API", "Services", "Payments", "Paymob", "PaymobPaymentService.cs"),
            "catch (DbUpdateException ex) when (IsUniqueViolation(ex))",
            "await transaction.RollbackAsync(cancellationToken);",
            "await using var recoveryTransaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);",
            "var claimedIntent = await _dbContext.PaymentIntents");
    }

    private static void AssertCommitBeforeProvider(string file, string commitMarker, string providerMarker)
    {
        var source = File.ReadAllText(file);
        var provider = source.IndexOf(providerMarker, StringComparison.Ordinal);
        Assert.True(provider > 0, $"Provider marker missing in {file}");
        var commit = source.LastIndexOf(commitMarker, provider, StringComparison.Ordinal);
        Assert.True(commit >= 0 && commit < provider, $"Provider call precedes claim commit in {file}");
    }

    private static void AssertDisposedClaimBeforeProvider(string file, string commitMarker, string providerMarker)
    {
        var source = File.ReadAllText(file);
        var provider = source.IndexOf(providerMarker, StringComparison.Ordinal);
        Assert.True(provider > 0, $"Provider marker missing in {file}");
        var commit = source.LastIndexOf(commitMarker, provider, StringComparison.Ordinal);
        var dispose = source.LastIndexOf("await transaction.DisposeAsync();", provider, StringComparison.Ordinal);
        Assert.True(commit >= 0 && dispose > commit && dispose < provider,
            $"Provider call retains its claim transaction in {file}");
    }

    private static void AssertDisposeBeforeRetryDelay(string file)
    {
        var source = File.ReadAllText(file);
        var delay = source.IndexOf("await Task.Delay(100 * (int)Math.Pow(2, attempt - 1), cancellationToken);", StringComparison.Ordinal);
        Assert.True(delay > 0, $"Retry delay marker missing in {file}");
        var rollback = source.LastIndexOf("await transaction.RollbackAsync(cancellationToken);", delay, StringComparison.Ordinal);
        var dispose = source.LastIndexOf("await transaction.DisposeAsync();", delay, StringComparison.Ordinal);
        Assert.True(rollback >= 0 && dispose > rollback && dispose < delay,
            $"Retry backoff retains its rolled-back transaction in {file}");
    }

    private static void AssertOrdered(string file, params string[] markers)
    {
        var source = File.ReadAllText(file);
        var position = -1;
        foreach (var marker in markers)
        {
            position = source.IndexOf(marker, position + 1, StringComparison.Ordinal);
            Assert.True(position >= 0, $"Missing or out-of-order marker '{marker}' in {file}");
        }
    }

    private static string Repo(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "PointOfSale.sln")))
            current = current.Parent;
        Assert.NotNull(current);
        return Path.Combine([current!.FullName, .. segments]);
    }
}
