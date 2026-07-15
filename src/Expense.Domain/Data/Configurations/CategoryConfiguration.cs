using Expense.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Expense.Domain.Data.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
        // ValueGeneratedNever: without it, inserting a new Category with IsActive explicitly
        // set to false (the bool CLR default) would be silently overridden by the column
        // default (true) - the same EF gotcha hit with BudgetPeriod.Direction/Income.
        builder.Property(c => c.IsActive).HasDefaultValue(true).ValueGeneratedNever();
        builder.HasIndex(c => c.Name).IsUnique();
    }
}
