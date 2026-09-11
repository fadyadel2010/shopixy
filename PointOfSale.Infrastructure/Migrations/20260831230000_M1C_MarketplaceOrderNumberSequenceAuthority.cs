using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260831230000_M1C_MarketplaceOrderNumberSequenceAuthority")]
public partial class M1C_MarketplaceOrderNumberSequenceAuthority : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            DECLARE
                max_order_number bigint;
                current_last_value bigint;
                current_is_called boolean;
                current_next_value bigint;
            BEGIN
                CREATE SEQUENCE IF NOT EXISTS public."CommerceHubMarketplaceOrderNumberSeq"
                    START WITH 1
                    INCREMENT BY 1
                    MINVALUE 1
                    NO MAXVALUE
                    CACHE 1
                    NO CYCLE;

                SELECT COALESCE(MAX((regexp_match("OrderNumber", '^ORD-CH-(\d+)$'))[1]::bigint), 0)
                INTO max_order_number
                FROM "CommerceHubMarketplaceOrders"
                WHERE "OrderNumber" ~ '^ORD-CH-\d+$';

                SELECT last_value, is_called
                INTO current_last_value, current_is_called
                FROM public."CommerceHubMarketplaceOrderNumberSeq";

                current_next_value := CASE
                    WHEN current_is_called THEN current_last_value + 1
                    ELSE current_last_value
                END;

                IF current_next_value <= max_order_number THEN
                    PERFORM setval(
                        'public."CommerceHubMarketplaceOrderNumberSeq"'::regclass,
                        max_order_number,
                        true);
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Existing deployments created this sequence outside EF migrations; rollback must not remove shared authority.
    }
}
