using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class DebtBalanceSnapshotConfiguration : IEntityTypeConfiguration<DebtBalanceSnapshot>
{
    public void Configure(EntityTypeBuilder<DebtBalanceSnapshot> builder)
    {
        builder.ToTable("debt_balance_snapshots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Balance).HasColumnType("numeric(12,2)");
        builder.HasOne(s => s.Account).WithMany().HasForeignKey(s => s.AccountId);
        builder.HasIndex(s => new { s.AccountId, s.AsOfDate });
    }
}
