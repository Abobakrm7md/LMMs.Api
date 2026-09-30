using Agent.Domain.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agent.Infrastructure.Persistence.Configurations;

public sealed class ConversationMessageConfiguration : IEntityTypeConfiguration<ConversationMessage>
{
    public void Configure(EntityTypeBuilder<ConversationMessage> builder)
    {
        builder.ToTable("ChatMessages", table =>
            table.HasCheckConstraint("CK_ChatMessages_SequenceNumber", "[SequenceNumber] > 0"));
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Role).HasConversion<byte>().IsRequired();
        builder.Property(message => message.Status).HasConversion<byte>().IsRequired();
        builder.Property(message => message.Content).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(message => message.ModelName).HasMaxLength(128);
        builder.Property(message => message.CreatedAt).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(message => message.UpdatedAt).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(message => message.RowVersion).IsRowVersion();

        builder.HasIndex(message => new { message.ConversationId, message.SequenceNumber }).IsUnique();
        builder.HasOne(message => message.Conversation)
            .WithMany(conversation => conversation.Messages)
            .HasForeignKey(message => message.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
