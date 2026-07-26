using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentOutForDeliveryTimestamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Shipments_Orders_OrderId",
                table: "Shipments");

            migrationBuilder.DropForeignKey(
                name: "FK_Shipments_ShippingMethods_ShippingMethodId",
                table: "Shipments");

            migrationBuilder.RenameColumn(
                name: "PackedAtUtc",
                table: "Shipments",
                newName: "ReturnedAtUtc");

            migrationBuilder.RenameColumn(
                name: "CarrierName",
                table: "Shipments",
                newName: "LabelData");

            migrationBuilder.AlterColumn<decimal>(
                name: "WeightKg",
                table: "Shipments",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TrackingUrl",
                table: "Shipments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TrackingNumber",
                table: "Shipments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ShippingZipCode",
                table: "Shipments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ShippingState",
                table: "Shipments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ShippingMethodName",
                table: "Shipments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<Guid>(
                name: "ShippingMethodId",
                table: "Shipments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<string>(
                name: "ShippingCountry",
                table: "Shipments",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ShippingCost",
                table: "Shipments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<string>(
                name: "ShippingCity",
                table: "Shipments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ShippingAddress",
                table: "Shipments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "ShipmentNumber",
                table: "Shipments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "RecipientPhone",
                table: "Shipments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RecipientName",
                table: "Shipments",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "Shipments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "Shipments",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "CancellationReason",
                table: "Shipments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AwbNumber",
                table: "Shipments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierCode",
                table: "Shipments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CarrierCreateResponse",
                table: "Shipments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierMetadata",
                table: "Shipments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CarrierTrackingResponse",
                table: "Shipments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CodAmount",
                table: "Shipments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CodCollected",
                table: "Shipments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CodCollectedAtUtc",
                table: "Shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryAttempts",
                table: "Shipments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "FailedAtUtc",
                table: "Shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "Shipments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCashOnDelivery",
                table: "Shipments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LabelFormat",
                table: "Shipments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LabelUrl",
                table: "Shipments",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastCarrierSyncAtUtc",
                table: "Shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                table: "Shipments",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                table: "Shipments",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OutForDeliveryAtUtc",
                table: "Shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PackageCount",
                table: "Shipments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PackageDescription",
                table: "Shipments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PickedUpAtUtc",
                table: "Shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadyForPickupAtUtc",
                table: "Shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecipientEmail",
                table: "Shipments",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnReason",
                table: "Shipments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceLevel",
                table: "Shipments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShippingAddressLine2",
                table: "Shipments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CarrierConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CarrierCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    ApiKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ApiSecret = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AccountId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ApiUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UseSandbox = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsCod = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsTracking = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsWebhooks = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsLabels = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsQuotes = table.Column<bool>(type: "boolean", nullable: false),
                    SupportsCancellation = table.Column<bool>(type: "boolean", nullable: false),
                    Configuration = table.Column<string>(type: "text", nullable: true),
                    WebhookUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    WebhookSecret = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    PickupAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PickupCity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PickupCountry = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    PickupPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PickupContactName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AvailableServiceLevels = table.Column<string>(type: "text", nullable: true),
                    DefaultServiceLevel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UseCarrierRates = table.Column<bool>(type: "boolean", nullable: false),
                    FixedShippingCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MarkupPercentage = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    FreeShippingThreshold = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastValidatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastValidationSucceeded = table.Column<bool>(type: "boolean", nullable: true),
                    LastValidationError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierConfigurations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_CarrierCode_TrackingNumber",
                table: "Shipments",
                columns: new[] { "CarrierCode", "TrackingNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_TenantId_CarrierCode_Status",
                table: "Shipments",
                columns: new[] { "TenantId", "CarrierCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_TenantId_OrderId",
                table: "Shipments",
                columns: new[] { "TenantId", "OrderId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_TenantId_Status_LastCarrierSyncAtUtc",
                table: "Shipments",
                columns: new[] { "TenantId", "Status", "LastCarrierSyncAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierConfigurations_TenantId_CarrierCode",
                table: "CarrierConfigurations",
                columns: new[] { "TenantId", "CarrierCode" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierConfigurations_TenantId_IsDefault",
                table: "CarrierConfigurations",
                columns: new[] { "TenantId", "IsDefault" },
                filter: "\"IsDefault\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierConfigurations_TenantId_IsEnabled_SortOrder",
                table: "CarrierConfigurations",
                columns: new[] { "TenantId", "IsEnabled", "SortOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_Shipments_Orders_OrderId",
                table: "Shipments",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Shipments_ShippingMethods_ShippingMethodId",
                table: "Shipments",
                column: "ShippingMethodId",
                principalTable: "ShippingMethods",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Shipments_Orders_OrderId",
                table: "Shipments");

            migrationBuilder.DropForeignKey(
                name: "FK_Shipments_ShippingMethods_ShippingMethodId",
                table: "Shipments");

            migrationBuilder.DropTable(
                name: "CarrierConfigurations");

            migrationBuilder.DropIndex(
                name: "IX_Shipments_CarrierCode_TrackingNumber",
                table: "Shipments");

            migrationBuilder.DropIndex(
                name: "IX_Shipments_TenantId_CarrierCode_Status",
                table: "Shipments");

            migrationBuilder.DropIndex(
                name: "IX_Shipments_TenantId_OrderId",
                table: "Shipments");

            migrationBuilder.DropIndex(
                name: "IX_Shipments_TenantId_Status_LastCarrierSyncAtUtc",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "AwbNumber",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "CarrierCode",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "CarrierCreateResponse",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "CarrierMetadata",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "CarrierTrackingResponse",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "CodAmount",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "CodCollected",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "CodCollectedAtUtc",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "DeliveryAttempts",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "FailedAtUtc",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "IsCashOnDelivery",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "LabelFormat",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "LabelUrl",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "LastCarrierSyncAtUtc",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "OutForDeliveryAtUtc",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "PackageCount",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "PackageDescription",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "PickedUpAtUtc",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ReadyForPickupAtUtc",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "RecipientEmail",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ReturnReason",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ServiceLevel",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ShippingAddressLine2",
                table: "Shipments");

            migrationBuilder.RenameColumn(
                name: "ReturnedAtUtc",
                table: "Shipments",
                newName: "PackedAtUtc");

            migrationBuilder.RenameColumn(
                name: "LabelData",
                table: "Shipments",
                newName: "CarrierName");

            migrationBuilder.AlterColumn<decimal>(
                name: "WeightKg",
                table: "Shipments",
                type: "numeric",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,3)",
                oldPrecision: 10,
                oldScale: 3,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TrackingUrl",
                table: "Shipments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "TrackingNumber",
                table: "Shipments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ShippingZipCode",
                table: "Shipments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ShippingState",
                table: "Shipments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ShippingMethodName",
                table: "Shipments",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ShippingMethodId",
                table: "Shipments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ShippingCountry",
                table: "Shipments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ShippingCost",
                table: "Shipments",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<string>(
                name: "ShippingCity",
                table: "Shipments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ShippingAddress",
                table: "Shipments",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<string>(
                name: "ShipmentNumber",
                table: "Shipments",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AlterColumn<string>(
                name: "RecipientPhone",
                table: "Shipments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RecipientName",
                table: "Shipments",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "Shipments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Currency",
                table: "Shipments",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.AlterColumn<string>(
                name: "CancellationReason",
                table: "Shipments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Shipments_Orders_OrderId",
                table: "Shipments",
                column: "OrderId",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Shipments_ShippingMethods_ShippingMethodId",
                table: "Shipments",
                column: "ShippingMethodId",
                principalTable: "ShippingMethods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
