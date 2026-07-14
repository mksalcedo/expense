using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> builder)
    {
        builder.ToTable("bank_transactions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Description).IsRequired().HasMaxLength(500);
        builder.Property(t => t.Merchant).HasMaxLength(200);
        builder.Property(t => t.Amount).HasColumnType("numeric(12,2)");
        builder.Property(t => t.ExternalId).HasMaxLength(200);
        builder.Property(t => t.ImportSource).IsRequired().HasMaxLength(50);
        builder.Property(t => t.DedupFingerprint).HasMaxLength(500);

        builder.HasOne(t => t.Account).WithMany().HasForeignKey(t => t.AccountId);
        builder.HasOne(t => t.Category).WithMany().HasForeignKey(t => t.CategoryId);

        // Review Queue's primary filter
        builder.HasIndex(t => t.CategoryId);

        builder.HasIndex(t => new { t.AccountId, t.ExternalId }).IsUnique()
            .HasFilter("external_id IS NOT NULL");
        builder.HasIndex(t => t.DedupFingerprint).IsUnique()
            .HasFilter("dedup_fingerprint IS NOT NULL");
    }
}
