using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class AmazonOrderItemConfiguration : IEntityTypeConfiguration<AmazonOrderItem>
{
    public void Configure(EntityTypeBuilder<AmazonOrderItem> builder)
    {
        builder.ToTable("amazon_order_items");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.OrderId).IsRequired().HasMaxLength(50);
        builder.Property(i => i.ItemTitle).IsRequired().HasMaxLength(500);
        builder.Property(i => i.Price).HasColumnType("numeric(12,2)");
        builder.Property(i => i.TaxAllocated).HasColumnType("numeric(12,2)");
        builder.Property(i => i.RefundAmount).HasColumnType("numeric(12,2)");
        builder.Property(i => i.SourceMessageId).HasMaxLength(100);
        builder.Property(i => i.NeedsReviewReason).HasMaxLength(500);
        builder.Property(i => i.OrderDetailsUrl).HasMaxLength(500);
        // RawEmailBody deliberately has no HasMaxLength - Npgsql maps it to an unbounded
        // text column, same as ImportRunProgressLine.Text, since a full email body can be
        // long and there's no reasonable fixed cap to pick.

        builder.HasOne(i => i.Product).WithMany().HasForeignKey(i => i.ProductId);
        builder.HasOne(i => i.Category).WithMany().HasForeignKey(i => i.CategoryId);

        builder.HasIndex(i => i.OrderId);
        builder.HasIndex(i => i.ProductId); // pending-categorization filter
    }
}
