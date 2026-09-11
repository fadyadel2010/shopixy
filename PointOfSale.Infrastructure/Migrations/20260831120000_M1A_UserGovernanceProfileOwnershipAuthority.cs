using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260831120000_M1A_UserGovernanceProfileOwnershipAuthority")]
public partial class M1A_UserGovernanceProfileOwnershipAuthority : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
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
    }
}
