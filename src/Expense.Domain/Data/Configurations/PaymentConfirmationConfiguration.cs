using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class PaymentConfirmationConfiguration : IEntityTypeConfiguration<PaymentConfirmation>
{
    public void Configure(EntityTypeBuilder<PaymentConfirmation> builder)
    {
        builder.ToTable("payment_confirmations");
        builder.HasKey(c => c.Id);
        builder.HasOne(c => c.Account).WithMany().HasForeignKey(c => c.AccountId);
        builder.HasIndex(c => new { c.AccountId, c.OriginalDate }).IsUnique();
    }
}
