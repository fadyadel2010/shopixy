using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Migrations;

[Migration("20261013210000_CanonicalAddressEvidenceFoundation")]
public sealed class CanonicalAddressEvidenceFoundation : ShipmentParcelRequestEvidence
{
    // Compose the retained immutable parcel target and its subsequent explicit
    // postal/claim deltas, never the mutable latest model snapshot.
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        base.BuildTargetModel(modelBuilder);
        modelBuilder.Entity("PointOfSale.Core.Entities.Geographic.AddressAdmissionEvidence")
            .Property<string>("PostalCode").HasMaxLength(32).HasColumnType("character varying(32)");
        ShippingWebhookOwnerClaims.AddClaimTarget(modelBuilder);
        modelBuilder.Entity("PointOfSale.Core.Entities.Geographic.AddressAdmissionEvidence")
            .Property<string>("CanonicalAddressEvidenceJson").HasColumnType("text");
    }

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("CanonicalAddressEvidenceJson", "AddressAdmissionEvidence", type: "text", nullable: true);
        migrationBuilder.Sql(GuardSql);
    }

    // No backfill and no automatic downgrade that could discard retained original evidence.
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Canonical address evidence requires a reviewed forward migration; retained evidence must not be dropped.");

    public const string GuardSql = """
        CREATE FUNCTION shopixy_guard_canonical_admission() RETURNS trigger AS $$
        DECLARE p jsonb; r jsonb; k text;
        BEGIN
          IF TG_OP <> 'INSERT' THEN
            IF OLD."CanonicalAddressEvidenceJson" IS NOT NULL OR
               (TG_OP = 'UPDATE' AND NEW."CanonicalAddressEvidenceJson" IS NOT NULL) THEN
              RAISE EXCEPTION 'Canonical admission evidence is insert-only and retained' USING ERRCODE = '23514';
            END IF;
            IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
          END IF;
          IF NEW."CanonicalAddressEvidenceJson" IS NULL THEN RETURN NEW; END IF;
          p := NEW."CanonicalAddressEvidenceJson"::jsonb;
          r := to_jsonb(NEW);
          IF octet_length(NEW."CanonicalAddressEvidenceJson") > 65536 OR jsonb_typeof(p) IS DISTINCT FROM 'object' OR
             p->>'Kind' IS DISTINCT FROM 'CanonicalAddressEvidence' OR p->>'Version' IS DISTINCT FROM '1' OR
             NEW."Status" IS DISTINCT FROM 'Admitted' OR NEW."TenantId" IS NULL OR NEW."OwnerId" IS NULL OR
             NEW."TenantId" = '00000000-0000-0000-0000-000000000000' OR NEW."OwnerId" = '00000000-0000-0000-0000-000000000000' OR
             p->>'EvidenceId' IS DISTINCT FROM NEW."Id"::text OR p->>'TenantId' IS DISTINCT FROM NEW."TenantId"::text OR
             p->>'OwnerId' IS DISTINCT FROM NEW."OwnerId"::text OR
             p->>'OwnerDomain' IS DISTINCT FROM (CASE NEW."OwnerDomain" WHEN 'Checkout' THEN '0' WHEN 'Warehouse' THEN '4' ELSE NULL END) OR
             p->>'ValidatorVersion' IS DISTINCT FROM 'canonical-address:v1:trim-utf8-sha256' OR
             p->>'CatalogReleaseId' IS DISTINCT FROM NEW."CatalogReleaseId"::text OR
             p->'Lineage'->>'CountryPlaceId' IS DISTINCT FROM NEW."CountryPlaceId"::text OR
             p->>'AddressFingerprintSha256' IS DISTINCT FROM NEW."AddressFingerprintSha256" OR
             p->>'NormalizedPostalCode' IS DISTINCT FROM NULLIF(btrim(NEW."PostalCode"),'') OR
             coalesce(p->>'PayloadSha256','') !~ '^[A-Fa-f0-9]{64}$' OR
             coalesce(p->>'AddressFingerprintSha256','') !~ '^[A-Fa-f0-9]{64}$' OR
             coalesce(p->>'InputFingerprintSha256','') !~ '^[A-Fa-f0-9]{64}$' OR
             coalesce(p->>'ReleaseChecksum','') !~ '^[A-Fa-f0-9]{64}$' OR
             coalesce(p->>'PublicationId','') = '' OR coalesce(p->>'CountryProfileId','') = '' OR
             coalesce(p->>'CountryIso2','') !~ '^[A-Z]{2}$' OR p->>'PostalValidationScope' IS DISTINCT FROM 'SyntaxOnly' OR
             NULLIF(btrim(p->'Inputs'->>'Street'),'') IS NULL OR NULLIF(btrim(p->'Inputs'->>'ContactName'),'') IS NULL OR
             NULLIF(btrim(p->'Inputs'->>'ContactPhone'),'') IS NULL OR NULLIF(btrim(p->'Inputs'->>'PrincipalReference'),'') IS NULL OR
             NULLIF(btrim(p->'Inputs'->>'OwnershipSource'),'') IS NULL OR
             (((NEW."OwnerDomain" = 'Warehouse' AND p->'Inputs'->>'AddressRole' = 'warehouse-origin') OR
                  (NEW."OwnerDomain" = 'Checkout' AND p->'Inputs'->>'AddressRole' IN ('checkout-shipping','checkout-billing'))) IS NOT TRUE) THEN
            RAISE EXCEPTION 'Canonical admission binding invalid' USING ERRCODE = '23514';
          END IF;
          FOREACH k IN ARRAY ARRAY['AdministrativeAreaPlaceId','LocalityPlaceId','DistrictPlaceId','ZonePlaceId'] LOOP
            IF p->'Lineage'->>k IS DISTINCT FROM r->>k THEN
              RAISE EXCEPTION 'Canonical admission lineage binding invalid' USING ERRCODE = '23514';
            END IF;
          END LOOP;
          IF (p->>'AdmittedAtUtc')::timestamptz IS DISTINCT FROM (r->>'AdmittedAtUtc')::timestamptz OR
             (p->>'PublishedAtUtc')::timestamptz > (p->>'ActivatedAtUtc')::timestamptz OR
             (p->>'ActivatedAtUtc')::timestamptz > (p->>'AdmittedAtUtc')::timestamptz OR
             (NEW."OwnerDomain" = 'Checkout' AND p->'Inputs'->>'SubmittedCountryIso2' IS DISTINCT FROM p->>'CountryIso2') THEN
            RAISE EXCEPTION 'Canonical admission time/country binding invalid' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END; $$ LANGUAGE plpgsql SET search_path FROM CURRENT;
        CREATE TRIGGER "TR_AddressAdmissionEvidence_Canonical" BEFORE INSERT OR UPDATE OR DELETE ON "AddressAdmissionEvidence"
          FOR EACH ROW EXECUTE FUNCTION shopixy_guard_canonical_admission();

        CREATE FUNCTION shopixy_validate_canonical_projection(r jsonb, p jsonb, prefix text) RETURNS void AS $$
        DECLARE k text; row_key text;
        BEGIN
          IF p->>'CatalogReleaseId' IS DISTINCT FROM r->>(prefix || 'GeographicCatalogReleaseId') OR
             p->>'CountryIso2' IS DISTINCT FROM NULLIF(btrim(r->>(CASE WHEN prefix = 'Destination' THEN 'ShippingCountry' ELSE prefix || 'Country' END)),'') OR
             p->>'NormalizedPostalCode' IS DISTINCT FROM NULLIF(btrim(r->>(CASE WHEN prefix = 'Destination' THEN 'ShippingZipCode' ELSE prefix || 'ZipCode' END)),'') THEN
            RAISE EXCEPTION 'Canonical projection release/country/postal invalid' USING ERRCODE = '23514';
          END IF;
          FOREACH k IN ARRAY ARRAY['CountryPlaceId','AdministrativeAreaPlaceId','LocalityPlaceId','DistrictPlaceId','ZonePlaceId'] LOOP
            IF p->'Lineage'->>k IS DISTINCT FROM r->>(prefix || k) THEN
              RAISE EXCEPTION 'Canonical projection lineage invalid' USING ERRCODE = '23514';
            END IF;
          END LOOP;
          FOREACH k IN ARRAY ARRAY['Street','Street2','Building','Floor','Apartment','Landmark','City','State'] LOOP
            row_key := prefix || k;
            IF prefix = 'Destination' AND k = 'City' THEN row_key := 'DestinationLocalityDisplayName'; END IF;
            IF prefix = 'Destination' AND k = 'State' THEN row_key := 'DestinationAdministrativeAreaDisplayName'; END IF;
            IF p->'Inputs'->>k IS DISTINCT FROM NULLIF(btrim(r->>row_key),'') THEN
              RAISE EXCEPTION 'Canonical projection address invalid' USING ERRCODE = '23514';
            END IF;
          END LOOP;
          IF prefix <> 'Billing' AND
             (p->'Inputs'->>'ContactName' IS DISTINCT FROM NULLIF(btrim(r->>(CASE WHEN prefix = 'Shipping' THEN 'GuestName' ELSE 'RecipientName' END)),'') OR
              p->'Inputs'->>'ContactPhone' IS DISTINCT FROM NULLIF(btrim(r->>(CASE WHEN prefix = 'Shipping' THEN 'GuestPhone' ELSE 'RecipientPhone' END)),'') OR
              p->'Inputs'->>'ContactEmail' IS DISTINCT FROM NULLIF(btrim(r->>(CASE WHEN prefix = 'Shipping' THEN 'GuestEmail' ELSE 'RecipientEmail' END)),'')) THEN
            RAISE EXCEPTION 'Canonical projection recipient invalid' USING ERRCODE = '23514';
          END IF;
        END; $$ LANGUAGE plpgsql SET search_path FROM CURRENT;

        CREATE FUNCTION shopixy_guard_canonical_order() RETURNS trigger AS $$
        DECLARE e record; prior jsonb; current_row jsonb; k text;
        BEGIN
          IF TG_OP <> 'INSERT' AND EXISTS (SELECT 1 FROM "AddressAdmissionEvidence" a WHERE
             a."Id" IN (OLD."ShippingAddressAdmissionEvidenceId", OLD."BillingAddressAdmissionEvidenceId") AND
             a."CanonicalAddressEvidenceJson" IS NOT NULL) THEN
            IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Canonical order evidence retained' USING ERRCODE = '23514'; END IF;
            prior := to_jsonb(OLD); current_row := to_jsonb(NEW);
            FOR k IN SELECT jsonb_object_keys(prior) LOOP
              IF (k LIKE 'Shipping%' AND k <> 'ShippingCost' OR k LIKE 'Billing%' OR
                  k IN ('TenantId','CartId','WarehouseId','FulfillmentMode','FulfillmentCarrierCode','GuestName','GuestPhone','GuestEmail','IsDeleted'))
                 AND prior->k IS DISTINCT FROM current_row->k THEN
                RAISE EXCEPTION 'Canonical order address/identity immutable' USING ERRCODE = '23514';
              END IF;
            END LOOP;
          END IF;
          IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
          FOR e IN SELECT * FROM "AddressAdmissionEvidence" a WHERE
              a."Id" IN (NEW."ShippingAddressAdmissionEvidenceId", NEW."BillingAddressAdmissionEvidenceId") AND
              a."CanonicalAddressEvidenceJson" IS NOT NULL LOOP
            IF e."TenantId" IS DISTINCT FROM NEW."TenantId" OR e."OwnerId" IS DISTINCT FROM NEW."CartId" OR e."OwnerDomain" <> 'Checkout' THEN
              RAISE EXCEPTION 'Canonical order ownership invalid' USING ERRCODE = '23514';
            END IF;
            IF e."Id" = NEW."ShippingAddressAdmissionEvidenceId" AND
               (e."CatalogReleaseId" IS DISTINCT FROM NEW."ShippingGeographicCatalogReleaseId" OR
                e."CountryPlaceId" IS DISTINCT FROM NEW."ShippingCountryPlaceId" OR
                e."CanonicalAddressEvidenceJson"::jsonb->'Inputs'->>'AddressRole' <> 'checkout-shipping' OR
                e."CanonicalAddressEvidenceJson"::jsonb->>'CountryIso2' IS DISTINCT FROM NEW."ShippingCountry") THEN
              RAISE EXCEPTION 'Canonical shipping linkage invalid' USING ERRCODE = '23514';
            END IF;
            IF e."Id" = NEW."ShippingAddressAdmissionEvidenceId" THEN
              PERFORM shopixy_validate_canonical_projection(to_jsonb(NEW), e."CanonicalAddressEvidenceJson"::jsonb, 'Shipping');
            END IF;
            IF e."Id" = NEW."BillingAddressAdmissionEvidenceId" THEN
              IF e."CanonicalAddressEvidenceJson"::jsonb->'Inputs'->>'AddressRole' IS DISTINCT FROM 'checkout-billing' THEN
                RAISE EXCEPTION 'Canonical billing role invalid' USING ERRCODE = '23514';
              END IF;
              PERFORM shopixy_validate_canonical_projection(to_jsonb(NEW), e."CanonicalAddressEvidenceJson"::jsonb, 'Billing');
            END IF;
          END LOOP;
          RETURN NEW;
        END; $$ LANGUAGE plpgsql SET search_path FROM CURRENT;
        CREATE TRIGGER "TR_Orders_CanonicalAddress" BEFORE INSERT OR UPDATE OR DELETE ON "Orders"
          FOR EACH ROW EXECUTE FUNCTION shopixy_guard_canonical_order();

        CREATE FUNCTION shopixy_guard_canonical_shipment() RETURNS trigger AS $$
         DECLARE e record; o record; prior jsonb; current_row jsonb; k text; p jsonb; c jsonb; row_key text;
        BEGIN
          IF TG_OP <> 'INSERT' AND (OLD."OriginGeographicSnapshotVersion" = 2 OR EXISTS
             (SELECT 1 FROM "AddressAdmissionEvidence" a WHERE a."Id" = OLD."DestinationAddressAdmissionEvidenceId" AND
              a."CanonicalAddressEvidenceJson" IS NOT NULL)) THEN
            IF TG_OP = 'DELETE' THEN RAISE EXCEPTION 'Canonical shipment evidence retained' USING ERRCODE = '23514'; END IF;
            prior := to_jsonb(OLD); current_row := to_jsonb(NEW);
            FOR k IN SELECT jsonb_object_keys(prior) LOOP
              IF (k LIKE 'Destination%' OR OLD."OriginSnapshotJson" IS NOT NULL AND k LIKE 'Origin%' OR
                  k IN ('TenantId','OrderId','CarrierCode','ServiceLevel','ShippingMethodId','RecipientName','RecipientPhone','RecipientEmail',
                        'ShippingCountry','ShippingZipCode','IsDeleted')) AND prior->k IS DISTINCT FROM current_row->k THEN
                RAISE EXCEPTION 'Canonical shipment address/identity immutable' USING ERRCODE = '23514';
              END IF;
            END LOOP;
          END IF;
          IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
          SELECT * INTO e FROM "AddressAdmissionEvidence" a WHERE a."Id" = NEW."DestinationAddressAdmissionEvidenceId" AND
              a."CanonicalAddressEvidenceJson" IS NOT NULL;
          IF FOUND THEN
            SELECT * INTO o FROM "Orders" WHERE "Id" = NEW."OrderId" AND "TenantId" = NEW."TenantId";
            IF NOT FOUND OR e."TenantId" IS DISTINCT FROM NEW."TenantId" OR e."OwnerId" IS DISTINCT FROM o."CartId" OR
               e."OwnerDomain" <> 'Checkout' OR e."Id" IS DISTINCT FROM o."ShippingAddressAdmissionEvidenceId" OR
               e."CatalogReleaseId" IS DISTINCT FROM NEW."DestinationGeographicCatalogReleaseId" OR
               e."CountryPlaceId" IS DISTINCT FROM NEW."DestinationCountryPlaceId" THEN
              RAISE EXCEPTION 'Canonical destination linkage invalid' USING ERRCODE = '23514';
            END IF;
            IF e."CanonicalAddressEvidenceJson"::jsonb->'Inputs'->>'AddressRole' IS DISTINCT FROM 'checkout-shipping' OR
               to_jsonb(NEW)->>'ShippingMethodId' IS DISTINCT FROM to_jsonb(o)->>'ShippingMethodId' OR
               to_jsonb(NEW)->>'ServiceLevel' IS DISTINCT FROM to_jsonb(o)->>'ShippingServiceLevel' OR
               to_jsonb(NEW)->>'CarrierCode' IS DISTINCT FROM (CASE WHEN to_jsonb(o)->>'FulfillmentMode' = 'Manual' THEN 'manual' ELSE to_jsonb(o)->>'FulfillmentCarrierCode' END) THEN
              RAISE EXCEPTION 'Canonical destination method/role invalid' USING ERRCODE = '23514';
            END IF;
            PERFORM shopixy_validate_canonical_projection(to_jsonb(NEW), e."CanonicalAddressEvidenceJson"::jsonb, 'Destination');
          END IF;
          IF NEW."OriginGeographicSnapshotVersion" = 2 THEN
            p := NEW."OriginSnapshotJson"::jsonb;
            SELECT * INTO e FROM "AddressAdmissionEvidence" a WHERE a."Id" = NEW."OriginAddressAdmissionEvidenceId";
            IF NOT FOUND OR e."TenantId" IS DISTINCT FROM NEW."TenantId" OR e."OwnerId" IS DISTINCT FROM NEW."OriginWarehouseId" OR
               e."OwnerDomain" <> 'Warehouse' OR e."CanonicalAddressEvidenceJson" IS NULL OR
               p->>'CanonicalAddressEvidenceJson' IS DISTINCT FROM e."CanonicalAddressEvidenceJson" OR
               p->>'TenantId' IS DISTINCT FROM NEW."TenantId"::text OR p->>'OrderId' IS DISTINCT FROM NEW."OrderId"::text OR
                p->>'ShipmentId' IS DISTINCT FROM NEW."Id"::text THEN
              RAISE EXCEPTION 'Canonical origin linkage invalid' USING ERRCODE = '23514';
            END IF;
            SELECT * INTO o FROM "Orders" WHERE "Id" = NEW."OrderId" AND "TenantId" = NEW."TenantId";
            IF NOT FOUND OR to_jsonb(o)->>'WarehouseId' IS DISTINCT FROM NEW."OriginWarehouseId"::text OR p->>'SnapshotVersion' IS DISTINCT FROM '2' OR
               p->>'WarehouseId' IS DISTINCT FROM NEW."OriginWarehouseId"::text OR p->>'AddressAdmissionEvidenceId' IS DISTINCT FROM e."Id"::text THEN
              RAISE EXCEPTION 'Canonical origin order/warehouse invalid' USING ERRCODE = '23514';
            END IF;
            c := e."CanonicalAddressEvidenceJson"::jsonb;
            IF p->>'CatalogReleaseId' IS DISTINCT FROM c->>'CatalogReleaseId' OR
               p->>'CountryIso2' IS DISTINCT FROM c->>'CountryIso2' OR p->>'PostalCode' IS DISTINCT FROM c->>'NormalizedPostalCode' OR
               p->>'Street' IS DISTINCT FROM c->'Inputs'->>'Street' OR p->>'ContactName' IS DISTINCT FROM c->'Inputs'->>'ContactName' OR
               p->>'ContactPhone' IS DISTINCT FROM c->'Inputs'->>'ContactPhone' THEN
              RAISE EXCEPTION 'Canonical origin payload projection invalid' USING ERRCODE = '23514';
            END IF;
            FOREACH k IN ARRAY ARRAY['CountryPlaceId','AdministrativeAreaPlaceId','LocalityPlaceId','DistrictPlaceId','ZonePlaceId'] LOOP
              IF p->>k IS DISTINCT FROM c->'Lineage'->>k OR p->>k IS DISTINCT FROM to_jsonb(NEW)->>('Origin' || k) THEN
                RAISE EXCEPTION 'Canonical origin lineage invalid' USING ERRCODE = '23514';
              END IF;
            END LOOP;
            FOREACH k IN ARRAY ARRAY['CatalogReleaseId','CountryIso2','Street','ContactName','ContactPhone','CountryDisplayName',
                                    'AdministrativeAreaDisplayName','LocalityDisplayName','DistrictDisplayName','ZoneDisplayName'] LOOP
              row_key := CASE WHEN k = 'CatalogReleaseId' THEN 'OriginGeographicCatalogReleaseId' ELSE 'Origin' || k END;
              IF p->>k IS DISTINCT FROM to_jsonb(NEW)->>row_key THEN
                RAISE EXCEPTION 'Canonical origin row projection invalid' USING ERRCODE = '23514';
              END IF;
            END LOOP;
            IF (p->>'SnapshottedAtUtc')::timestamptz IS DISTINCT FROM (to_jsonb(NEW)->>'OriginGeographicSnapshotCreatedAtUtc')::timestamptz THEN
              RAISE EXCEPTION 'Canonical origin snapshot time invalid' USING ERRCODE = '23514';
            END IF;
            IF TG_OP = 'UPDATE' AND OLD."OriginSnapshotJson" IS NULL AND
                (OLD."ExternalRegistrationAttemptCount" > 0 OR
                OLD."ExternalRegistrationLastAttemptAtUtc" IS NOT NULL OR OLD."CarrierRequestSnapshotJson" IS NOT NULL OR
                OLD."CarrierRequestFingerprint" IS NOT NULL OR OLD."ExternalReferenceId" IS NOT NULL OR OLD."TrackingNumber" IS NOT NULL) THEN
              RAISE EXCEPTION 'Uncertain registration cannot capture new origin' USING ERRCODE = '23514';
            END IF;
          END IF;
          RETURN NEW;
        END; $$ LANGUAGE plpgsql SET search_path FROM CURRENT;
        CREATE TRIGGER "TR_Shipments_CanonicalAddress" BEFORE INSERT OR UPDATE OR DELETE ON "Shipments"
          FOR EACH ROW EXECUTE FUNCTION shopixy_guard_canonical_shipment();
        """;
}
