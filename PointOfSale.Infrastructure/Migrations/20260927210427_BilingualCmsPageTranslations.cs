using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BilingualCmsPageTranslations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_CmsPages_PageGroupId",
                table: "CmsPages",
                newName: "UQ_CmsPages_PageGroupId_Published");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_CmsPages_Id_TenantId",
                table: "CmsPages",
                columns: new[] { "Id", "TenantId" });

            migrationBuilder.CreateTable(
                name: "CmsPageTranslations",
                columns: table => new
                {
                    CmsPageVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    MetaTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MetaDescription = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    MetaKeywords = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OpenGraphImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CmsPageTranslations", x => new { x.CmsPageVersionId, x.Locale });
                    table.CheckConstraint("CK_CmsPageTranslations_ContentType", "\"ContentType\" IN ('RichText', 'Blocks')");
                    table.CheckConstraint("CK_CmsPageTranslations_Locale", "\"Locale\" IN ('en', 'ar')");
                    table.CheckConstraint("CK_CmsPageTranslations_SchemaVersion", "\"SchemaVersion\" > 0");
                    table.ForeignKey(
                        name: "FK_CmsPageTranslations_CmsPages_CmsPageVersionId_TenantId",
                        columns: x => new { x.CmsPageVersionId, x.TenantId },
                        principalTable: "CmsPages",
                        principalColumns: new[] { "Id", "TenantId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CmsPages_PageGroupId",
                table: "CmsPages",
                column: "PageGroupId");

            migrationBuilder.CreateIndex(
                name: "UQ_CmsPages_PageGroupId_Draft",
                table: "CmsPages",
                column: "PageGroupId",
                unique: true,
                filter: "\"IsDraft\" AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "UQ_CmsPages_Tenant_Draft_Slug",
                table: "CmsPages",
                columns: new[] { "TenantId", "Slug" },
                unique: true,
                filter: "\"IsDraft\" AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_CmsPageTranslations_CmsPageVersionId_TenantId",
                table: "CmsPageTranslations",
                columns: new[] { "CmsPageVersionId", "TenantId" });

            migrationBuilder.CreateIndex(
                name: "IX_CmsPageTranslations_TenantId_Locale_CmsPageVersionId",
                table: "CmsPageTranslations",
                columns: new[] { "TenantId", "Locale", "CmsPageVersionId" });

            migrationBuilder.Sql(
                """
                INSERT INTO "CmsPageTranslations" (
                    "CmsPageVersionId",
                    "Locale",
                    "TenantId",
                    "Title",
                    "Content",
                    "ContentType",
                    "SchemaVersion",
                    "MetaTitle",
                    "MetaDescription",
                    "MetaKeywords",
                    "OpenGraphImageUrl")
                SELECT
                    page."Id",
                    'en',
                    page."TenantId",
                    page."Title",
                    page."Content",
                    page."ContentType",
                    page."SchemaVersion",
                    page."MetaTitle",
                    page."MetaDescription",
                    page."MetaKeywords",
                    page."OpenGraphImageUrl"
                FROM "CmsPages" AS page;
                """);

            migrationBuilder.Sql(
                """
                DO $verification$
                BEGIN
                    IF (SELECT COUNT(*) FROM "CmsPages") <>
                       (SELECT COUNT(*) FROM "CmsPageTranslations" WHERE "Locale" = 'en') THEN
                        RAISE EXCEPTION 'CMS translation backfill count mismatch';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "CmsPageTranslations"
                        WHERE "Locale" = 'ar') THEN
                        RAISE EXCEPTION 'CMS translation backfill fabricated Arabic content';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "CmsPages" AS page
                        LEFT JOIN "CmsPageTranslations" AS translation
                          ON translation."CmsPageVersionId" = page."Id"
                         AND translation."Locale" = 'en'
                        WHERE translation."CmsPageVersionId" IS NULL
                           OR translation."TenantId" IS DISTINCT FROM page."TenantId"
                           OR translation."Title" IS DISTINCT FROM page."Title"
                           OR translation."Content" IS DISTINCT FROM page."Content"
                           OR translation."ContentType" IS DISTINCT FROM page."ContentType"
                           OR translation."SchemaVersion" IS DISTINCT FROM page."SchemaVersion"
                           OR translation."MetaTitle" IS DISTINCT FROM page."MetaTitle"
                           OR translation."MetaDescription" IS DISTINCT FROM page."MetaDescription"
                           OR translation."MetaKeywords" IS DISTINCT FROM page."MetaKeywords"
                           OR translation."OpenGraphImageUrl" IS DISTINCT FROM page."OpenGraphImageUrl") THEN
                        RAISE EXCEPTION 'CMS English translation backfill is not lossless';
                    END IF;
                END
                $verification$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CmsPageTranslations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CmsPages_Id_TenantId",
                table: "CmsPages");

            migrationBuilder.DropIndex(
                name: "IX_CmsPages_PageGroupId",
                table: "CmsPages");

            migrationBuilder.DropIndex(
                name: "UQ_CmsPages_PageGroupId_Draft",
                table: "CmsPages");

            migrationBuilder.DropIndex(
                name: "UQ_CmsPages_Tenant_Draft_Slug",
                table: "CmsPages");

            migrationBuilder.RenameIndex(
                name: "UQ_CmsPages_PageGroupId_Published",
                table: "CmsPages",
                newName: "IX_CmsPages_PageGroupId");
        }
    }
}
