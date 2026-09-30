using Agent.Domain.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agent.Infrastructure.Persistence.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Email).HasMaxLength(256).IsRequired();
        builder.Property(user => user.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(user => user.UserName).HasMaxLength(64).IsRequired();
        builder.Property(user => user.NormalizedUserName).HasMaxLength(64).IsRequired();
        builder.Property(user => user.DisplayName).HasMaxLength(128).IsRequired();
        builder.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(user => user.CreatedAt).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(user => user.UpdatedAt).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(user => user.LastLoginAt).HasColumnType("datetimeoffset(3)");
        builder.Property(user => user.RowVersion).IsRowVersion();

        builder.HasIndex(user => user.NormalizedEmail).IsUnique();
        builder.HasIndex(user => user.NormalizedUserName).IsUnique();
    }
}
