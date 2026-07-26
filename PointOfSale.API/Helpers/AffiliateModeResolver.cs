namespace PointOfSale.API.Helpers;

/// <summary>
/// AF-12 + AF-15: Affiliate Mode Resolver.
///
/// Provides request-scoped detection of Affiliate Mode.
/// Affiliate Mode is activated when AffiliateReferralMiddleware validates a ?ref=CODE parameter.
///
/// AF-15 ADDITIONS:
/// - PromotionLinkId: set when v2 PromotionLink flow is active
/// - AffiliateUserId: global affiliate identity from the promotion link
/// - StoreId (TenantId): the store context of the promotion link
///
/// DUAL COMPATIBILITY:
/// - If PromotionLinkId exists → v2 flow (new)
/// - Else if AffiliateAccountId exists → v1 flow (legacy)
///
/// RULES:
/// - Affiliate Mode is request-scoped only (NOT persisted)
/// - Activated by middleware, never by frontend
/// - When active, only IsAffiliateEnabled=true products are visible
/// - Checkout flow is unchanged
/// - No cookies, no database storage
///
/// SECURITY:
/// - Frontend cannot activate Affiliate Mode
/// - No query param like ?affiliate=true
/// - Server-side enforcement only
/// </summary>
public static class AffiliateModeResolver
{
    private const string AffiliateModeKey = "AffiliateMode";
    private const string AffiliateAccountIdKey = "AffiliateAccountId";
    private const string AffiliateReferralCodeKey = "AffiliateReferralCode"; // AF-27E
    private const string AffiliateDisplayNameKey = "AffiliateDisplayName";
    
    // AF-15: New keys for v2 flow
    private const string PromotionLinkIdKey = "AF15_PromotionLinkId";
    private const string AffiliateUserIdKey = "AF15_AffiliateUserId";
    private const string StoreIdKey = "AF15_StoreId";

    /// <summary>
    /// Checks if the current request is in Affiliate Mode.
    /// Returns true if AffiliateReferralMiddleware set the flag.
    /// </summary>
    public static bool IsAffiliateMode(HttpContext context)
    {
        return context.Items.ContainsKey(AffiliateModeKey) &&
               context.Items[AffiliateModeKey] is true;
    }

    /// <summary>
    /// Gets the AffiliateAccountId for the current request if in Affiliate Mode.
    /// Returns null if not in Affiliate Mode.
    /// </summary>
    public static Guid? GetAffiliateAccountId(HttpContext context)
    {
        if (!IsAffiliateMode(context))
            return null;

        if (context.Items.TryGetValue(AffiliateAccountIdKey, out var value) && value is Guid accountId)
            return accountId;

        return null;
    }
    /// <summary>
    /// AF-27E: Gets the ReferralCode for the current request if in Affiliate Mode.
    /// Returns null if not in Affiliate Mode.
    /// </summary>
    public static string? GetReferralCode(HttpContext context)
    {
        if (!IsAffiliateMode(context))
            return null;

        if (context.Items.TryGetValue(AffiliateReferralCodeKey, out var value) && value is string code)
            return code;

        return null;
    }

    public static string? GetDisplayName(HttpContext context)
    {
        if (!IsAffiliateMode(context))
            return null;

        if (context.Items.TryGetValue(AffiliateDisplayNameKey, out var value) && value is string displayName)
            return displayName;

        return null;
    }

    // ═══════════════════════════════════════════════════════════════════
    // AF-15: V2 Promotion Link Accessors
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// AF-15: Gets the PromotionLinkId if the current request used a v2 promotion link.
    /// Returns null for v1 flow or non-affiliate requests.
    /// </summary>
    public static Guid? GetPromotionLinkId(HttpContext context)
    {
        if (!IsAffiliateMode(context))
            return null;

        if (context.Items.TryGetValue(PromotionLinkIdKey, out var value) && value is Guid linkId)
            return linkId;

        return null;
    }

    /// <summary>
    /// AF-15: Gets the global AffiliateUserId from the promotion link.
    /// Returns null for v1 flow or non-affiliate requests.
    /// </summary>
    public static Guid? GetAffiliateUserId(HttpContext context)
    {
        if (!IsAffiliateMode(context))
            return null;

        if (context.Items.TryGetValue(AffiliateUserIdKey, out var value) && value is Guid userId)
            return userId;

        return null;
    }

    /// <summary>
    /// AF-15: Checks if the current request is using the v2 (PromotionLink) flow.
    /// </summary>
    public static bool IsV2Flow(HttpContext context)
    {
        return GetPromotionLinkId(context).HasValue;
    }

    // ═══════════════════════════════════════════════════════════════════
    // SETTERS — internal use only (called by middleware)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sets Affiliate Mode for the current request (v1 legacy flow).
    /// INTERNAL USE ONLY - Called by AffiliateReferralMiddleware.
    /// </summary>
    internal static void SetAffiliateMode(
        HttpContext context,
        Guid affiliateAccountId,
        string referralCode,
        string? displayName = null)
    {
        context.Items[AffiliateModeKey] = true;
        context.Items[AffiliateAccountIdKey] = affiliateAccountId;
        context.Items[AffiliateReferralCodeKey] = referralCode; // AF-27E

        if (!string.IsNullOrWhiteSpace(displayName))
            context.Items[AffiliateDisplayNameKey] = displayName;
    }

    /// <summary>
    /// AF-15: Sets Affiliate Mode for the current request (v2 promotion link flow).
    /// INTERNAL USE ONLY - Called by AffiliateReferralMiddleware.
    ///
    /// Sets BOTH v1 (AffiliateAccountId) and v2 (PromotionLinkId, AffiliateUserId) keys
    /// for full dual compatibility. Downstream code that reads only AffiliateAccountId
    /// continues to work unchanged.
    /// </summary>
    internal static void SetAffiliateModeV2(
        HttpContext context,
        Guid affiliateAccountId,
        string referralCode,
        Guid promotionLinkId,
        Guid affiliateUserId,
        Guid storeId,
        string? displayName = null)
    {
        // V1 compatibility: always set AffiliateAccountId
        context.Items[AffiliateModeKey] = true;
        context.Items[AffiliateAccountIdKey] = affiliateAccountId;
        context.Items[AffiliateReferralCodeKey] = referralCode; // AF-27E

        if (!string.IsNullOrWhiteSpace(displayName))
            context.Items[AffiliateDisplayNameKey] = displayName;

        // V2 additions
        context.Items[PromotionLinkIdKey] = promotionLinkId;
        context.Items[AffiliateUserIdKey] = affiliateUserId;
        context.Items[StoreIdKey] = storeId;
    }
}
