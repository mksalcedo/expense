using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class SyncIssueConfiguration : IEntityTypeConfiguration<SyncIssue>
{
    public void Configure(EntityTypeBuilder<SyncIssue> builder)
    {
        builder.ToTable("sync_issues");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.MessageId).HasMaxLength(200);
        builder.Property(i => i.Subject).HasMaxLength(500);
        builder.Property(i => i.Reason).HasMaxLength(1000);
        builder.HasIndex(i => new { i.Source, i.MessageId }).IsUnique();
        builder.HasOne(i => i.ResolvedAmazonOrderItem).WithMany().HasForeignKey(i => i.ResolvedAmazonOrderItemId);
    }
}
