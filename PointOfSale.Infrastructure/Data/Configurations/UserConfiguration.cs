using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PointOfSale.Core.Entities;

namespace PointOfSale.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        
        builder.Property(u => u.Username)
            .IsRequired()
            .HasMaxLength(100);
        
        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(200);
        
        builder.Property(u => u.PasswordHash)
            .IsRequired();

        // The single current POS refresh-session binding is rotated atomically.
        // Competing refresh/login/logout writes must not silently resurrect a session.
        builder.Property(u => u.RefreshToken)
            .IsConcurrencyToken();
        
        builder.Property(u => u.FirstName)
            .IsRequired()
            .HasMaxLength(100);
        
        builder.Property(u => u.LastName)
            .IsRequired()
            .HasMaxLength(100);
        
        builder.Property(u => u.Phone)
            .HasMaxLength(50);
        
        builder.Ignore(u => u.FullName);
        
        builder.HasIndex(u => u.Username)
            .IsUnique();
        
        builder.HasIndex(u => u.Email)
            .IsUnique();
    }
}
