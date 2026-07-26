using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    [Microsoft.EntityFrameworkCore.Infrastructure.DbContext(typeof(PointOfSale.Infrastructure.Data.ApplicationDbContext))]
    [Migration("20251202174743_AddBanksTable")]
    public partial class AddBanksTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.CreateTable(
                    name: "Banks",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(type: "uuid", nullable: false),
                        BankName = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                        BranchName = table.Column<string>(type: "text", nullable: true),
                        AccountNumber = table.Column<string>(type: "text", nullable: true),
                        IBAN = table.Column<string>(type: "text", nullable: true),
                        Notes = table.Column<string>(type: "text", nullable: true),
                        IsActive = table.Column<bool>(type: "boolean", nullable: false),
                        OpeningBalanceAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                        OpeningBalanceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                        CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                        UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                        CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                        UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                        IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Banks", x => x.Id);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_Banks_BankName",
                    table: "Banks",
                    column: "BankName",
                    unique: true,
                    filter: "\"IsDeleted\" = false");
            }
            else
            {
                migrationBuilder.CreateTable(
                    name: "Banks",
                    columns: table => new
                    {
                        Id = table.Column<Guid>(nullable: false),
                        BankName = table.Column<string>(maxLength: 450, nullable: false),
                        BranchName = table.Column<string>(nullable: true),
                        AccountNumber = table.Column<string>(nullable: true),
                        IBAN = table.Column<string>(nullable: true),
                        Notes = table.Column<string>(nullable: true),
                        IsActive = table.Column<bool>(nullable: false),
                        OpeningBalanceAmount = table.Column<decimal>(nullable: true),
                        OpeningBalanceDate = table.Column<DateTime>(nullable: true),
                        CreatedAt = table.Column<DateTime>(nullable: false),
                        UpdatedAt = table.Column<DateTime>(nullable: true),
                        CreatedBy = table.Column<Guid>(nullable: true),
                        UpdatedBy = table.Column<Guid>(nullable: true),
                        IsDeleted = table.Column<bool>(nullable: false)
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Banks", x => x.Id);
                    });

                migrationBuilder.CreateIndex(
                    name: "IX_Banks_BankName",
                    table: "Banks",
                    column: "BankName",
                    unique: true,
                    filter: "\"IsDeleted\" = false");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Banks");
        }
    }
}
