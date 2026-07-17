using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class PaymentDeferralConfiguration : IEntityTypeConfiguration<PaymentDeferral>
{
    public void Configure(EntityTypeBuilder<PaymentDeferral> builder)
    {
        builder.ToTable("payment_deferrals");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Note).HasMaxLength(500);
        builder.HasOne(d => d.Account).WithMany().HasForeignKey(d => d.AccountId);
        builder.HasIndex(d => new { d.AccountId, d.OriginalDate }).IsUnique();
    }
}
