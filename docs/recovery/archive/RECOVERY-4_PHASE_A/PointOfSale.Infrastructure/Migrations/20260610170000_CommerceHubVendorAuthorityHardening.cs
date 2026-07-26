using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

public partial class CommerceHubVendorAuthorityHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsMarketplaceApproved",
            table: "UserGovernanceProfiles",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTime>(
            name: "KycVerifiedAtUtc",
            table: "UserGovernanceProfiles",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "KycVerifiedByAdminId",
            table: "UserGovernanceProfiles",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "KycVerificationReason",
            table: "UserGovernanceProfiles",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "MarketplaceApprovedAtUtc",
            table: "UserGovernanceProfiles",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "MarketplaceApprovedByAdminId",
            table: "UserGovernanceProfiles",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MarketplaceApprovalReason",
            table: "UserGovernanceProfiles",
            type: "character varying(1000)",
            maxLength: 1000,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_UserGovernanceProfiles_MarketplaceApprovalRequiresKyc",
            table: "UserGovernanceProfiles",
            sql: "NOT \"IsMarketplaceApproved\" OR \"IsKycVerified\"");

        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION prevent_user_governance_profile_ownership_mutation()
            RETURNS trigger AS $$
            BEGIN
                IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'UserGovernanceProfiles are durable authority records and cannot be deleted';
                END IF;

                IF NEW."SubjectId" IS DISTINCT FROM OLD."SubjectId"
                   OR NEW."Role" IS DISTINCT FROM OLD."Role" THEN
                    RAISE EXCEPTION 'UserGovernanceProfile ownership cannot be reassigned';
                END IF;

                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            DROP TRIGGER IF EXISTS trg_user_governance_profile_ownership_immutable
                ON "UserGovernanceProfiles";

            CREATE TRIGGER trg_user_governance_profile_ownership_immutable
            BEFORE UPDATE OR DELETE ON "UserGovernanceProfiles"
            FOR EACH ROW
            EXECUTE FUNCTION prevent_user_governance_profile_ownership_mutation();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DROP TRIGGER IF EXISTS trg_user_governance_profile_ownership_immutable
                ON "UserGovernanceProfiles";
            DROP FUNCTION IF EXISTS prevent_user_governance_profile_ownership_mutation();
            """);

        migrationBuilder.DropCheckConstraint(
            name: "CK_UserGovernanceProfiles_MarketplaceApprovalRequiresKyc",
            table: "UserGovernanceProfiles");

        migrationBuilder.DropColumn(name: "IsMarketplaceApproved", table: "UserGovernanceProfiles");
        migrationBuilder.DropColumn(name: "KycVerifiedAtUtc", table: "UserGovernanceProfiles");
        migrationBuilder.DropColumn(name: "KycVerifiedByAdminId", table: "UserGovernanceProfiles");
        migrationBuilder.DropColumn(name: "KycVerificationReason", table: "UserGovernanceProfiles");
        migrationBuilder.DropColumn(name: "MarketplaceApprovedAtUtc", table: "UserGovernanceProfiles");
        migrationBuilder.DropColumn(name: "MarketplaceApprovedByAdminId", table: "UserGovernanceProfiles");
        migrationBuilder.DropColumn(name: "MarketplaceApprovalReason", table: "UserGovernanceProfiles");
    }
}
