using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesRepresentatives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SalesRepresentativeId",
                table: "SalesInvoices",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalesRepresentatives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    MonthlyTarget = table.Column<decimal>(type: "numeric", nullable: true),
                    CommissionPercentage = table.Column<decimal>(type: "numeric", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesRepresentatives", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_SalesRepresentativeId",
                table: "SalesInvoices",
                column: "SalesRepresentativeId");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_SalesRepresentatives_SalesRepresentativeId",
                table: "SalesInvoices",
                column: "SalesRepresentativeId",
                principalTable: "SalesRepresentatives",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_SalesRepresentatives_SalesRepresentativeId",
                table: "SalesInvoices");

            migrationBuilder.DropTable(
                name: "SalesRepresentatives");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_SalesRepresentativeId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "SalesRepresentativeId",
                table: "SalesInvoices");
        }
    }
}
