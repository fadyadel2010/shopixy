using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ManualWalletCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CommerceAiAutoGreetingEnabled",
                table: "StoreConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CommerceAiAutoGreetingMessage",
                table: "StoreConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "CommerceAiEnabled",
                table: "StoreConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CommerceAiEnabledAt",
                table: "StoreConfigs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommerceAiPersonality",
                table: "StoreConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CommerceAiUpdatedBy",
                table: "StoreConfigs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommerceAiWidgetPosition",
                table: "StoreConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CommerceAiWidgetStyle",
                table: "StoreConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CarrierCode",
                table: "ShippingMethods",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "PaymentIntents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ProofImageDeleted",
                table: "PaymentIntents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProofImageExpiresAtUtc",
                table: "PaymentIntents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProofImageUrl",
                table: "PaymentIntents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceCode",
                table: "PaymentIntents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "PaymentIntents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAtUtc",
                table: "PaymentIntents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerifiedByAdmin",
                table: "PaymentIntents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WalletPhoneNumber",
                table: "PaymentIntents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WalletType",
                table: "PaymentIntents",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommerceAiAutoGreetingEnabled",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "CommerceAiAutoGreetingMessage",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "CommerceAiEnabled",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "CommerceAiEnabledAt",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "CommerceAiPersonality",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "CommerceAiUpdatedBy",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "CommerceAiWidgetPosition",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "CommerceAiWidgetStyle",
                table: "StoreConfigs");

            migrationBuilder.DropColumn(
                name: "CarrierCode",
                table: "ShippingMethods");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "ProofImageDeleted",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "ProofImageExpiresAtUtc",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "ProofImageUrl",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "ReferenceCode",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "VerifiedAtUtc",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "VerifiedByAdmin",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "WalletPhoneNumber",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "WalletType",
                table: "PaymentIntents");
        }
    }
}
