using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class BudgetPeriodConfiguration : IEntityTypeConfiguration<BudgetPeriod>
{
    public void Configure(EntityTypeBuilder<BudgetPeriod> builder)
    {
        builder.ToTable("budget_periods");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Amount).HasColumnType("numeric(12,2)");
        builder.Property(p => p.Frequency).HasConversion<string>().HasMaxLength(20);
        builder.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryId);
        builder.HasIndex(p => new { p.CategoryId, p.EffectiveFrom });
    }
}
