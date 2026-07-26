using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <summary>
    /// Fixes SmartOffer CouponCode check constraints to match entity validation.
    /// 
    /// PROBLEM: The old constraints used TRIM() which only removes spaces, but the entity's
    /// Validate() method uses string.IsNullOrWhiteSpace() which treats tabs, newlines, and
    /// all Unicode whitespace as blank. This allowed coupon codes containing only tabs/newlines
    /// to pass the database check while the entity treats them as blank.
    /// 
    /// SOLUTION: Replace TRIM() with REGEXP_REPLACE to strip all whitespace characters
    /// (spaces, tabs, newlines, Unicode spaces like U+00A0, U+2000-U+200A, U+2028, U+2029, etc.)
    /// before checking if the string is empty, matching the behavior of string.IsNullOrWhiteSpace().
    /// 
    /// UNICODE WHITESPACE COVERAGE:
    /// The pattern matches all characters recognized by .NET Char.IsWhiteSpace():
    /// - ASCII: \s covers U+0009-U+000D (TAB, LF, VT, FF, CR) and U+0020 (SPACE)
    /// - U+0085 (NEL - Next Line)
    /// - U+00A0 (NO-BREAK SPACE)
    /// - U+1680 (OGHAM SPACE MARK)
    /// - U+2000-U+200A (EN QUAD, EM QUAD, EN SPACE, EM SPACE, THREE-PER-EM SPACE, 
    ///   FOUR-PER-EM SPACE, SIX-PER-EM SPACE, FIGURE SPACE, PUNCTUATION SPACE, THIN SPACE, HAIR SPACE)
    /// - U+2028 (LINE SEPARATOR)
    /// - U+2029 (PARAGRAPH SEPARATOR)
    /// - U+202F (NARROW NO-BREAK SPACE)
    /// - U+205F (MEDIUM MATHEMATICAL SPACE)
    /// - U+3000 (IDEOGRAPHIC SPACE)
    /// 
    /// MIGRATION STRATEGY:
    /// 1. Drop old constraints
    /// 2. Add new constraints as NOT VALID (deferred validation)
    /// 3. Clean up legacy rows with whitespace-only coupon codes
    /// 4. Validate constraints after cleanup
    /// This ensures the migration is self-contained and won't fail on legacy data.
    /// </summary>
    public partial class FixSmartOfferCouponCodeWhitespaceConstraints : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop old constraints that use TRIM()
            migrationBuilder.DropCheckConstraint(
                name: "CK_SmartOffers_CouponCode_Presence",
                table: "SmartOffers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SmartOffers_CouponDiscountValue_Presence",
                table: "SmartOffers");

            // Add new constraints as NOT VALID to avoid immediate validation against existing rows
            // This allows us to clean up legacy data before enforcing the constraints
            // 
            // UNICODE WHITESPACE PATTERN:
            // PostgreSQL \s only matches ASCII whitespace (space, tab, newline, CR, FF).
            // To match .NET IsNullOrWhiteSpace() behavior, we need to include all Unicode whitespace:
            // - ASCII: \t \n \v \f \r space (U+0009-U+000D, U+0020)
            // - U+0085 (NEL), U+00A0 (NBSP)
            // - U+1680 (OGHAM SPACE MARK)
            // - U+2000-U+200A (EN QUAD, EM QUAD, EN SPACE, EM SPACE, etc.)
            // - U+2028 (LINE SEPARATOR), U+2029 (PARAGRAPH SEPARATOR)
            // - U+202F (NARROW NBSP), U+205F (MEDIUM MATHEMATICAL SPACE)
            // - U+3000 (IDEOGRAPHIC SPACE)
            migrationBuilder.Sql(@"
                ALTER TABLE ""SmartOffers"" 
                ADD CONSTRAINT ""CK_SmartOffers_CouponCode_Presence"" 
                CHECK (NULLIF(REGEXP_REPLACE(""CouponCode"", '[\s\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]', '', 'g'), '') IS NOT NULL 
                       OR (""CouponDiscountType"" IS NULL AND ""CouponDiscountValue"" IS NULL 
                           AND ""CouponMinOrderAmount"" IS NULL AND ""CouponMaxDiscountAmount"" IS NULL)) 
                NOT VALID;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE ""SmartOffers"" 
                ADD CONSTRAINT ""CK_SmartOffers_CouponDiscountValue_Presence"" 
                CHECK (NULLIF(REGEXP_REPLACE(""CouponCode"", '[\s\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]', '', 'g'), '') IS NULL 
                       OR ""CouponDiscountValue"" IS NOT NULL) 
                NOT VALID;
            ");

            // Clean up legacy rows with whitespace-only coupon codes
            // Set whitespace-only CouponCode to NULL and clear related coupon fields
            // Uses the same Unicode whitespace pattern as the constraints above
            migrationBuilder.Sql(@"
                UPDATE ""SmartOffers""
                SET ""CouponCode"" = NULL,
                    ""CouponDiscountType"" = NULL,
                    ""CouponDiscountValue"" = NULL,
                    ""CouponMinOrderAmount"" = NULL,
                    ""CouponMaxDiscountAmount"" = NULL
                WHERE ""CouponCode"" IS NOT NULL 
                  AND NULLIF(REGEXP_REPLACE(""CouponCode"", '[\s\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]', '', 'g'), '') IS NULL;
            ");

            // Now validate the constraints - all rows should pass after cleanup
            migrationBuilder.Sql(@"
                ALTER TABLE ""SmartOffers"" VALIDATE CONSTRAINT ""CK_SmartOffers_CouponCode_Presence"";
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE ""SmartOffers"" VALIDATE CONSTRAINT ""CK_SmartOffers_CouponDiscountValue_Presence"";
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop new constraints
            migrationBuilder.DropCheckConstraint(
                name: "CK_SmartOffers_CouponCode_Presence",
                table: "SmartOffers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SmartOffers_CouponDiscountValue_Presence",
                table: "SmartOffers");

            // Restore old constraints with TRIM() - add as NOT VALID for safety
            migrationBuilder.Sql(@"
                ALTER TABLE ""SmartOffers"" 
                ADD CONSTRAINT ""CK_SmartOffers_CouponCode_Presence"" 
                CHECK (NULLIF(TRIM(""CouponCode""), '') IS NOT NULL 
                       OR (""CouponDiscountType"" IS NULL AND ""CouponDiscountValue"" IS NULL 
                           AND ""CouponMinOrderAmount"" IS NULL AND ""CouponMaxDiscountAmount"" IS NULL)) 
                NOT VALID;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE ""SmartOffers"" 
                ADD CONSTRAINT ""CK_SmartOffers_CouponDiscountValue_Presence"" 
                CHECK (NULLIF(TRIM(""CouponCode""), '') IS NULL 
                       OR ""CouponDiscountValue"" IS NOT NULL) 
                NOT VALID;
            ");

            // Validate the old constraints
            migrationBuilder.Sql(@"
                ALTER TABLE ""SmartOffers"" VALIDATE CONSTRAINT ""CK_SmartOffers_CouponCode_Presence"";
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE ""SmartOffers"" VALIDATE CONSTRAINT ""CK_SmartOffers_CouponDiscountValue_Presence"";
            ");
        }
    }
}
