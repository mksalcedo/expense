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

        // Two indexes over the same single column (OrderId) - the string passed to this
        // HasIndex overload is just the fluent model's internal identity key so EF treats
        // them as two distinct indexes instead of one being redefined; HasDatabaseName
        // below is what actually names the underlying Postgres index.
        builder.HasIndex(i => i.OrderId, "PlainOrderIdIndex").HasDatabaseName("ix_amazon_order_items_order_id");

        // At most one NeedsReview placeholder per order - a database-level backstop for
        // the application-level dedup check in AmazonImportService, which only queries
        // the database and so can't see another connection's uncommitted insert of the
        // same placeholder (confirmed live 2026-08-14: two overlapping app instances both
        // passed the "does this already exist" check before either had committed).
        builder.HasIndex(i => i.OrderId, "UniqueNeedsReviewPlaceholderPerOrderIndex")
            .IsUnique()
            .HasFilter("needs_review = true")
            .HasDatabaseName("ix_amazon_order_items_order_id_needs_review_placeholder");

        builder.HasIndex(i => i.ProductId); // pending-categorization filter
    }
}
