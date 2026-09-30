using Agent.Domain.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Agent.Infrastructure.Persistence.Configurations;

public sealed class ToolExecutionConfiguration : IEntityTypeConfiguration<ToolExecution>
{
    public void Configure(EntityTypeBuilder<ToolExecution> builder)
    {
        builder.ToTable("ToolExecutions", table =>
            table.HasCheckConstraint("CK_ToolExecutions_StepNumber", "[StepNumber] > 0"));
        builder.HasKey(execution => execution.Id);
        builder.Property(execution => execution.ToolName).HasMaxLength(128).IsRequired();
        builder.Property(execution => execution.Description).HasMaxLength(500);
        builder.Property(execution => execution.ArgumentsJson).HasColumnType("nvarchar(max)");
        builder.Property(execution => execution.Output).HasColumnType("nvarchar(max)");
        builder.Property(execution => execution.Error).HasMaxLength(2000);
        builder.Property(execution => execution.StartedAt).HasColumnType("datetimeoffset(3)").IsRequired();
        builder.Property(execution => execution.CompletedAt).HasColumnType("datetimeoffset(3)").IsRequired();

        builder.HasIndex(execution => new { execution.AssistantMessageId, execution.StepNumber });
        builder.HasOne(execution => execution.AssistantMessage)
            .WithMany(message => message.ToolExecutions)
            .HasForeignKey(execution => execution.AssistantMessageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
