using Agent.Domain.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agent.Infrastructure.Persistence.Configurations;

public sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations", table =>
            table.HasCheckConstraint("CK_Conversations_NextMessageSequence", "[NextMessageSequence] >= 0"));
        builder.HasKey(conversation => conversation.Id);
        builder.Property(conversation => conversation.Title).HasMaxLength(200).IsRequired();
        builder.Property(conversation => conversation.CreatedAt).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(conversation => conversation.UpdatedAt).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(conversation => conversation.LastMessageAt).HasColumnType("datetimeoffset(3)");
        builder.Property(conversation => conversation.NextMessageSequence).IsRequired();
        builder.Property(conversation => conversation.RowVersion).IsRowVersion();

        builder.HasIndex(conversation => new { conversation.UserId, conversation.UpdatedAt });
        builder.HasOne(conversation => conversation.User)
            .WithMany(user => user.Conversations)
            .HasForeignKey(conversation => conversation.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
