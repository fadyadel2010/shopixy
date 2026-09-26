using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PointOfSale.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StorefrontFooterCmsPageIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CmsPageGroupId",
                table: "StorefrontFooterLinks",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "StorefrontFooterLinks" AS link
                SET "CmsPageGroupId" = (
                    SELECT page."PageGroupId"
                    FROM "CmsPages" AS page
                    INNER JOIN "StorefrontFooterSections" AS section
                        ON section."Id" = link."SectionId"
                    WHERE section."TenantId" IS NOT NULL
                      AND page."TenantId" = section."TenantId"
                      AND NOT page."IsDeleted"
                      AND page."IsPublished"
                      AND NOT page."IsDraft"
                      AND page."PublishedAt" IS NOT NULL
                      AND LOWER(page."Slug") = LOWER(link."CmsPageSlug")
                    ORDER BY page."Version" DESC, page."PublishedAt" DESC
                    LIMIT 1)
                WHERE link."LinkType" = 'Cms'
                  AND link."CmsPageSlug" IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontFooterLinks_CmsPageGroupId",
                table: "StorefrontFooterLinks",
                column: "CmsPageGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StorefrontFooterLinks_CmsPageGroupId",
                table: "StorefrontFooterLinks");

            migrationBuilder.DropColumn(
                name: "CmsPageGroupId",
                table: "StorefrontFooterLinks");
        }
    }
}
