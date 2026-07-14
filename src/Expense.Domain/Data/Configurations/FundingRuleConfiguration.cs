using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class FundingRuleConfiguration : IEntityTypeConfiguration<FundingRule>
{
    public void Configure(EntityTypeBuilder<FundingRule> builder)
    {
        builder.ToTable("funding_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Strategy).IsRequired().HasMaxLength(50);
        builder.HasOne(r => r.Category).WithMany().HasForeignKey(r => r.CategoryId);
        builder.HasIndex(r => r.CategoryId).IsUnique();
    }
}
