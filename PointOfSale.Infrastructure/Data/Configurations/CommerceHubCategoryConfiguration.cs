using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class CommerceHubCategoryConfiguration : IEntityTypeConfiguration<CommerceHubCategory>
{
    public void Configure(EntityTypeBuilder<CommerceHubCategory> builder)
    {
        builder.ToTable("CommerceHubCategories");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.NameAr)
            .HasMaxLength(200);

        builder.Property(x => x.Slug)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.IconEmoji)
            .HasMaxLength(10);

        builder.Property(x => x.Level)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.UpdatedAtUtc);

        builder.HasIndex(x => x.Slug)
            .IsUnique()
            .HasDatabaseName("UQ_CommerceHubCategories_Slug");

        builder.HasIndex(x => new { x.ParentId, x.SortOrder })
            .HasDatabaseName("IX_CommerceHubCategories_ParentId_SortOrder");

        builder.HasOne(x => x.Parent)
            .WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        SeedCategories(builder);
    }

    private static void SeedCategories(EntityTypeBuilder<CommerceHubCategory> builder)
    {
        var seedDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var electronics = new CommerceHubCategory
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Name = "Electronics",
            NameAr = "إلكترونيات",
            Slug = "electronics",
            ParentId = null,
            Level = 0,
            IconEmoji = "📱",
            IsActive = true,
            SortOrder = 1,
            CreatedAtUtc = seedDate
        };

        var fashion = new CommerceHubCategory
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
            Name = "Fashion",
            NameAr = "أزياء",
            Slug = "fashion",
            ParentId = null,
            Level = 0,
            IconEmoji = "👗",
            IsActive = true,
            SortOrder = 2,
            CreatedAtUtc = seedDate
        };

        var homeGarden = new CommerceHubCategory
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000003"),
            Name = "Home & Garden",
            NameAr = "المنزل والحديقة",
            Slug = "home-garden",
            ParentId = null,
            Level = 0,
            IconEmoji = "🏠",
            IsActive = true,
            SortOrder = 3,
            CreatedAtUtc = seedDate
        };

        var tvs = new CommerceHubCategory
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000011"),
            Name = "TVs",
            NameAr = "تلفزيونات",
            Slug = "tvs",
            ParentId = electronics.Id,
            Level = 1,
            IconEmoji = "📺",
            IsActive = true,
            SortOrder = 1,
            CreatedAtUtc = seedDate
        };

        var mobiles = new CommerceHubCategory
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000012"),
            Name = "Mobile Phones",
            NameAr = "هواتف محمولة",
            Slug = "mobile-phones",
            ParentId = electronics.Id,
            Level = 1,
            IconEmoji = "📱",
            IsActive = true,
            SortOrder = 2,
            CreatedAtUtc = seedDate
        };

        var laptops = new CommerceHubCategory
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000013"),
            Name = "Laptops",
            NameAr = "لابتوب",
            Slug = "laptops",
            ParentId = electronics.Id,
            Level = 1,
            IconEmoji = "💻",
            IsActive = true,
            SortOrder = 3,
            CreatedAtUtc = seedDate
        };

        var mensFashion = new CommerceHubCategory
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000021"),
            Name = "Men",
            NameAr = "رجالي",
            Slug = "mens-fashion",
            ParentId = fashion.Id,
            Level = 1,
            IconEmoji = "👔",
            IsActive = true,
            SortOrder = 1,
            CreatedAtUtc = seedDate
        };

        var womensFashion = new CommerceHubCategory
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000022"),
            Name = "Women",
            NameAr = "نسائي",
            Slug = "womens-fashion",
            ParentId = fashion.Id,
            Level = 1,
            IconEmoji = "👗",
            IsActive = true,
            SortOrder = 2,
            CreatedAtUtc = seedDate
        };

        var furniture = new CommerceHubCategory
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000031"),
            Name = "Furniture",
            NameAr = "أثاث",
            Slug = "furniture",
            ParentId = homeGarden.Id,
            Level = 1,
            IconEmoji = "🛋️",
            IsActive = true,
            SortOrder = 1,
            CreatedAtUtc = seedDate
        };

        var kitchen = new CommerceHubCategory
        {
            Id = Guid.Parse("10000000-0000-0000-0000-000000000032"),
            Name = "Kitchen",
            NameAr = "مطبخ",
            Slug = "kitchen",
            ParentId = homeGarden.Id,
            Level = 1,
            IconEmoji = "🍳",
            IsActive = true,
            SortOrder = 2,
            CreatedAtUtc = seedDate
        };

        builder.HasData(
            electronics, fashion, homeGarden,
            tvs, mobiles, laptops,
            mensFashion, womensFashion,
            furniture, kitchen);
    }
}
