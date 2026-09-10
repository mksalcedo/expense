using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class MerchantRuleConfiguration : IEntityTypeConfiguration<MerchantRule>
{
    public void Configure(EntityTypeBuilder<MerchantRule> builder)
    {
        builder.ToTable("merchant_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.MerchantPattern).IsRequired().HasMaxLength(200);
        builder.Property(r => r.Direction).HasConversion<string>().HasMaxLength(20);
        builder.HasOne(r => r.Category).WithMany().HasForeignKey(r => r.CategoryId);
    }
}
