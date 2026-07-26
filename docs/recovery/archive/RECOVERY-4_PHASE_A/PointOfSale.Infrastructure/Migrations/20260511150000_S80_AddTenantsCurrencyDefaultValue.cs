using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <summary>
    /// S80: Add DEFAULT 'EGP' to Tenants.Currency column.
    /// 
    /// The EF model configuration in ApplicationDbContext has HasDefaultValue("EGP") since S40,
    /// but the S40 migration did not apply this default to the database schema.
    /// This migration aligns the database with the EF model.
    /// </summary>
    public partial class S80_AddTenantsCurrencyDefaultValue : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                return;
            }

            migrationBuilder.Sql(@"
                -- Add DEFAULT 'EGP' to Tenants.Currency column
                ALTER TABLE ""Tenants"" 
                    ALTER COLUMN ""Currency"" SET DEFAULT 'EGP';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                return;
            }

            migrationBuilder.Sql(@"
                -- Remove DEFAULT from Tenants.Currency column
                ALTER TABLE ""Tenants"" 
                    ALTER COLUMN ""Currency"" DROP DEFAULT;
            ");
        }
    }
}
