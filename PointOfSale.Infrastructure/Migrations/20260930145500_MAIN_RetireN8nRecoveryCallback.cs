using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260930145500_MAIN_RetireN8nRecoveryCallback")]
public partial class MAIN_RetireN8nRecoveryCallback : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "N8nWebhookApiKey",
            table: "TenantAutomationSettings");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "N8nWebhookApiKey",
            table: "TenantAutomationSettings",
            type: "text",
            nullable: true);
    }
}
