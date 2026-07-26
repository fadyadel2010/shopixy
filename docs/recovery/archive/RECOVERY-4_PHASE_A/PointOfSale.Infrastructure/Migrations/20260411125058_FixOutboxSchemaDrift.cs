using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixOutboxSchemaDrift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider.Contains("Npgsql"))
            {
                migrationBuilder.Sql("""
                    DO $$
                    BEGIN
                        IF NOT EXISTS (
                            SELECT 1
                            FROM information_schema.columns
                            WHERE table_schema = current_schema()
                              AND table_name = 'AccountingOutboxMessages'
                              AND column_name = 'CorrelationId') THEN
                            ALTER TABLE "AccountingOutboxMessages"
                            ADD COLUMN "CorrelationId" uuid NULL;
                        END IF;

                        IF EXISTS (
                            SELECT 1
                            FROM information_schema.columns
                            WHERE table_schema = current_schema()
                              AND table_name = 'AccountingOutboxMessages'
                              AND column_name = 'RowVersion') THEN
                            ALTER TABLE "AccountingOutboxMessages"
                            DROP COLUMN "RowVersion";
                        END IF;
                    END
                    $$;
                    """);
            }
            else
            {
                migrationBuilder.AddColumn<Guid>(
                    name: "CorrelationId",
                    table: "AccountingOutboxMessages",
                    type: "uniqueidentifier",
                    nullable: true);

                migrationBuilder.DropColumn(
                    name: "RowVersion",
                    table: "AccountingOutboxMessages");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider.Contains("Npgsql"))
            {
                migrationBuilder.Sql("""
                    DO $$
                    BEGIN
                        IF EXISTS (
                            SELECT 1
                            FROM information_schema.columns
                            WHERE table_schema = current_schema()
                              AND table_name = 'AccountingOutboxMessages'
                              AND column_name = 'CorrelationId') THEN
                            ALTER TABLE "AccountingOutboxMessages"
                            DROP COLUMN "CorrelationId";
                        END IF;

                        IF NOT EXISTS (
                            SELECT 1
                            FROM information_schema.columns
                            WHERE table_schema = current_schema()
                              AND table_name = 'AccountingOutboxMessages'
                              AND column_name = 'RowVersion') THEN
                            ALTER TABLE "AccountingOutboxMessages"
                            ADD COLUMN "RowVersion" bytea NULL;
                        END IF;
                    END
                    $$;
                    """);
            }
            else
            {
                migrationBuilder.DropColumn(
                    name: "CorrelationId",
                    table: "AccountingOutboxMessages");

                migrationBuilder.AddColumn<byte[]>(
                    name: "RowVersion",
                    table: "AccountingOutboxMessages",
                    type: "varbinary(max)",
                    nullable: true);
            }
        }
    }
}
