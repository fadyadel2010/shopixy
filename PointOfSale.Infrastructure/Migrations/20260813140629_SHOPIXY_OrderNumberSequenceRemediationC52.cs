using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SHOPIXY_OrderNumberSequenceRemediationC52 : Migration
    {
        /// <inheritdoc />
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
                    CREATE SEQUENCE IF NOT EXISTS public.order_number_seq
                        START WITH 1000
                        INCREMENT BY 1
                        MINVALUE 1000
                        NO MAXVALUE
                        CACHE 1
                        NO CYCLE;

                    SELECT COALESCE(MAX((regexp_match("OrderNumber", '^ORD-(?:[A-F0-9]{4}-\d{4}|\d{8})-(\d+)$'))[1]::bigint), 0)
                    INTO max_order_number
                    FROM "Orders"
                    WHERE "OrderNumber" ~ '^ORD-(?:[A-F0-9]{4}-\d{4}|\d{8})-\d+$';

                    SELECT last_value, is_called
                    INTO current_last_value, current_is_called
                    FROM public.order_number_seq;

                    current_next_value := CASE
                        WHEN current_is_called THEN current_last_value + 1
                        ELSE current_last_value
                    END;

                    IF max_order_number < 1000 THEN
                        IF current_next_value < 1000 THEN
                            PERFORM setval('public.order_number_seq'::regclass, 1000, false);
                        END IF;
                    ELSIF current_next_value <= max_order_number THEN
                        PERFORM setval('public.order_number_seq'::regclass, max_order_number, true);
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP SEQUENCE IF EXISTS public.order_number_seq;""");
        }
    }
}
