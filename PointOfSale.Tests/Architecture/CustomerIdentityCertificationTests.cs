namespace PointOfSale.Tests.Architecture;

public sealed class CustomerIdentityCertificationTests
{
    [Fact]
    public void Pass161Registration_MustRemainTransactionalAndRaceSafe()
    {
        var authService = ReadRepoFile("PointOfSale.API/Services/CommerceHub/CommerceHubCustomerAuthService.cs");

        Assert.Contains("BeginTransactionAsync", authService, StringComparison.Ordinal);
        Assert.Contains("EnsureProfileExistsAsync", authService, StringComparison.Ordinal);
        Assert.Contains("CommitAsync", authService, StringComparison.Ordinal);
        Assert.Contains("IsEmailUniquenessViolation", authService, StringComparison.Ordinal);
        Assert.Contains("\"IX_CommerceHubCustomers_Email\"", authService, StringComparison.Ordinal);
        Assert.Contains("return Fail(\"EMAIL_EXISTS\"", authService, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefaultAsync(customer => customer.Email == email", authService, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass161GuestCartTransition_MustPreserveGuestSnapshotUntilCustomerAdoption()
    {
        var cartStore = ReadRepoFile("PointOfSale.CommerceHub/src/store/cart-store.ts");

        Assert.Contains("stores: state.stores", cartStore, StringComparison.Ordinal);
        Assert.Contains("promoCode: state.promoCode", cartStore, StringComparison.Ordinal);
        Assert.Contains("adoptGuestCartOnBootstrap: hasLocalItems", cartStore, StringComparison.Ordinal);
        Assert.Contains("mergeCartStores(localStores, serverStores)", cartStore, StringComparison.Ordinal);
    }

    [Fact]
    public void Pass161ArchitectureRecords_MustFreezeNotReadyVerdict()
    {
        var findings = ReadRepoFile("docs/architecture/customer-identity-certification-findings.md");

        Assert.Contains("PASS 16.1 Customer Identity Certification Findings", findings, StringComparison.Ordinal);
        Assert.Contains("**NOT READY**", findings, StringComparison.Ordinal);
        Assert.Contains("Can customers reliably recover accounts? | Yes, when SMTP delivery and the migration are deployed correctly.", findings, StringComparison.Ordinal);
        Assert.Contains("Is CommerceHub customer identity production-ready? | No.", findings, StringComparison.Ordinal);
        Assert.Contains("PASS 16.1 Customer Identity Certification", ReadRepoFile(".ai-context/PROJECT_CONTEXT.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 16.1 Customer Identity Certification Truth", ReadRepoFile(".ai-context/ARCHITECTURE_TRUTH.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 16.1 CommerceHub customer identity launch remains blocked", ReadRepoFile(".ai-context/KNOWN_RISKS.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 16.1 Customer Identity Certification Freeze", ReadRepoFile(".ai-context/CODEX_RULES.md"), StringComparison.Ordinal);
        Assert.Contains("PASS 16.1 Customer Identity Certification", ReadRepoFile(".ai-context/CROSS_SYSTEM_AUTHORITY_HARDENING.md"), StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath) => CertificationSource.ReadRepoFile(relativePath);
}
