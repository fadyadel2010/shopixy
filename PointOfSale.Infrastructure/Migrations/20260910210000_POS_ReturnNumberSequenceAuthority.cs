using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PointOfSale.Infrastructure.Data;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260910210000_POS_ReturnNumberSequenceAuthority")]
public partial class POS_ReturnNumberSequenceAuthority : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (!ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        migrationBuilder.Sql(
            """
            DO $$
            DECLARE
                max_return_number bigint;
                current_last_value bigint;
                current_is_called boolean;
                current_next_value bigint;
            BEGIN
                CREATE SEQUENCE IF NOT EXISTS return_number_seq
                    START WITH 1000
                    INCREMENT BY 1
                    MINVALUE 1000
                    CACHE 1;

                IF to_regclass('public."Returns"') IS NULL THEN
                    RETURN;
                END IF;

                SELECT COALESCE(MAX((regexp_match("ReturnNumber", '^(?:PR|SR)-\d{8}-(\d+)$'))[1]::bigint), 0)
                INTO max_return_number
                FROM "Returns"
                WHERE "ReturnNumber" ~ '^(?:PR|SR)-\d{8}-\d+$';

                SELECT last_value, is_called
                INTO current_last_value, current_is_called
                FROM return_number_seq;

                current_next_value := CASE
                    WHEN current_is_called THEN current_last_value + 1
                    ELSE current_last_value
                END;

                IF max_return_number < 1000 THEN
                    IF current_next_value < 1000 THEN
                        PERFORM setval('return_number_seq', 1000, false);
                    END IF;
                ELSIF current_next_value <= max_return_number THEN
                    PERFORM setval('return_number_seq', max_return_number, true);
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Return numbers are durable business identifiers. Do not remove sequence authority.
    }
}
