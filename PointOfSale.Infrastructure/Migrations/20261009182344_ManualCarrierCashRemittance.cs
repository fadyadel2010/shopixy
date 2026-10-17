using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ManualCarrierCashRemittance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CarrierRemittances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CarrierCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ConfigurationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Environment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReceivedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Fees = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReceiptChannel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReceiptReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReceiptReferenceKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    EvidenceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ActualReceiptAttested = table.Column<bool>(type: "boolean", nullable: false),
                    ConfirmationBasis = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ReconciliationOutcome = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ReceivingAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClearingAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PostedJournalId = table.Column<Guid>(type: "uuid", nullable: true),
                    PostedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierRemittances", x => x.Id);
                    table.CheckConstraint("CK_CarrierRemittance_Receipt", "\"GrossAmount\" > 0 AND \"ReceivedAmount\" = \"GrossAmount\" AND \"Fees\" = 0 AND \"ActualReceiptAttested\" AND \"ReceiptChannel\" = 'PhysicalMerchantCash' AND \"ConfirmationBasis\" = 'OperatorAttestedPhysicalReceipt' AND \"ReconciliationOutcome\" = 'WholeOrderZeroFeeMatched' AND length(btrim(\"ReceiptReferenceKey\")) > 0 AND \"ReceivedAtUtc\" <= \"RecordedAtUtc\" AND (\"Outcome\" = 'Preparing' OR (\"Outcome\" = 'Posted' AND \"PostedJournalId\" IS NOT NULL AND \"PostedAtUtc\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_CarrierRemittances_CarrierConfigurations_ConfigurationId",
                        column: x => x.ConfigurationId,
                        principalTable: "CarrierConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierRemittances_LedgerAccounts_ClearingAccountId",
                        column: x => x.ClearingAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierRemittances_LedgerAccounts_ReceivingAccountId",
                        column: x => x.ReceivingAccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierRemittances_LedgerJournalEntries_PostedJournalId",
                        column: x => x.PostedJournalId,
                        principalTable: "LedgerJournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierRemittances_PaymentSettlementBatches_Id",
                        column: x => x.Id,
                        principalTable: "PaymentSettlementBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierRemittances_Users_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CarrierRemittanceOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RemittanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentBindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionEvidenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionJournalId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CarrierRemittanceOrders", x => x.Id);
                    table.CheckConstraint("CK_CarrierRemittanceOrder_Amount", "\"GrossAmount\" > 0");
                    table.ForeignKey(
                        name: "FK_CarrierRemittanceOrders_CarrierCodShipmentBindings_Shipment~",
                        column: x => x.ShipmentBindingId,
                        principalTable: "CarrierCodShipmentBindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierRemittanceOrders_CarrierCollectionEvidence_Collectio~",
                        column: x => x.CollectionEvidenceId,
                        principalTable: "CarrierCollectionEvidence",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierRemittanceOrders_CarrierRemittances_RemittanceId",
                        column: x => x.RemittanceId,
                        principalTable: "CarrierRemittances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierRemittanceOrders_LedgerJournalEntries_CollectionJour~",
                        column: x => x.CollectionJournalId,
                        principalTable: "LedgerJournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierRemittanceOrders_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierRemittanceOrders_PaymentSettlementOrders_AllocationId",
                        column: x => x.AllocationId,
                        principalTable: "PaymentSettlementOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CarrierRemittanceOrders_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CarrierRemittanceOrders_AllocationId",
                table: "CarrierRemittanceOrders",
                column: "AllocationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CarrierRemittanceOrders_CollectionEvidenceId",
                table: "CarrierRemittanceOrders",
                column: "CollectionEvidenceId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierRemittanceOrders_CollectionJournalId",
                table: "CarrierRemittanceOrders",
                column: "CollectionJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierRemittanceOrders_OrderId",
                table: "CarrierRemittanceOrders",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierRemittanceOrders_RemittanceId",
                table: "CarrierRemittanceOrders",
                column: "RemittanceId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierRemittanceOrders_ShipmentBindingId",
                table: "CarrierRemittanceOrders",
                column: "ShipmentBindingId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierRemittanceOrders_ShipmentId",
                table: "CarrierRemittanceOrders",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierRemittanceOrders_TenantId_OrderId",
                table: "CarrierRemittanceOrders",
                columns: new[] { "TenantId", "OrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CarrierRemittances_ClearingAccountId",
                table: "CarrierRemittances",
                column: "ClearingAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierRemittances_ConfigurationId",
                table: "CarrierRemittances",
                column: "ConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierRemittances_OperatorId",
                table: "CarrierRemittances",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierRemittances_PostedJournalId",
                table: "CarrierRemittances",
                column: "PostedJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierRemittances_ReceivingAccountId",
                table: "CarrierRemittances",
                column: "ReceivingAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CarrierRemittances_TenantId_ReceiptReferenceKey",
                table: "CarrierRemittances",
                columns: new[] { "TenantId", "ReceiptReferenceKey" },
                unique: true);

            // Additive, source-specific SQL guards. Certified Manual settlement history is untouched.
            migrationBuilder.Sql("""
                CREATE FUNCTION carrier_remittance_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF TG_TABLE_NAME = 'CarrierRemittances' AND TG_OP = 'UPDATE' THEN
                        IF OLD."Outcome" = 'Preparing' AND NEW."Outcome" = 'Posted' AND OLD."PostedJournalId" IS NULL
                           AND NEW."PostedJournalId" IS NOT NULL AND NEW."PostedAtUtc" IS NOT NULL
                           AND (to_jsonb(OLD) - ARRAY['Outcome','PostedJournalId','PostedAtUtc','UpdatedAt']) =
                               (to_jsonb(NEW) - ARRAY['Outcome','PostedJournalId','PostedAtUtc','UpdatedAt']) THEN
                            RETURN NEW;
                        END IF;
                    END IF;
                    RAISE EXCEPTION 'Carrier physical receipt and membership are permanent immutable history' USING ERRCODE = '55000';
                END; $$;
                CREATE TRIGGER carrier_remittance_immutable BEFORE UPDATE OR DELETE ON "CarrierRemittances"
                    FOR EACH ROW EXECUTE FUNCTION carrier_remittance_immutable();
                CREATE TRIGGER carrier_remittance_member_immutable BEFORE UPDATE OR DELETE ON "CarrierRemittanceOrders"
                    FOR EACH ROW EXECUTE FUNCTION carrier_remittance_immutable();

                CREATE FUNCTION carrier_remittance_commit_required() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE r "CarrierRemittances"%ROWTYPE; members integer;
                BEGIN
                    SELECT * INTO r FROM "CarrierRemittances" WHERE "Id" = NEW."Id";
                    SELECT count(*) INTO members FROM "CarrierRemittanceOrders" WHERE "RemittanceId" = r."Id";
                    IF r."Outcome" <> 'Posted' OR r."IsDeleted" OR members < 1 OR members > 100
                       OR NOT EXISTS (SELECT 1 FROM "PaymentSettlementBatches" b WHERE b."Id" = r."Id" AND b."TenantId" = r."TenantId"
                           AND b."SettlementType" = 'CARRIER_CASH_RECEIPT' AND b."SettlementId" = r."Id"::text
                           AND NOT b."IsDeleted" AND b."CreatedBy" = r."OperatorId" AND b."OrderCount" = members
                           AND b."GrossAmount" = r."GrossAmount" AND b."Fees" = 0 AND b."Currency" = r."Currency"
                           AND b."SettledAtUtc" = r."ReceivedAtUtc")
                       OR NOT EXISTS (SELECT 1 FROM "LedgerJournalEntries" j WHERE j."Id" = r."PostedJournalId" AND j."TenantId" = r."TenantId"
                           AND j."Status" = 1 AND NOT j."IsDeleted" AND j."SourceType" = 'CarrierCashRemittance'
                           AND j."SourceId" = r."Id" AND j."SourceReference" = r."Id"::text AND j."CreatedByUserId" = r."OperatorId")
                       OR (SELECT count(*) FROM "LedgerLines" l WHERE l."JournalEntryId" = r."PostedJournalId") <> 2
                       OR NOT EXISTS (SELECT 1 FROM "LedgerLines" l WHERE l."JournalEntryId" = r."PostedJournalId" AND l."TenantId" = r."TenantId"
                           AND l."AccountId" = r."ReceivingAccountId" AND l."DebitAmount" = r."GrossAmount" AND l."CreditAmount" = 0 AND NOT l."IsDeleted")
                       OR NOT EXISTS (SELECT 1 FROM "LedgerLines" l WHERE l."JournalEntryId" = r."PostedJournalId" AND l."TenantId" = r."TenantId"
                           AND l."AccountId" = r."ClearingAccountId" AND l."CreditAmount" = r."GrossAmount" AND l."DebitAmount" = 0 AND NOT l."IsDeleted")
                       OR NOT EXISTS (SELECT 1 FROM "AccountingOutboxMessages" a WHERE a."TenantId" = r."TenantId" AND a."BusinessEntityId" = r."Id"
                           AND a."SourceType" = 'CarrierCashRemittance' AND a."JournalId" = r."PostedJournalId" AND a."ProcessedAtUtc" IS NOT NULL)
                        OR NOT EXISTS (SELECT 1 FROM "OutboxMessages" o WHERE o."TenantId" = r."TenantId" AND o."AggregateId" = r."Id"
                            AND o."EventType" LIKE '%CarrierRemittanceAccountingPostedEvent%')
                        OR EXISTS (SELECT 1 FROM "CarrierRemittanceOrders" m
                            LEFT JOIN "Orders" ord ON ord."Id" = m."OrderId"
                            WHERE m."RemittanceId" = r."Id" AND (ord."TenantId" <> r."TenantId" OR ord."IsDeleted" OR ord."PaymentStatus" <> 9
                                OR NOT EXISTS (SELECT 1 FROM "OutboxMessages" o WHERE o."TenantId" = r."TenantId" AND o."AggregateId" = m."OrderId"
                                    AND o."EventType" LIKE '%OrderPaymentStatusChangedEvent%'
                                    AND o."EventPayload"::jsonb ->> 'PaymentReference' = r."Id"::text)))
                       OR EXISTS (SELECT 1 FROM "CarrierRemittanceOrders" m LEFT JOIN "PaymentSettlementOrders" a ON a."Id" = m."AllocationId"
                           LEFT JOIN "CarrierCollectionEvidence" e ON e."Id" = m."CollectionEvidenceId"
                           WHERE m."RemittanceId" = r."Id" AND (m."TenantId" <> r."TenantId" OR m."IsDeleted" OR a."TenantId" <> r."TenantId"
                               OR a."SettlementBatchId" <> r."Id" OR a."OrderId" <> m."OrderId" OR a."IsDeleted" OR a."OrderTotal" <> m."GrossAmount"
                               OR e."TenantId" <> r."TenantId" OR e."OrderId" <> m."OrderId" OR e."ShipmentId" <> m."ShipmentId"
                               OR e."ShipmentBindingId" <> m."ShipmentBindingId" OR e."AdmissionOutcome" <> 'Qualified'
                               OR e."ObservedAmount" <> m."GrossAmount" OR e."ConfigurationId" <> r."ConfigurationId"
                               OR e."AccountId" <> r."AccountId" OR e."Environment" <> r."Environment" OR e."CarrierCode" <> r."CarrierCode"
                               OR m."Currency" <> r."Currency" OR e."Currency" <> r."Currency"))
                       OR (SELECT sum("GrossAmount") FROM "CarrierRemittanceOrders" WHERE "RemittanceId" = r."Id") <> r."GrossAmount" THEN
                        RAISE EXCEPTION 'Carrier receipt must commit complete Posted journal, allocation and outbox' USING ERRCODE = '23514';
                    END IF;
                    RETURN NULL;
                END; $$;
                CREATE CONSTRAINT TRIGGER carrier_remittance_posted_commit AFTER INSERT OR UPDATE ON "CarrierRemittances"
                    DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION carrier_remittance_commit_required();

                CREATE FUNCTION carrier_remittance_allocation_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF OLD."SettlementType" = 'CARRIER_CASH_RECEIPT' OR
                       (TG_OP = 'UPDATE' AND NEW."SettlementType" = 'CARRIER_CASH_RECEIPT') THEN
                        RAISE EXCEPTION 'Carrier settlement allocation is permanent' USING ERRCODE = '55000';
                    END IF;
                    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER carrier_remittance_batch_immutable BEFORE UPDATE OR DELETE ON "PaymentSettlementBatches"
                    FOR EACH ROW EXECUTE FUNCTION carrier_remittance_allocation_immutable();

                CREATE FUNCTION carrier_remittance_shared_allocation_guard() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "PaymentSettlementBatches" WHERE "Id" = OLD."SettlementBatchId" AND "SettlementType" = 'CARRIER_CASH_RECEIPT') THEN
                        RAISE EXCEPTION 'Carrier whole-order allocation is immutable' USING ERRCODE = '55000';
                    END IF;
                    IF TG_OP = 'UPDATE' AND EXISTS (SELECT 1 FROM "PaymentSettlementBatches"
                        WHERE "Id" = NEW."SettlementBatchId" AND "SettlementType" = 'CARRIER_CASH_RECEIPT') THEN
                        RAISE EXCEPTION 'Cannot move allocation into carrier receipt history' USING ERRCODE = '55000';
                    END IF;
                    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER carrier_remittance_shared_allocation_guard BEFORE UPDATE OR DELETE ON "PaymentSettlementOrders"
                    FOR EACH ROW EXECUTE FUNCTION carrier_remittance_shared_allocation_guard();

                CREATE FUNCTION carrier_remittance_shared_allocation_admission() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "CarrierRemittances" WHERE "Id" = NEW."SettlementBatchId" AND "Outcome" = 'Posted') THEN
                        RAISE EXCEPTION 'Cannot add allocation to a posted carrier receipt' USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER carrier_remittance_shared_allocation_admission BEFORE INSERT ON "PaymentSettlementOrders"
                    FOR EACH ROW EXECUTE FUNCTION carrier_remittance_shared_allocation_admission();

                CREATE FUNCTION carrier_remittance_member_admission() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM "CarrierRemittances" WHERE "Id" = NEW."RemittanceId" AND "Outcome" = 'Preparing') THEN
                        RAISE EXCEPTION 'Cannot add membership to a posted carrier receipt' USING ERRCODE = '55000';
                    END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER carrier_remittance_member_admission BEFORE INSERT ON "CarrierRemittanceOrders"
                    FOR EACH ROW EXECUTE FUNCTION carrier_remittance_member_admission();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER carrier_remittance_batch_immutable ON "PaymentSettlementBatches";
                DROP FUNCTION carrier_remittance_allocation_immutable();
                DROP TRIGGER carrier_remittance_shared_allocation_guard ON "PaymentSettlementOrders";
                DROP FUNCTION carrier_remittance_shared_allocation_guard();
                DROP TRIGGER carrier_remittance_shared_allocation_admission ON "PaymentSettlementOrders";
                DROP FUNCTION carrier_remittance_shared_allocation_admission();
                """);
            migrationBuilder.DropTable(
                name: "CarrierRemittanceOrders");

            migrationBuilder.DropTable(
                name: "CarrierRemittances");
            migrationBuilder.Sql("DROP FUNCTION carrier_remittance_member_admission(); DROP FUNCTION carrier_remittance_commit_required(); DROP FUNCTION carrier_remittance_immutable();");
        }
    }
}
