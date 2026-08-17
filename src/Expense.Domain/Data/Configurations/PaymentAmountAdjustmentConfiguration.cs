using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class PaymentAmountAdjustmentConfiguration : IEntityTypeConfiguration<PaymentAmountAdjustment>
{
    public void Configure(EntityTypeBuilder<PaymentAmountAdjustment> builder)
    {
        builder.ToTable("payment_amount_adjustments");
        builder.HasKey(a => a.Id);
        builder.HasOne(a => a.Account).WithMany().HasForeignKey(a => a.AccountId);
        builder.HasOne(a => a.Category).WithMany().HasForeignKey(a => a.CategoryId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(a => new { a.AccountId, a.CategoryId, a.OriginalDate }).IsUnique();
    }
}
