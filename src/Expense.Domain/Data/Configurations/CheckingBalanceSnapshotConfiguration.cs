using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class CheckingBalanceSnapshotConfiguration : IEntityTypeConfiguration<CheckingBalanceSnapshot>
{
    public void Configure(EntityTypeBuilder<CheckingBalanceSnapshot> builder)
    {
        builder.ToTable("checking_balance_snapshots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Balance).HasColumnType("numeric(12,2)");
        builder.HasIndex(s => s.AsOfDate);
    }
}
