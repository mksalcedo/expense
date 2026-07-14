using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class RecurringRuleConfiguration : IEntityTypeConfiguration<RecurringRule>
{
    public void Configure(EntityTypeBuilder<RecurringRule> builder)
    {
        builder.ToTable("recurring_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Direction).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Frequency).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Amount).HasColumnType("numeric(12,2)");
        builder.HasOne(r => r.Account).WithMany().HasForeignKey(r => r.AccountId);
        builder.HasIndex(r => r.Active);
    }
}
