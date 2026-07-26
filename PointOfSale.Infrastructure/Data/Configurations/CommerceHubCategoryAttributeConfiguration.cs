using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubCategoryAttributeConfiguration : IEntityTypeConfiguration<CommerceHubCategoryAttribute>
{
    public void Configure(EntityTypeBuilder<CommerceHubCategoryAttribute> builder)
    {
        builder.ToTable("CommerceHubCategoryAttributes");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CategoryId)
            .IsRequired();

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.NameAr)
            .HasMaxLength(200);

        builder.Property(x => x.Type)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.IsRequired)
            .IsRequired();

        builder.Property(x => x.IsFilterable)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.IsSearchable)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.OptionsJson)
            .HasColumnType("text");

        builder.Property(x => x.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

        // Index: list attributes for a category, ordered
        builder.HasIndex(x => new { x.CategoryId, x.SortOrder })
            .HasDatabaseName("IX_CommerceHubCategoryAttributes_CategoryId_SortOrder");

        // FK to category
        builder.HasOne(x => x.Category)
            .WithMany(x => x.Attributes)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // Seed data
        SeedAttributes(builder);
    }

    private static void SeedAttributes(EntityTypeBuilder<CommerceHubCategoryAttribute> builder)
    {
        // TV attributes
        var tvCategoryId = Guid.Parse("10000000-0000-0000-0000-000000000011");
        builder.HasData(
            new CommerceHubCategoryAttribute
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000001"),
                CategoryId = tvCategoryId,
                Name = "Brand",
                NameAr = "الماركة",
                Type = "select",
                IsRequired = true,
                IsFilterable = true,
                IsSearchable = true,
                OptionsJson = "[\"Samsung\",\"LG\",\"Sony\",\"TCL\",\"Hisense\",\"Toshiba\",\"Panasonic\"]",
                SortOrder = 1
            },
            new CommerceHubCategoryAttribute
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000002"),
                CategoryId = tvCategoryId,
                Name = "Screen Size (inches)",
                NameAr = "حجم الشاشة (بوصة)",
                Type = "number",
                IsRequired = true,
                IsFilterable = true,
                IsSearchable = false,
                OptionsJson = null,
                SortOrder = 2
            },
            new CommerceHubCategoryAttribute
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000003"),
                CategoryId = tvCategoryId,
                Name = "Resolution",
                NameAr = "الدقة",
                Type = "select",
                IsRequired = false,
                IsFilterable = true,
                IsSearchable = true,
                OptionsJson = "[\"HD\",\"Full HD\",\"4K UHD\",\"8K\"]",
                SortOrder = 3
            }
        );

        // Mobile attributes
        var mobileCategoryId = Guid.Parse("10000000-0000-0000-0000-000000000012");
        builder.HasData(
            new CommerceHubCategoryAttribute
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000011"),
                CategoryId = mobileCategoryId,
                Name = "Brand",
                NameAr = "الماركة",
                Type = "select",
                IsRequired = true,
                IsFilterable = true,
                IsSearchable = true,
                OptionsJson = "[\"Apple\",\"Samsung\",\"Xiaomi\",\"Huawei\",\"Oppo\",\"Vivo\",\"Realme\",\"OnePlus\"]",
                SortOrder = 1
            },
            new CommerceHubCategoryAttribute
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000012"),
                CategoryId = mobileCategoryId,
                Name = "Storage",
                NameAr = "السعة التخزينية",
                Type = "select",
                IsRequired = true,
                IsFilterable = true,
                IsSearchable = true,
                OptionsJson = "[\"32GB\",\"64GB\",\"128GB\",\"256GB\",\"512GB\",\"1TB\"]",
                SortOrder = 2
            },
            new CommerceHubCategoryAttribute
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000013"),
                CategoryId = mobileCategoryId,
                Name = "RAM",
                NameAr = "الذاكرة العشوائية",
                Type = "select",
                IsRequired = false,
                IsFilterable = true,
                IsSearchable = false,
                OptionsJson = "[\"2GB\",\"3GB\",\"4GB\",\"6GB\",\"8GB\",\"12GB\",\"16GB\"]",
                SortOrder = 3
            }
        );

        // Laptop attributes
        var laptopCategoryId = Guid.Parse("10000000-0000-0000-0000-000000000013");
        builder.HasData(
            new CommerceHubCategoryAttribute
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000021"),
                CategoryId = laptopCategoryId,
                Name = "Brand",
                NameAr = "الماركة",
                Type = "select",
                IsRequired = true,
                IsFilterable = true,
                IsSearchable = true,
                OptionsJson = "[\"Apple\",\"Dell\",\"HP\",\"Lenovo\",\"ASUS\",\"Acer\",\"MSI\"]",
                SortOrder = 1
            },
            new CommerceHubCategoryAttribute
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000022"),
                CategoryId = laptopCategoryId,
                Name = "Processor",
                NameAr = "المعالج",
                Type = "select",
                IsRequired = false,
                IsFilterable = true,
                IsSearchable = true,
                OptionsJson = "[\"Intel Core i3\",\"Intel Core i5\",\"Intel Core i7\",\"Intel Core i9\",\"AMD Ryzen 5\",\"AMD Ryzen 7\",\"Apple M1\",\"Apple M2\",\"Apple M3\"]",
                SortOrder = 2
            },
            new CommerceHubCategoryAttribute
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000023"),
                CategoryId = laptopCategoryId,
                Name = "RAM",
                NameAr = "الذاكرة العشوائية",
                Type = "select",
                IsRequired = false,
                IsFilterable = true,
                IsSearchable = false,
                OptionsJson = "[\"4GB\",\"8GB\",\"16GB\",\"32GB\",\"64GB\"]",
                SortOrder = 3
            }
        );

        // Fashion - Men attributes
        var mensCategoryId = Guid.Parse("10000000-0000-0000-0000-000000000021");
        builder.HasData(
            new CommerceHubCategoryAttribute
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000031"),
                CategoryId = mensCategoryId,
                Name = "Size",
                NameAr = "المقاس",
                Type = "select",
                IsRequired = true,
                IsFilterable = true,
                IsSearchable = false,
                OptionsJson = "[\"XS\",\"S\",\"M\",\"L\",\"XL\",\"XXL\",\"3XL\"]",
                SortOrder = 1
            },
            new CommerceHubCategoryAttribute
            {
                Id = Guid.Parse("20000000-0000-0000-0000-000000000032"),
                CategoryId = mensCategoryId,
                Name = "Color",
                NameAr = "اللون",
                Type = "text",
                IsRequired = false,
                IsFilterable = true,
                IsSearchable = true,
                OptionsJson = null,
                SortOrder = 2
            }
        );
    }
}
