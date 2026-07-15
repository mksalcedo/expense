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
        // ValueGeneratedNever forces EF to always send the explicit C# value on insert.
        // Without it, HasDefaultValue makes EF treat Direction.Income (the enum's CLR
        // default, 0) as "unset" and silently substitute the column default (Expense)
        // instead - the same class of bug as the earlier Category.IsActive corruption,
        // caught here by the test-first round-trip test before it ever hit real data.
        builder.Property(p => p.Direction).HasConversion<string>().HasMaxLength(20)
            .HasDefaultValue(Direction.Expense).ValueGeneratedNever();
        builder.HasOne(p => p.Category).WithMany().HasForeignKey(p => p.CategoryId);
        builder.HasOne(p => p.Account).WithMany().HasForeignKey(p => p.AccountId);
        builder.HasIndex(p => new { p.CategoryId, p.EffectiveFrom });
    }
}
